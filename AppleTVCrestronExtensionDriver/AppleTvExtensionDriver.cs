// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.EntityModel.Logging;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;
using Crestron.DeviceDrivers.SDK.EntityModel.Data;

namespace AppleTV.CrestronDriver.Extension;

/// <summary>
/// Crestron Home Entity V2 extension driver providing an Apple TV app-launcher tile plus a full
/// remote control (with volume/mute exposed only when the paired Apple TV advertises support for
/// it). This driver never pairs with, discovers, or connects to the Apple TV itself and never
/// references the Companion Link control library at all; the only configuration it accepts is
/// the Apple TV's name, which it uses to look up the same-named Apple TV Companion Link video
/// server driver's stored <see cref="AppleTvStoredDevice.UniqueId"/> (see
/// <see cref="AppleTvStoredDevice.VIDEO_SERVER_BASE_MODEL"/>), derive that video server's loopback
/// bridge port candidates (<see cref="AppleTvBridgePort"/>), and connect to it as a bridge client
/// (<see cref="AppleTvBridgeClient"/>). The video server owns the single live Companion Link
/// pairing/session for that Apple TV; every command this driver issues and every event it reflects
/// travels over that bridge as a tokenized line (see <see cref="AppleTvBridgeProtocol"/>).
/// </summary>
/// <remarks>
/// This class is split across several partial-class files, grouped by concern:
/// <list type="bullet">
/// <item><description><c>AppleTvExtensionDriver.cs</c> (this file): construction, configuration, bridge connection lifecycle, and shared helpers.</description></item>
/// <item><description><c>AppleTvExtensionDriver.Properties.cs</c>: Entity Model property declarations.</description></item>
/// <item><description><c>AppleTvExtensionDriver.Commands.cs</c>: Entity Model commands (remote control and app launching), each sending a bridge <c>CMD:</c> line.</description></item>
/// </list>
/// </remarks>
public sealed partial class AppleTvExtensionDriver : ReflectedAttributeDriverEntity
	{
	private readonly DriverControllerLogger _logger;
	private readonly string _logControllerId;
	private readonly UiDefinitionProperty _uiDefinition;
	private readonly object _stateLock = new ();
	private readonly List<(string BundleId, string Name)> _apps = [];

	private CancellationTokenSource _connectCancellationTokenSource;
	private AppleTvBridgeClient _bridgeClient;
	private string _appleTvName = string.Empty;
	private string _lastUniqueId = string.Empty;
	private int _refreshStatusRequestVersion;
	private readonly Timer _appRefreshTimer;
	private static readonly TimeSpan _appRefreshInterval = TimeSpan.FromMinutes (5);

	/// <summary>
	/// Initializes the Apple TV extension driver.
	/// </summary>
	/// <param name="creationArgs">The Crestron runtime creation arguments.</param>
	/// <param name="resources">The driver implementation resources resolved by the SDK.</param>
	public AppleTvExtensionDriver (DriverControllerCreationArgs creationArgs, DriverImplementationResources resources)
		: base (DriverController.RootControllerId)
		{
		_logger = creationArgs.Logger;
		_logControllerId = creationArgs.DriverId;

		var configurationArgs = DataDrivenConfigurationControllerArgs.FromResources (creationArgs, resources, ControllerId);
		ConfigurationController = new DelegateDataDrivenConfigurationController (configurationArgs, ApplyConfigurationItems, null, null);

		_uiDefinition = UiDefinitionProperty.LoadFromDirectoryIfExists (creationArgs.DriverDataDirectoryPath, resources.InitLogger, LogEntryLevel.Error);
		if (_uiDefinition != null)
			{
			AddProperty (this, UiDefinitionProperty.Name, _uiDefinition);
			}

		try
			{
			AddCommand (this, ExtensionDoCommandExecutor.CommandName, new ExtensionDoCommandExecutor (GetCommand, resources.Logger));
			AddCommand (this, ExtensionSetPropertyValueExecutor.CommandName, new ExtensionSetPropertyValueExecutor (GetCommand, resources.Logger));
			}
		catch (Exception ex)
			{
			LogWarning ("Failed to register extension UI command helpers: " + ex.Message);
			}

		DeviceLabel = "Apple TV Remote";
		TileIcon = "icRemoteRegular";
		VolumeControlSupported = false;
		PowerStatusLabel = "Unknown";
		SetStatusSummary ("Waiting for configuration");
		ApplyAppList ([]);

		TryPublishUiDefinition ();

		// Crestron Home has no built-in periodic-refresh mechanism for extension driver UI
		// data, so the app list is kept current here instead of relying on a user-triggered
		// refresh button (which has been removed): every AppRefreshInterval, request a fresh
		// app list from the bridge if a connection is currently available.
		_appRefreshTimer = new Timer (OnAppRefreshTimerTick, null, _appRefreshInterval, _appRefreshInterval);
		}

	internal DataDrivenConfigurationController ConfigurationController
		{
		get;
		}

	/// <inheritdoc/>
	public override void Dispose ()
		{
		_appRefreshTimer.Dispose ();
		StopConnecting ();
		DisposeBridgeClient ();
		base.Dispose ();
		}

	private ConfigurationItemErrors ApplyConfigurationItems (
		DataDrivenConfigurationController.ApplyConfigurationAction action,
		string stepId,
		IDictionary<string, DriverEntityValue?> values)
		{
		if (action == DataDrivenConfigurationController.ApplyConfigurationAction.ClearValues)
			{
			StopConnecting ();
			DisposeBridgeClient ();
			_appleTvName = string.Empty;
			_lastUniqueId = string.Empty;
			SetUnavailableState ("Configuration cleared");
			return null;
			}

		string appleTvName = GetString (values, "AppleTvName");
		if (string.IsNullOrWhiteSpace (appleTvName))
			{
			return new ConfigurationItemErrors (
				new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase) { { "AppleTvName", "An Apple TV name is required." } },
				"Enter the name of the Apple TV to control.");
			}

		AppleTvStoredDevice device = AppleTvStoredDevice.LoadForName (appleTvName, AppleTvStoredDevice.VIDEO_SERVER_BASE_MODEL);
		if (device is null || string.IsNullOrWhiteSpace (device.UniqueId))
			{
			return new ConfigurationItemErrors (
				new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase)
					{
					{ "AppleTvName", $"'{appleTvName}' is not configured on the Apple TV Companion Link video server driver. Configure and pair it there first." },
					},
				"Configure the matching Apple TV Companion Link video server driver first.");
			}

		_appleTvName = appleTvName;
		DeviceLabel = appleTvName;
		SetStatusSummary ("Connecting...");
		StartConnecting (device.UniqueId);
		return null;
		}

	private void OnAppRefreshTimerTick (object state) =>
		_ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_REFRESH_APPS}", "automatically refresh apps");

	private void StartConnecting (string uniqueId)
		{
		StopConnecting ();
		_lastUniqueId = uniqueId;
		var cancellationTokenSource = new CancellationTokenSource ();
		_connectCancellationTokenSource = cancellationTokenSource;
		_ = Task.Run (() => ConnectAsync (uniqueId, cancellationTokenSource.Token));
		}

	private void StopConnecting ()
		{
		CancellationTokenSource cancellationTokenSource;
		lock (_stateLock)
			{
			cancellationTokenSource = _connectCancellationTokenSource;
			_connectCancellationTokenSource = null;
			}

		if (cancellationTokenSource is null)
			{
			return;
			}

		try
			{
			cancellationTokenSource.Cancel ();
			}
		catch
			{
			}

		cancellationTokenSource.Dispose ();
		}

	private void DisposeBridgeClient ()
		{
		AppleTvBridgeClient client;
		lock (_stateLock)
			{
			client = _bridgeClient;
			_bridgeClient = null;
			}

		if (client is null)
			{
			return;
			}

		client.LineReceived -= HandleBridgeLine;
		client.Disconnected -= HandleBridgeDisconnected;
		client.Dispose ();
		}

	// Connects to the video server's loopback bridge for this Apple TV, retrying with backoff
	// (mirroring the previous direct-Companion-Link reconnect behavior) since the video server
	// driver may not have started its bridge server yet (e.g. it is still discovering/pairing) or
	// may be mid-reinitialization.
	private async Task ConnectAsync (string uniqueId, CancellationToken cancellationToken)
		{
		int[] candidates = [.. AppleTvBridgePort.GetPortCandidates (uniqueId)];
		int attempt = 0;
		while (!cancellationToken.IsCancellationRequested)
			{
			attempt++;
			foreach (int port in candidates)
				{
				if (cancellationToken.IsCancellationRequested)
					{
					return;
					}

				var client = new AppleTvBridgeClient (LogInformation);
				try
					{
					await client.ConnectAsync (port, cancellationToken).ConfigureAwait (false);
					if (cancellationToken.IsCancellationRequested)
						{
						client.Dispose ();
						return;
						}

					client.LineReceived += HandleBridgeLine;
					client.Disconnected += HandleBridgeDisconnected;
					lock (_stateLock)
						{
						_bridgeClient = client;
						}

					LogInformation ($"Connected to Apple TV bridge on 127.0.0.1:{port}.");
					OnlineIndicatorIsOnline = true;
					ReadyIndicatorIsReady = true;
					SetStatusSummary ("Connected");
					await client.SendCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_REFRESH_STATUS}").ConfigureAwait (false);
					WatchForRefreshStatusReply ();
					await client.SendCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_REFRESH_APPS}").ConfigureAwait (false);
					return;
					}
				catch (Exception exception)
					{
					client.Dispose ();
					LogInformation ($"Bridge connect attempt on port {port} failed: {exception.GetType ().FullName}: {exception.Message}");
					}
				}

			if (cancellationToken.IsCancellationRequested)
				{
				return;
				}

			int delaySeconds = Math.Min (30, attempt * 5);
			LogInformation ($"Could not reach the Apple TV bridge on any candidate port; retrying in {delaySeconds} second(s).");
			try
				{
				await Task.Delay (TimeSpan.FromSeconds (delaySeconds), cancellationToken).ConfigureAwait (false);
				}
			catch (TaskCanceledException)
				{
				return;
				}
			}
		}

	// Diagnostic-only: CMD:REFRESHSTATUS is fire-and-forget over the bridge, so there is
	// otherwise no visible indication from the extension driver's own logs if the video server
	// never replies with an EVT:SYSSTATUS: line (e.g. because RefreshStatusAsync's own fetch
	// failed or the command was never received). Logs an explicit warning if no such line has
	// arrived a few seconds after this connect's request was sent, so PowerStatusLabel staying
	// "Unknown" can be attributed to a missing reply rather than a silent success.
	private void WatchForRefreshStatusReply ()
		{
		int requestedVersion = Volatile.Read (ref _refreshStatusRequestVersion);
		_ = Task.Run (async () =>
			{
			await Task.Delay (TimeSpan.FromSeconds (5)).ConfigureAwait (false);
			if (Volatile.Read (ref _refreshStatusRequestVersion) == requestedVersion)
				{
				LogWarning ("No EVT:SYSSTATUS: reply was received within 5 seconds of sending CMD:REFRESHSTATUS; PowerStatusLabel will remain stale.");
				}
			});
		}

	private void HandleBridgeDisconnected ()
		{
		// Report offline immediately, before anything else (including disposing the dead
		// client or attempting to reconnect), so nothing downstream can observe a stale
		// OnlineIndicatorIsOnline=true/stale status while the bridge connection is actually
		// down, and so SendBridgeCommandAsync's IsOnline check reliably blocks commands from
		// the moment the connection is lost.
		SetUnavailableState ("Reconnecting...");
		DisposeBridgeClient ();
		LogError ("Lost connection to the Apple TV bridge; reconnecting.");
		string uniqueId = _lastUniqueId;
		if (!string.IsNullOrWhiteSpace (uniqueId))
			{
			StartConnecting (uniqueId);
			}
		}

	// Applies a single tokenized bridge event line (see AppleTvBridgeProtocol) to this driver's
	// Entity Model properties, mirroring how AppleTvVideoServerProtocol itself would react to the
	// same underlying Companion Link event.
	private void HandleBridgeLine (string line)
		{
		LogInformation ($"Bridge line received: '{line}'");

		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (line);
		switch (result.Kind)
			{
			case AppleTvExtensionDriverLogic.BridgeLineKind.Connected:
				OnlineIndicatorIsOnline = true;
				ReadyIndicatorIsReady = true;
				SetStatusSummary ("Connected");
				return;

			case AppleTvExtensionDriverLogic.BridgeLineKind.Disconnected:
				OnlineIndicatorIsOnline = false;
				ReadyIndicatorIsReady = false;
				SetStatusSummary ("Apple TV disconnected");
				return;

			case AppleTvExtensionDriverLogic.BridgeLineKind.Power:
				PowerIsOn = result.BoolValue;
				return;

			case AppleTvExtensionDriverLogic.BridgeLineKind.SystemStatus:
				_ = Interlocked.Increment (ref _refreshStatusRequestVersion);
				// PowerStatusLabel reflects the Apple TV's own reported system status (e.g.
				// Awake/Asleep/Idle) and is shown via its own dedicated UI binding
				// (PowerToggle's secondarylabel). StatusSummary/TileDisplay are a distinct
				// concept - the bridge connection status (Connected/Reconnecting/disconnected)
				// - and must not be overwritten here.
				PowerStatusLabel = result.StringValue;
				return;

			case AppleTvExtensionDriverLogic.BridgeLineKind.VolumeSupported:
				VolumeControlSupported = result.BoolValue;
				return;

			case AppleTvExtensionDriverLogic.BridgeLineKind.Mute:
				MuteIsOn = result.BoolValue;
				return;

			case AppleTvExtensionDriverLogic.BridgeLineKind.Apps:
				LogInformation ($"Decoded {result.Apps.Count} app(s) from bridge apps event.");
				ApplyAppList (result.Apps);
				return;

			// Mirrors AppleTv.Remote.Wpf's reactive TextInputDialog: KeyboardFocused gates the
			// textentry control group's visibility in UiDefinition.xml, showing it only while the
			// Apple TV's on-screen keyboard actually has focus.
			case AppleTvExtensionDriverLogic.BridgeLineKind.KeyboardFocus:
				KeyboardFocused = result.BoolValue;
				// The Apple TV's on-screen keyboard is gone once it drops the text-input request, so
				// the textentry control (and any previously-entered text) is hidden/cleared along
				// with it, mirroring AppleTv.Remote.Wpf's MainViewModel.HideTextInput.
				if (!result.BoolValue)
					{
					ApplyKeyboardTextFromDevice (string.Empty);
					}

				return;

			case AppleTvExtensionDriverLogic.BridgeLineKind.Text:
				ApplyKeyboardTextFromDevice (result.StringValue);
				return;
			}
		}

	private void ApplyAppList (IReadOnlyList<(string BundleId, string Name)> apps)
		{
		apps = AppleTvExtensionDriverLogic.SortApps (apps);

		lock (_stateLock)
			{
			if (_apps.Count == apps.Count && _apps.SequenceEqual (apps))
				{
				return;
				}

			_apps.Clear ();
			_apps.AddRange (apps);
			}

		var items = new AppleTvAppItem[apps.Count];
		for (int index = 0; index < apps.Count; index++)
			{
			(string bundleId, string name) = apps[index];
			items[index] = new AppleTvAppItem (bundleId, name);
			}

		// AppList and SelectedApp's setters notify only when the value actually changes (see
		// SetAndNotify), so assigning them here is sufficient - no separate explicit
		// NotifyPropertyChanged calls are needed.
		AppList = items;
		(bool shouldChange, string bundleIdToSelect, string nameToSelect) = AppleTvExtensionDriverLogic.DetermineSelection (apps, SelectedApp);
		if (shouldChange)
			{
			SelectedApp = bundleIdToSelect;
			SelectedAppName = nameToSelect;
			}

		LogInformation ($"ApplyAppList applied {apps.Count} app(s); AppList.Length={AppList?.Length ?? 0}, SelectedApp='{SelectedApp}'.");
		}

	private static string GetString (IDictionary<string, DriverEntityValue?> values, string key)
		{
		if (values == null || !values.TryGetValue (key, out DriverEntityValue? value) || !value.HasValue)
			{
			return null;
			}

		return value.Value.ToString ();
		}

	private void SetAndNotify (string propertyId, string value, ref string backingField)
		{
		value ??= string.Empty;
		lock (_stateLock)
			{
			if (string.Equals (backingField, value, StringComparison.Ordinal))
				{
				return;
				}

			backingField = value;
			}

		NotifyPropertyChanged (propertyId, new DriverEntityValue (value));
		}

	private void SetAndNotify (string propertyId, bool value, ref bool backingField)
		{
		lock (_stateLock)
			{
			if (backingField == value)
				{
				return;
				}

			backingField = value;
			}

		NotifyPropertyChanged (propertyId, new DriverEntityValue (value));
		}

	private void SetAndNotify (string propertyId, AppleTvAppItem[] value, ref AppleTvAppItem[] backingField)
		{
		value ??= [];
		lock (_stateLock)
			{
			if (ReferenceEquals (backingField, value))
				{
				return;
				}

			backingField = value;
			}

		NotifyPropertyChanged (propertyId, CreateValueForObjects (value));
		}

	private void SetUnavailableState (string statusSummary)
		{
		OnlineIndicatorIsOnline = false;
		ReadyIndicatorIsReady = false;
		SetStatusSummary (statusSummary);
		}

	// Keeps the tile's own status text (TileDisplay, bound to the <tile status="{tileDisplay}">
	// attribute in UiDefinition.xml) mirroring the driver's actual current status, rather than a
	// static placeholder or the configured Apple TV name, so the Crestron Home tile always shows
	// what StatusSummary/PowerStatusLabel show on the detail page.
	private void SetStatusSummary (string statusSummary)
		{
		StatusSummary = statusSummary;
		TileDisplay = statusSummary;
		}

	private void TryPublishUiDefinition ()
		{
		if (_uiDefinition == null)
			{
			return;
			}

		DriverEntityValue? uiDefinitionValue = _uiDefinition.GetValue (null, null);
		if (uiDefinitionValue.HasValue)
			{
			NotifyPropertyChanged (UiDefinitionProperty.Name, uiDefinitionValue.Value);
			}
		}

	[Conditional ("DEBUG")]
	private void DebugLog (string message) => LogInformation (message);

	private void LogWarning (string message) => _logger?.Log (_logControllerId, LogEntryLevel.Warning, message);

	private void LogError (string message) => _logger?.Log (_logControllerId, LogEntryLevel.Error, message);

	private void LogInformation (string message) => _logger?.Log (_logControllerId, LogEntryLevel.Info, message);
	}
