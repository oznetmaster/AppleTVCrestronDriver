using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AppleTvControlLibrary.Discovery.Companion;

using Crestron.SimplSharp;
using Crestron.RAD.Common.Enums;
using Crestron.RAD.Common.Interfaces;
using Crestron.RAD.Common.Transports;
using Crestron.RAD.DeviceTypes.VideoServer;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Provides Crestron Home Video Server control for a paired Apple TV through Companion Link.
/// </summary>
public sealed class AppleTvVideoServer : ABasicVideoServer, ICloudConnected, ISerial
	{
	private const string STORED_DEVICE_SETTING_KEY = "AppleTvStoredDevice";
	private const string PAIRING_NOTICE_ATTRIBUTE_ID = "AppleTvPairingNotice";
	private const string PAIRING_PIN_ATTRIBUTE_ID = "PairingPin";
	private const string PAIR_NOW_ATTRIBUTE_ID = "PairNow";

	private AppleTvNoOpTransport _transport;
	private AppleTvStoredDevice _storedDevice;

	// Pairing state, and the configure gate, live in the static singleton
	// (AppleTvPairingSessionState.Instance) rather than on instance fields,
	// because Crestron Home reinitializes (disposes and recreates) this driver
	// instance whenever a configuration attribute is applied - including
	// PairNow and PairingPin themselves. Instance fields would be torn down or
	// fail to serialize across that recreation: a pairing handshake could race
	// a second BeginAsync against the Apple TV instead of resuming the
	// original one, and a discovery scan started by an older instance could
	// resume after a newer instance's pairing already completed and
	// overwrite the just-saved paired credentials with a stale record.

	/// <summary>
	/// Initializes the Video Server driver and begins connection or pairing for the configured Apple TV.
	/// </summary>
	public void Initialize ()
		{
		LogDiagnostic ("Initializing Apple TV Companion Video Server.");
		_transport = new AppleTvNoOpTransport
			{
			EnableLogging = InternalEnableLogging,
			CustomLogger = InternalCustomLogger
			};
		ConnectionTransport = _transport;

		var protocol = new AppleTvVideoServerProtocol (ConnectionTransport, Id)
			{
			EnableLogging = InternalEnableLogging,
			CustomLogger = InternalCustomLogger
			};
		protocol.AppleTvNameChanged += appleTvName => _ = HandleAppleTvNameChangedAsync (protocol, appleTvName);
		protocol.PairingPinChanged += pairingPin => _ = HandlePairingPinChangedAsync (protocol, pairingPin);
		protocol.PairNowRequested += () => _ = HandlePairNowRequestedAsync (protocol);
		protocol.StateChange += StateChange;
		protocol.RxOut += SendRxOut;
		protocol.Initialize (VideoServerData);
		VideoServerProtocol = protocol;

		// PairNow, AppleTvPairingNotice, and PairingPin are declared statically in the
		// driver's json manifest, so they always exist and are never added or removed at
		// runtime. Any in-flight pairing session survives this reinitialization because
		// it lives in the static AppleTvPairingSessionState singleton rather than on
		// this instance.
		_ = ConfigureAppleTvAsync (protocol, protocol.AppleTvName);
		}

	/// <summary>
	/// Releases the active Companion Link session and any pending pairing session.
	/// </summary>
	public override void Dispose ()
		{
		// Do not dispose the pairing session, its gate, or the configure gate
		// here: they are owned by the static AppleTvPairingSessionState
		// singleton so in-flight work survives this instance being recreated
		// by a host config reinit. The pairing session is only released by
		// ClearPairing when pairing actually completes, fails, or is
		// superseded.
		//
		// If these gates were disposed here, a still-running task from this
		// instance (e.g. ConfigureAppleTvAsync awaiting a discovery scan of up
		// to five seconds) would throw ObjectDisposedException when it later
		// tries to Release() a disposed SemaphoreSlim. SemaphoreSlim does not
		// hold an unmanaged handle unless AvailableWaitHandle is used (it is
		// not), so leaving them undisposed here is safe and simply lets them
		// be collected once no longer referenced.
		base.Dispose ();
		}

	// These are invoked as fire-and-forget from synchronous RAD SDK event delegates
	// (Action/Action<string>), which offer no way to await a result back into
	// SetUserAttribute. Every path must therefore be wrapped in try/catch so that
	// nothing throws synchronously into the SDK's callback and no exception,
	// synchronous or asynchronous, is ever left unobserved.
	private async Task HandleAppleTvNameChangedAsync (AppleTvVideoServerProtocol protocol, string appleTvName)
		{
		try
			{
			LogDiagnostic ($"Apple TV name configured as '{appleTvName}'.");

			// Crestron Home replays every configuration attribute, including
			// AppleTvName with its unchanged value, whenever it reinitializes the
			// driver instance - including reinits caused by PairNow/PairingPin
			// themselves. Only clear an active pairing session when the name has
			// genuinely changed; otherwise this replay would tear down the very
			// pairing handshake that PairNow/PairingPin just started or completed.
			AppleTvPairingSessionState session = AppleTvPairingSessionState.Instance;
			if (session.Pairing is not null && !string.Equals (session.Name, appleTvName, StringComparison.OrdinalIgnoreCase))
				{
				ClearPairing ();
				}

			await ConfigureAppleTvAsync (protocol, appleTvName).ConfigureAwait (false);
			}
		catch (Exception exception)
			{
			LogException (exception);
			}
		}

	private async Task HandlePairingPinChangedAsync (AppleTvVideoServerProtocol protocol, string pairingPin)
		{
		try
			{
			LogDiagnostic ($"Pairing PIN received ({pairingPin.Length} digits).");
			await CompletePairingAsync (protocol, pairingPin).ConfigureAwait (false);
			}
		catch (Exception exception)
			{
			LogException (exception);
			}
		}

	private async Task HandlePairNowRequestedAsync (AppleTvVideoServerProtocol protocol)
		{
		try
			{
			LogDiagnostic ("Pair Now was requested.");
			await BeginPairingAsync (protocol).ConfigureAwait (false);
			}
		catch (Exception exception)
			{
			LogException (exception);
			}
		}

	private async Task ConfigureAppleTvAsync (AppleTvVideoServerProtocol protocol, string appleTvName)
		{
		AppleTvPairingSessionState session = AppleTvPairingSessionState.Instance;

		// Crestron Home can recreate this driver instance again while an older
		// instance's BeginPairingAsync/CompletePairingAsync (guarded by
		// session.Gate) is still in flight - e.g. right after PairNow or
		// PairingPin triggers a reinit before the pairing handshake or its
		// credential save has finished. Without waiting on the same gate here,
		// this (now current) instance's configure pass can run concurrently,
		// see the shared record as not-yet-paired, skip connecting, and return -
		// while the older, no-longer-current instance goes on to connect
		// successfully a moment later on an instance Crestron Home no longer
		// tracks, leaving the device shown offline despite a fully successful
		// pairing/connection sequence in the logs. Waiting here first ensures
		// this instance observes the just-saved paired credentials and is the
		// one that actually connects.
		await session.Gate.WaitAsync ().ConfigureAwait (false);
		session.Gate.Release ();

		await session.ConfigureGate.WaitAsync ().ConfigureAwait (false);
		try
			{
			AppleTvStoredDevice device = LoadStoredDevice ();

			// If the stored identity - paired or only a discovery record - no longer
			// matches the configured Apple TV name, the user has typed a different name
			// (or renamed the target device). Discard the stale record, including a
			// paired one, so the new name drives a fresh lookup instead of silently
			// reconnecting to the previously configured device using its old saved
			// endpoint and leaving the driver reporting online for the wrong Apple TV.
			if (device is not null
				&& !string.IsNullOrWhiteSpace (appleTvName)
				&& !string.Equals (device.Name, appleTvName, StringComparison.OrdinalIgnoreCase))
				{
				LogDiagnostic (device.IsPaired
					? $"Configured Apple TV name '{appleTvName}' no longer matches the paired identity '{device.Name}'; disconnecting and discarding the stale paired record."
					: $"Configured Apple TV name '{appleTvName}' no longer matches the discovered identity '{device.Name}'; discarding the stale discovery record.");
				if (device.IsPaired)
					{
					protocol.SetCompanionConnectionState (false);
					}

				device = null;
				}

			if (device is null)
				{
				if (string.IsNullOrWhiteSpace (appleTvName))
					{
					LogDiagnostic ("No Apple TV name is configured and device settings do not contain a discovered or paired identity.");
					return;
					}

				device = AppleTvStoredDevice.LoadForName (appleTvName);
				if (device is not null)
					{
					SaveStoredDevice (device);
					SetAppleTvNameStatus (device.IsPaired
						? "Existing paired credentials were found for this Apple TV name."
						: "A previously discovered identity was found for this Apple TV name.");
					LogDiagnostic ($"Initialized device settings from the shared record for '{device.Name}'.");
					}
				}

			if (device is not null && device.IsPaired)
				{
				try
					{
					LogDiagnostic ($"Attempting saved endpoint for '{device.Name}' at {device.Address}:{device.Port}.");
					await ConnectCompanionAsync (protocol, device, device.Address, device.Port).ConfigureAwait (false);
					LogDiagnostic ($"Connected to '{device.Name}' using its saved endpoint.");
					return;
					}
				catch (Exception exception)
					{
					LogDiagnostic ($"Saved endpoint failed for '{device.Name}'; starting endpoint recovery.");
					LogException (exception);
					}

				appleTvName = device.Name;
				}

			// The stored identity is an unpaired discovery record that already matches the
			// configured name (checked above; a mismatch would have set device to null).
			// Discovery already ran once to resolve this name; do not run it again just
			// because Crestron Home replayed AppleTvName on a reinit. Discovery should only
			// run again if the name genuinely changes (handled above), the paired endpoint
			// fails to connect (handled above), or pairing itself fails (handled in
			// CompletePairingAsync/BeginPairingAsync, which reuse this same saved record).
			if (device is not null && !device.IsPaired)
				{
				LogDiagnostic ($"'{device.Name}' was already discovered and awaits Pair Now; skipping a redundant discovery scan.");
				return;
				}

			if (string.IsNullOrWhiteSpace (appleTvName))
				{
				LogDiagnostic ("A paired Apple TV endpoint failed, but no Apple TV name is available for discovery recovery.");
				return;
				}

			CompanionDiscoveryResult discovered;
			if (device is null)
				{
				LogDiagnostic ($"Discovering '{appleTvName}' for up to five seconds.");
				discovered = await MulticastCompanionDiscovery.DiscoveryAsync (appleTvName, TimeSpan.FromSeconds (5)).ConfigureAwait (false);
				}
			else
				{
				LogDiagnostic ("Discovering the known Apple TV by its saved identity for up to five seconds.");
				discovered = (await new MulticastCompanionDiscovery ().ScanAsync (TimeSpan.FromSeconds (5)).ConfigureAwait (false))
					.FirstOrDefault (result => string.Equals (result.UniqueId, device.UniqueId, StringComparison.Ordinal));
				}

			if (discovered is null || discovered.Address is null)
				{
				LogDiagnostic (device is null
					? $"Discovery did not find '{appleTvName}'."
					: "Discovery did not find the known Apple TV identity.");
				LogException (new InvalidOperationException ("The required Apple TV was not found by Companion Link discovery."));
				if (device is null)
					{
					SetAppleTvNameStatus ($"No Apple TV named '{appleTvName}' was found. Check the name and ensure the Apple TV is online on the local network.");
					}

				return;
				}

			LogDiagnostic ($"Discovery resolved '{discovered.Name}' at {discovered.Address}:{discovered.Port}.");

			if (device is not null && device.IsPaired)
				{
				if (string.IsNullOrWhiteSpace (device.UniqueId) || !string.Equals (discovered.UniqueId, device.UniqueId, StringComparison.Ordinal))
					{
					LogDiagnostic ($"Discovered identity for '{discovered.Name}' does not match the stored pairing.");
					LogException (new InvalidOperationException ("The discovered Apple TV does not match the paired device identity."));
					return;
					}

				string discoveredAddress = discovered.Address.ToString ();
				bool endpointChanged = !string.Equals (device.Address, discoveredAddress, StringComparison.Ordinal)
					|| device.Port != discovered.Port
					|| !string.Equals (device.Name, discovered.Name, StringComparison.Ordinal);

				device.Address = discoveredAddress;
				device.Port = discovered.Port;
				device.Name = discovered.Name;
				if (endpointChanged)
					{
					AppleTvStoredDevice.Save (device);
					LogDiagnostic ($"Saved endpoint refreshed for '{device.Name}'; reconnecting.");
					}
				else
					{
					LogDiagnostic ($"Discovered endpoint for '{device.Name}' is unchanged; reconnecting without rewriting stored credentials.");
					}

				SaveStoredDevice (device);
				await ConnectCompanionAsync (protocol, device, device.Address, device.Port).ConfigureAwait (false);
				LogDiagnostic ($"Connected to '{device.Name}' after endpoint recovery.");
				return;
				}

			// The Apple TV was discovered on the network but has no stored pairing credentials
			// yet. If pairing was started and completed on another (newer) driver instance
			// while this discovery scan was in flight, a paired record now exists for this
			// unique id; do not clobber it with this stale, unpaired discovery record.
			AppleTvStoredDevice existingDevice = AppleTvStoredDevice.LoadForName (appleTvName);
			if (existingDevice is not null && existingDevice.IsPaired)
				{
				LogDiagnostic ($"Discovery for '{appleTvName}' completed after pairing already succeeded elsewhere; keeping the paired credentials.");
				SaveStoredDevice (existingDevice);
				return;
				}

			// Persist the discovered identity (name/address/port/unique id) so pairing,
			// whenever the user initiates it, does not require another discovery pass. Source
			// routing works regardless of the reported connection state, so the driver is kept
			// offline here and only reports connected once a real Companion session is
			// established (pairing complete and/or ConnectCompanionAsync succeeds).
			var discoveredDevice = new AppleTvStoredDevice
				{
				Address = discovered.Address.ToString (),
				Port = discovered.Port,
				Name = discovered.Name,
				UniqueId = discovered.UniqueId ?? string.Empty,
				};
			AppleTvStoredDevice.Save (discoveredDevice);
			SaveStoredDevice (discoveredDevice);
			SetAppleTvNameStatus ($"'{discovered.Name}' was found on the network. Press Pair Now to complete pairing.");
			LogDiagnostic ($"Discovered but unpaired identity persisted for '{discovered.Name}'; awaiting Pair Now.");
			}
		catch (Exception exception)
			{
			LogException (exception);
			SetAppleTvNameStatus ($"The Apple TV name could not be validated: {exception.Message}");
			}
		finally
			{
			session.ConfigureGate.Release ();
			}
		}

	/// <summary>
	/// Starts Companion Link pairing using the persisted discovery identity (address/port),
	/// requiring no additional network discovery, and shows the PIN entry attributes.
	/// </summary>
	private async Task BeginPairingAsync (AppleTvVideoServerProtocol protocol)
		{
		AppleTvPairingSessionState session = AppleTvPairingSessionState.Instance;
		await session.Gate.WaitAsync ().ConfigureAwait (false);
		try
			{
			AppleTvStoredDevice device = LoadStoredDevice ();
			if (device is null || device.IsPaired || string.IsNullOrWhiteSpace (device.Address))
				{
				LogDiagnostic ("Pair Now was requested, but no discovered (unpaired) Apple TV identity is available.");
				return;
				}

			// If a pairing handshake is already active for this device (started by
			// an instance the host has since recreated), do not start a second,
			// competing BeginAsync against the same Apple TV; let the existing
			// session run and be completed by CompletePairingAsync instead.
			if (session.Pairing is not null)
				{
				LogDiagnostic ($"Pairing is already active for '{device.Name}'; ignoring the repeated Pair Now request.");
				SetAppleTvNameStatus ("Pairing is active; enter the code shown on the Apple TV.");
				return;
				}

			session.Address = device.Address;
			session.Port = device.Port;
			session.UniqueId = device.UniqueId;
			session.Name = device.Name;
			LogDiagnostic ($"Starting pairing for '{device.Name}'. Enter the PIN shown on the Apple TV.");
			session.Pairing = await AppleTvCompanionPairing.BeginAsync (session.Address, session.Port, default).ConfigureAwait (false);
			SetAppleTvNameStatus ("Pairing is active; enter the code shown on the Apple TV.");
			LogDiagnostic ($"Pairing setup is active for '{device.Name}'.");
			}
		catch (Exception exception)
			{
			LogException (exception);
			SetAppleTvNameStatus ($"Pairing could not be started: {exception.Message}");
			}
		finally
			{
			session.Gate.Release ();
			}
		}

	private async Task CompletePairingAsync (AppleTvVideoServerProtocol protocol, string pairingPin)
		{
		// "0000" is the manifest's placeholder DefaultValue for PairingPin (an
		// OnScreenId field). Configure Pro will not enable Next while an
		// OnScreenId field is empty, but the real PIN cannot exist until PairNow
		// has been submitted and pairing has actually started on the Apple TV.
		// The placeholder lets Next be pressed at all, but it is never a real
		// PIN typed by the user and must never be attempted against the device.
		if (string.Equals (pairingPin, "0000", StringComparison.Ordinal))
			{
			LogDiagnostic ("Pairing PIN was ignored because it is still the placeholder value.");
			return;
			}

		if (pairingPin.Length != 4 || !int.TryParse (pairingPin, out int pin) || pin < 0 || pin > 9999 || string.IsNullOrWhiteSpace (protocol.AppleTvName))
			{
			LogDiagnostic ("Pairing PIN was ignored because the PIN is invalid or no Apple TV name is configured.");
			return;
			}

		// Crestron Home can apply PairNow and PairingPin back-to-back in the same
		// configuration batch, and may even recreate the driver instance between
		// them. Wait for any in-flight BeginPairingAsync (on this or a recreated
		// instance, since the gate lives in the static registry) to finish before
		// deciding whether a pairing session exists, instead of racing against it
		// and seeing a stale null Pairing.
		AppleTvPairingSessionState session = AppleTvPairingSessionState.Instance;
		await session.Gate.WaitAsync ().ConfigureAwait (false);
		try
			{
			if (session.Pairing is null)
				{
				LogDiagnostic ("Pairing PIN was ignored because pairing is not active.");
				return;
				}

			LogDiagnostic ($"Completing pairing for '{protocol.AppleTvName}'.");
			AppleTvStoredDevice device = await session.Pairing.CompleteAsync (pin, protocol.AppleTvName, session.Address, session.Port, default).ConfigureAwait (false);
			device.UniqueId = session.UniqueId;
			AppleTvStoredDevice.Save (device);
			SaveStoredDevice (device);
			LogDiagnostic ($"Credentials were saved for '{device.Name}'.");
			ClearPairing ();
			await ConnectCompanionAsync (protocol, device, device.Address, device.Port).ConfigureAwait (false);
			LogDiagnostic ($"Pairing completed and '{device.Name}' is connected.");
			}
		catch (Exception exception)
			{
			ClearPairing ();
			LogDiagnostic ($"Pairing failed for '{protocol.AppleTvName}'; showing Pair Now for retry.");
			LogException (exception);
			SetAppleTvNameStatus ($"Pairing failed: {exception.Message}");
			}
		finally
			{
			session.Gate.Release ();
			}
		}

	private async Task ConnectCompanionAsync (AppleTvVideoServerProtocol protocol, AppleTvStoredDevice device, string address, int port)
		{
		#if DEBUG
		LogDiagnostic ($"Starting Companion TCP connection to {address}:{port}.");
		#endif
		await protocol.ConnectCompanionAsync (
			address,
			port,
			device.ToCredentials (),
			device.StableIdentifier,
			device.Name).ConfigureAwait (false);
		ClearPairing ();
		LogDiagnostic ($"Companion session initialized for '{device.Name}'.");
		}

	private void ClearPairing ()
		{
		AppleTvPairingSessionState session = AppleTvPairingSessionState.Instance;
		if (session.Pairing is not null)
			{
			LogDiagnostic ("Clearing the active pairing session.");
			}

		session.Pairing?.Dispose ();
		session.Pairing = null;
		session.Address = string.Empty;
		session.Port = 0;
		session.UniqueId = string.Empty;
		session.Name = string.Empty;
		}

	private void SetAppleTvNameStatus (string description)
		{
		LogDiagnostic (description);
		}

	private AppleTvStoredDevice LoadStoredDevice ()
		{
		try
			{
			var storedDevice = GetSetting (STORED_DEVICE_SETTING_KEY) as AppleTvStoredDevice;
			if (storedDevice is not null && !string.IsNullOrWhiteSpace (storedDevice.UniqueId))
				{
				_storedDevice = storedDevice;
				return _storedDevice;
				}
			}
		catch (Exception exception)
			{
			LogException (exception);
			}

		return null;
		}

	private void SaveStoredDevice (AppleTvStoredDevice device)
		{
		SaveSetting (STORED_DEVICE_SETTING_KEY, device);
		_storedDevice = device;
		}

	private void LogException (Exception exception)
		{
		// Exceptions must be visible in both Debug and Release builds, so this
		// logs directly rather than going through the DEBUG-only LogDiagnostic.
		string diagnostic = $"[AppleTV] {exception.GetType ().FullName}: {exception.Message}";
		CrestronConsole.PrintLine (diagnostic);
		ErrorLog.Notice (diagnostic);
		}

	[Conditional ("DEBUG")]
	private void LogDiagnostic (string message)
		{
		// Write straight to the processor console/error log instead of going
		// through the RAD Log() hook, which is routed through Crestron Home
		// and can be filtered/delayed/interleaved with its own logging.
		// EnableLogging is not set until after the driver is constructed and
		// Initialize() runs, so gating on it here would silently drop every
		// diagnostic emitted during construction/load and the initial
		// SetUserAttribute calls that follow.
		string diagnostic = $"[AppleTV] {message}";
		CrestronConsole.PrintLine (diagnostic);
		ErrorLog.Notice (diagnostic);
		}
	}