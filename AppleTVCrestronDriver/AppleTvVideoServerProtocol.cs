// Copyright � 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AppleTvControlLibrary.Auth;
using AppleTvControlLibrary.Protocol;

using Crestron.RAD.Common.BasicDriver;
using Crestron.RAD.Common.Enums;
using Crestron.RAD.Common.Transports;
using Crestron.RAD.DeviceTypes.VideoServer;
using Crestron.SimplSharp;

namespace AppleTV.CrestronDriver;

internal sealed class AppleTvVideoServerProtocol : AVideoServerProtocol
	{
	// Confirmed against the Ultamation reference driver's IL
	// (CLinkClient.Navigate(HidCommand, HidAction), HidAction.Press/Release):
	// press-and-hold uses true HID key-down/key-up semantics, not a
	// driver-side repeat timer. The Companion/tvOS side is responsible for
	// repeating while the key is held down; PressArrowKey sends a single
	// "down" and ReleaseArrowKey sends a single "up".
	// Confirmed against the Ultamation reference driver's IL: it tracks pressed
	// state per-direction (a Dictionary<ArrowDirections, bool>), not a single
	// "last pressed" slot, and never force-releases a direction early. Each
	// direction's down/up state is independent, so an overlapping Press for a
	// new direction cannot clobber the release bookkeeping for a previous one.
	private AppleTvCompanionSession _session;
	private readonly Dictionary<ArrowDirections, bool> _pressedArrowDirections = new ()
		{
		{ ArrowDirections.Up, false },
		{ ArrowDirections.Down, false },
		{ ArrowDirections.Left, false },
		{ ArrowDirections.Right, false },
		};
	private readonly object _pressedArrowLock = new();
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly AppleTvKeyboardBridge _keyboardBridge;

	// Crestron Home re-applies the entire current configuration form (every
	// attribute's last known value, not just the one the user changed) whenever
	// it re-initializes the driver - which happens, for example, right after a
	// pairing attempt fails and the driver restarts, or as a side effect of
	// PairNow/PairingPin being applied themselves. Since PairNow is a boolean
	// toggle rather than a momentary/pulse control, that replay resends
	// PairNow = True even though the user did not press it again. Tracking the
	// last observed value lets PairNow be treated as edge-triggered
	// (false -> true) instead of level-triggered, so a replayed True is ignored.
	// This must live in the static AppleTvPairingSessionState singleton, not on
	// this instance: this protocol object is itself recreated on every reinit,
	// so an instance field would reset to false and see the replayed True as a
	// fresh edge every time.
	internal event Action<string> AppleTvNameChanged;

	internal event Action<string> PairingPinChanged;

	internal event Action PairNowRequested;

	internal event Action PairNowTurnedOff;

	// Raised when the currently active Companion Link session's connection
	// drops (e.g. a faulted frame transport or a closed TCP socket) so the
	// owning AppleTvVideoServer can attempt to reconnect. Companion Link has
	// no driver-side keepalive/reconnect of its own: without this, a dropped
	// session leaves the device offline in Crestron Home until the user
	// manually reloads the driver.
	internal event Action CompanionDisconnected;

	// Raised for every tokenized event line (see AppleTvBridgeServer's protocol
	// remarks) that should be relayed to any local bridge client (i.e. the
	// extension driver) connected through the loopback bridge server, so it
	// can mirror this driver's power/connection state instead of needing its
	// own Companion Link connection.
	internal event Action<string> BridgeEventRaised;

	internal AppleTvVideoServerProtocol (ISerialTransport transportDriver, byte id)
		 : base (transportDriver, id)
		{
		_keyboardBridge = new AppleTvKeyboardBridge (() => _session, LogDiagnostic);
		_keyboardBridge.BridgeEventRaised += line => BridgeEventRaised?.Invoke (line);
		}

	internal async Task ConnectCompanionAsync (string address, int port, HapCredentials credentials, string stableIdentifier, string appleTvName)
		{
		_session?.Dispose ();
		AppleTvCompanionSession session = await AppleTvCompanionSession.ConnectAsync (
			address,
			port,
			credentials,
			stableIdentifier,
			appleTvName,
			CancellationToken.None,
			LogDiagnostic).ConfigureAwait (false);
		_session = session;

		// Captures 'session' rather than reading _session at invocation time,
		// so an event raised by a since-superseded session (e.g. its own
		// Dispose() call from a later ConnectCompanionAsync replacing it) is
		// recognized as stale and ignored instead of being misreported as the
		// current connection dropping.
		session.ConnectionStateChanged += connected => HandleSessionConnectionStateChanged (session, connected);
		session.ConnectionClosed += exception => HandleSessionConnectionClosed (session, exception);
		session.PowerStateChanged += SetPowerState;
		if (session.Api is not null)
			{
			session.Api.SystemStatusChanged += (sender, e) => HandleSystemStatusChangedForBridge (session);
			session.Api.MediaControlCapabilitiesChanged += (sender, e) => HandleVolumeSupportChangedForBridge (session);
			session.Api.TextFocusStateChanged += (sender, e) => _ = _keyboardBridge.HandleTextFocusStateChangedAsync (session);
			}

		SetCompanionConnectionState (true);

		// ConnectAsync's initial best-effort power snapshot does not raise
		// SystemStatusChanged even when it successfully learns the state, so
		// PowerIsOn would otherwise be left at its default until the next
		// pushed transition. Seed it explicitly from what is already known.
		SetPowerState (session.IsPoweredOn);
		_ = RefreshStatusAsync ();
		_ = RefreshAppsAsync ();
		}

	// Re-emits the currently known power/system-status/volume-support state to any connected
	// bridge client on demand. Used to seed a bridge client (the extension driver) that connects
	// after this driver's own Companion Link session already established its initial state, since
	// the individual EVT: events above are only raised on a subsequent change, not retroactively
	// on connect.
	//
	// CompanionApi.CurrentSystemStatus can still be stale/Unknown at this point: the initial
	// best-effort FetchAttentionState snapshot taken during CompanionApi.ConnectAsync is wrapped
	// in a try/catch there (some tvOS versions reply "No request handler"), so a caller landing
	// here before the very first pushed SystemStatus/TVSystemStatus event would otherwise relay
	// a stale Unknown. Re-fetching directly via FetchAttentionStateAsync sidesteps that instead
	// of trusting the cached value.
	internal async Task RefreshStatusAsync ()
		{
		AppleTvCompanionSession session = _session;
		if (session?.Api is null)
			{
			return;
			}

		SetPowerState (session.IsPoweredOn);
		try
			{
			SystemStatus currentStatus = await session.Api.FetchAttentionStateAsync ().ConfigureAwait (false);
			if (ReferenceEquals (session, _session))
				{
				BridgeEventRaised?.Invoke (AppleTvBridgeProtocol.EVENT_SYSTEM_STATUS_PREFIX + currentStatus);
				}
			}
		catch (Exception exception)
			{
			LogDiagnostic ($"Failed to fetch the Apple TV's current system status for the bridge: {exception.Message}");
			HandleSystemStatusChangedForBridge (session);
			}

		HandleVolumeSupportChangedForBridge (session);
		}

	// Relays the Apple TV's finer-grained system status (Awake/Screensaver/Idle/Asleep/Unknown -
	// distinct from the on/off-only Power event) to any connected bridge client. Ignored if
	// 'session' has since been superseded by a newer ConnectCompanionAsync call.
	private void HandleSystemStatusChangedForBridge (AppleTvCompanionSession session)
		{
		if (!ReferenceEquals (session, _session) || session.Api is null)
			{
			return;
			}

		BridgeEventRaised?.Invoke (AppleTvBridgeProtocol.EVENT_SYSTEM_STATUS_PREFIX + session.Api.CurrentSystemStatus);
		}

	// Relays whether the currently playing app/Apple TV advertises volume control support to any
	// connected bridge client, so it can show/hide volume controls exactly as this driver's own
	// Crestron Home UI would. Ignored if 'session' has since been superseded.
	private void HandleVolumeSupportChangedForBridge (AppleTvCompanionSession session)
		{
		if (!ReferenceEquals (session, _session) || session.Api is null)
			{
			return;
			}

		BridgeEventRaised?.Invoke (AppleTvBridgeProtocol.EVENT_VOLUME_SUPPORTED_PREFIX + (session.Api.IsVolumeControlSupported ? "1" : "0"));
		}

	private void HandleSessionConnectionStateChanged (AppleTvCompanionSession session, bool connected)
		{
		if (!ReferenceEquals (session, _session))
			{
			return;
			}

		SetCompanionConnectionState (connected);
		}

	// CompanionApi.ConnectionClosed (library v1.1.4+) is the authoritative
	// signal that the session's connection is gone. AppleTvCompanionSession
	// unsubscribes from it in Dispose() before closing its socket, so by the
	// time this fires at all it is never our own intentional teardown (e.g.
	// ConnectCompanionAsync replacing this session) - it is always a genuine
	// external closure, whether a clean remote close (Exception == null) or
	// an unexpected fault (Exception != null - a transport/decrypt/dispatch
	// failure). Both cases warrant an automatic reconnect.
	private void HandleSessionConnectionClosed (AppleTvCompanionSession session, Exception exception)
		{
		if (!ReferenceEquals (session, _session))
			{
			return;
			}

		CompanionDisconnected?.Invoke ();
		}

	// Reflects the Apple TV's pushed power state (asleep vs. awake/screensaver/idle)
	// into Crestron Home so the UI does not just show "on" the moment Companion
	// Link connects and then never update again. PowerIsOn must be updated on
	// the protocol itself (not just carried on the FireEvent payload): RAD's own
	// warm-up/cool-down gating and command queueing logic (see IsSendable/
	// IsQueueable in the base classes) reads this property directly.
	private void SetPowerState (bool isOn)
		{
		LogDiagnostic ($"Apple TV power state changed to {(isOn ? "on" : "off")}.");
		PowerIsOn = isOn;
		FireEvent (VideoServerStateObjects.Power, new Power { PowerIsOn = isOn });
		BridgeEventRaised?.Invoke ($"EVT:POWER:{(isOn ? "On" : "Off")}");
		}

	internal string AppleTvName { get; private set; } = string.Empty;

	/// <summary>
	/// Receives boolean user attribute changes made in Crestron Home (e.g. the
	/// "Pair Now" trigger, which is modeled as a Custom/Boolean attribute).
	/// </summary>
	/// <param name="attributeId">The manifest parameter identifier.</param>
	/// <param name="attributeValue">The configured parameter value.</param>
	public override void SetUserAttribute (string attributeId, bool attributeValue)
		{
		LogDiagnostic ($"SetUserAttribute(bool): {attributeId} = {attributeValue}");

		if (string.Equals (attributeId, "PairNow", StringComparison.Ordinal))
			{
			// Edge-triggered: only fire on a false -> true transition so that
			// Crestron Home replaying the last-known form state (e.g. after the
			// driver reinitializes) does not silently restart pairing without
			// the user actually pressing Pair Now again. The last-observed value
			// is kept on the static AppleTvPairingSessionState singleton because
			// this protocol instance itself is recreated on every reinit.
			//
			// Crestron Home also replays this attribute's last-known value -
			// which can be a leftover True from a previous manual Pair Now - on
			// the very first Initialize after a process reload/reboot, when
			// HasObservedPairNow is still false. That first observed value must
			// only be recorded, never treated as a fresh edge, or every restart
			// would silently kick off an unwanted pairing handshake.
			AppleTvPairingSessionState session = AppleTvPairingSessionState.Instance;
			if (attributeValue && !session.LastPairNowValue && session.HasObservedPairNow)
				{
				PairNowRequested?.Invoke ();
				}
			else if (!attributeValue && session.LastPairNowValue)
				{
				PairNowTurnedOff?.Invoke ();
				}

			session.LastPairNowValue = attributeValue;
			session.HasObservedPairNow = true;
			return;
			}

		base.SetUserAttribute (attributeId, attributeValue);
		}

	/// <summary>
	/// Receives user attribute changes made in Crestron Home.
	/// </summary>
	/// <param name="attributeId">The manifest parameter identifier.</param>
	/// <param name="attributeValue">The configured parameter value.</param>
	public override void SetUserAttribute (string attributeId, string attributeValue)
		{
		LogDiagnostic ($"SetUserAttribute(string): {attributeId} = {attributeValue}");

		if (string.Equals (attributeId, "AppleTvName", StringComparison.Ordinal))
			{
			AppleTvName = attributeValue?.Trim () ?? string.Empty;
			AppleTvNameChanged?.Invoke (AppleTvName);
			return;
			}

		if (string.Equals (attributeId, "PairingPin", StringComparison.Ordinal))
			{
			// Crestron Home replays every configured attribute with its
			// last-known value on every driver reinit, including reinits
			// triggered by other attributes (e.g. AppleTvName). Only raise
			// PairingPinChanged on an actual change so an already-paired
			// device does not repeatedly re-enter the pairing completion
			// path with a stale PIN it already consumed.
			string newPairingPin = attributeValue?.Trim () ?? string.Empty;
			if (string.Equals (PairingPin, newPairingPin, StringComparison.Ordinal))
				{
				return;
				}

			PairingPin = newPairingPin;
			PairingPinChanged?.Invoke (PairingPin);
			return;
			}
		}

	internal string PairingPin { get; private set; } = string.Empty;

	internal void SetCompanionConnectionState (bool connected)
		{
		if (Transport is AppleTvNoOpTransport transport)
			{
			transport.SetConnectionState (connected);
			}

		ConnectionChangedEvent (connected);
		BridgeEventRaised?.Invoke (connected ? "EVT:CONNECTED" : "EVT:DISCONNECTED");
		}

	// Applies a tokenized command line (see AppleTvBridgeServer's protocol remarks) received
	// from a local bridge client (the extension driver) to the live Companion Link session this
	// driver instance owns, by routing it through the exact same public command methods
	// Crestron Home itself calls (SendHid/SendMedia/PowerOn/PowerOff/arrow key handling), so the
	// bridged extension driver and this driver's own Crestron Home UI behave identically.
	internal void DispatchBridgeCommand (string commandLine)
		{
		string[] parts = commandLine.Split (':');
		if (parts.Length < 2 || !string.Equals (parts[0], "CMD", StringComparison.OrdinalIgnoreCase))
			{
			return;
			}

		string kind = parts[1];
		string argument = parts.Length > 2 ? parts[2] : null;

		switch (kind.ToUpperInvariant ())
			{
			case "HID":
				if (Enum.TryParse (argument, true, out HidCommand hidCommand))
					{
					SendHid (hidCommand);
					}

				break;
			case "MEDIA":
				if (Enum.TryParse (argument, true, out MediaControlCommand mediaCommand))
					{
					SendMedia (mediaCommand);
					}

				break;
			case "ARROW":
				if (Enum.TryParse (argument, true, out ArrowDirections arrowDirection))
					{
					ArrowKey (arrowDirection);
					}

				break;
			case "ARROWDOWN":
				if (Enum.TryParse (argument, true, out ArrowDirections pressDirection))
					{
					PressArrowKey (pressDirection);
					}

				break;
			case "ARROWUP":
				ReleaseArrowKey ();
				break;
			case "POWER":
				if (string.Equals (argument, "On", StringComparison.OrdinalIgnoreCase))
					{
					PowerOn ();
					}
				else if (string.Equals (argument, "Off", StringComparison.OrdinalIgnoreCase))
					{
					PowerOff ();
					}

				break;
			case "LAUNCH":
				if (!string.IsNullOrEmpty (argument))
					{
					LaunchApp (argument);
					}

				break;
			case "REFRESHAPPS":
				_ = RefreshAppsAsync ();
				break;
			case "REFRESHSTATUS":
				_ = RefreshStatusAsync ();
				break;
			case "MUTE":
				if (string.Equals (argument, "Toggle", StringComparison.OrdinalIgnoreCase))
					{
					_ = ToggleMuteAsync ();
					}

				break;
			case "SETTEXT":
				if (argument is not null)
					{
					_ = _keyboardBridge.SetTextAsync (AppleTvBridgeProtocol.DecodeText (argument));
					}

				break;
			}
		}

	// Launches an app by bundle id
	// diagnostic log rather than throwing back into the bridge server's read loop.
	private void LaunchApp (string bundleId)
		{
		if (_session?.Api is null)
			{
			return;
			}

		_ = SendAndLogAsync (() => _session.Api.LaunchAppAsync (bundleId));
		}

	// Fetches the current app list from the live Companion Link session and relays it to any
	// connected bridge client as a single EVT:APPS: line. Also called once automatically after
	// every successful ConnectCompanionAsync so a freshly (re)connected extension driver client
	// does not have to wait for an explicit CMD:REFRESHAPPS round trip to populate its app list.
	internal async Task RefreshAppsAsync ()
		{
		if (_session?.Api is null)
			{
			return;
			}

		try
			{
			Dictionary<string, string> apps = await _session.Api.AppListAsync ().ConfigureAwait (false);
			var ordered = new List<(string BundleId, string Name)> ();
			foreach (KeyValuePair<string, string> app in apps)
				{
				ordered.Add ((app.Key, app.Value));
				}

			ordered.Sort ((left, right) => string.Compare (left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
			BridgeEventRaised?.Invoke (AppleTvBridgeProtocol.EVENT_APPS_PREFIX + AppleTvBridgeProtocol.EncodeApps (ordered));
			}
		catch (Exception exception)
			{
			LogDiagnostic ($"Failed to fetch Apple TV app list for the bridge: {exception.Message}");
			}
		}

	// Toggles mute on the live Companion Link session and relays the resulting state to any
	// connected bridge client as an EVT:MUTE: line.
	private async Task ToggleMuteAsync ()
		{
		if (_session?.Api is null)
			{
			return;
			}

		try
			{
			bool isMuted = await _session.Api.ToggleMuteAsync ().ConfigureAwait (false);
			BridgeEventRaised?.Invoke (AppleTvBridgeProtocol.EVENT_MUTE_PREFIX + (isMuted ? "1" : "0"));
			}
		catch (Exception exception)
			{
			LogDiagnostic ($"Failed to toggle mute for the bridge: {exception.Message}");
			}
		}

	public override void Dispose ()
		{
		// Make sure any held arrow keys are never left "stuck down" on the device
		// if the driver is disposed/reloaded while a key is pressed.
		ReleaseAllPressedArrowKeys ();
		_session?.Dispose ();
		_session = null;
		_sendGate.Dispose ();
		base.Dispose ();
		}

	protected override void ConnectionChangedEvent (bool connection)
		{
		base.ConnectionChangedEvent (connection);
		IsConnected = connection;
		}

	public override void PowerOff () => SendPowerCommand (isOn: false);

	public override void PowerOn () => SendPowerCommand (isOn: true);

	public override void Play () => SendMedia (MediaControlCommand.Play);

	public override void Pause () => SendMedia (MediaControlCommand.Pause);

	public override void PlayPause () => SendHid (HidCommand.PlayPause);

	public override void ForwardScan () => SendMedia (MediaControlCommand.FastForwardBegin);

	public override void ReverseScan () => SendMedia (MediaControlCommand.RewindBegin);

	public override void ForwardSkip () => SendMedia (MediaControlCommand.NextTrack);

	public override void ReverseSkip () => SendMedia (MediaControlCommand.PreviousTrack);

	// Confirmed via live-tail diagnostics: RAD calls PressArrowKey exactly once
	// when the arrow key goes down and ReleaseArrowKey exactly once when it comes
	// up. A quick tap arrives separately as a Pulse routed to ArrowKey(direction).
	public override void ArrowKey (ArrowDirections direction)
		{
		LogDiagnostic ($"ArrowKey({direction}) called");
		SendArrowHid (MapArrowDirection (direction));
		}

	public override void PressArrowKey (ArrowDirections direction)
		{
		LogDiagnostic ($"PressArrowKey({direction}) called");

		// Matches the Ultamation reference driver: mark this direction pressed
		// without disturbing any other direction's pressed state, then send a
		// single HID down for it.
		lock (_pressedArrowLock)
			{
			_pressedArrowDirections[direction] = true;
			}

		SendArrowDown (MapArrowDirection (direction));
		}

	public override void ReleaseArrowKey ()
		{
		LogDiagnostic ("ReleaseArrowKey() called");
		ReleaseAllPressedArrowKeys ();
		}

	// Ensures down=false is always sent for every arrow direction currently
	// tracked as pressed, no matter what else happens (exceptions sending the
	// down event, repeated release calls, disposal, etc.). Matches the
	// Ultamation reference driver's ReleaseArrowKey, which releases whatever
	// direction(s) are actually flagged pressed rather than a single "last
	// pressed" value. Safe to call even when nothing is pressed.
	private void ReleaseAllPressedArrowKeys ()
		{
		List<ArrowDirections> pressedDirections = null;
		lock (_pressedArrowLock)
			{
			foreach (ArrowDirections direction in new[] { ArrowDirections.Up, ArrowDirections.Down, ArrowDirections.Left, ArrowDirections.Right })
				{
				if (_pressedArrowDirections[direction])
					{
					_pressedArrowDirections[direction] = false;
					(pressedDirections ??= []).Add (direction);
					}
				}
			}

		if (pressedDirections is not null)
			{
			foreach (ArrowDirections direction in pressedDirections)
				{
				SendArrowUp (MapArrowDirection (direction));
				}
			}
		}

	private static HidCommand MapArrowDirection (ArrowDirections direction) => direction switch
		{
			ArrowDirections.Up => HidCommand.Up,
			ArrowDirections.Down => HidCommand.Down,
			ArrowDirections.Left => HidCommand.Left,
			ArrowDirections.Right => HidCommand.Right,
			_ => HidCommand.Up,
			};

	private void LogDiagnostic (string message)
		{
		// Routed through the base class's own Log() (gated on EnableLogging,
		// as every other RAD driver does), so diagnostics are visible in the
		// field via Crestron Home's logging toggle rather than only in Debug
		// builds. EnableLogging is not set until after the driver is
		// constructed and Initialize() runs, so diagnostics emitted during
		// construction/load and the initial SetUserAttribute calls are
		// unavoidably dropped; that is an acceptable startup-only gap.
		if (EnableLogging)
			{
			Log ($"[AppleTV] {message}");
			}

		#if DEBUG
		// Also write straight to the processor console/error log in Debug
		// builds, since it is not routed through Crestron Home and is not
		// subject to EnableLogging, filtering, or delay/interleaving.
		ErrorLog.Notice ($"[AppleTV] {message}");
		#endif
		}

	public override void Select () => SendHid (HidCommand.Select);

	public override void Menu () => SendHid (HidCommand.Menu);

	public override void Home () => SendHid (HidCommand.Home);

	public override void Back () => SendHid (HidCommand.Menu);

	private void SendArrowHid (HidCommand command)
		{
		if (_session is null)
			{
			return;
			}

		// No in-flight gating here: the underlying session/API already sequences
		// frames correctly, so each command is simply queued through _sendGate in
		// arrival order.
		_ = SendAndLogAsync (() => _session.SendHidAsync (command));
		}

	private void SendArrowDown (HidCommand command)
		{
		if (_session is null)
			{
			return;
			}

		_ = SendAndLogAsync (() => _session.SendHidDownAsync (command));
		}

	private void SendArrowUp (HidCommand command)
		{
		// Always attempt the release even if the session has gone away by the
		// time we get here; SendAndLogAsync/session-null are the only things
		// that could stop it from going out, and both are handled defensively.
		if (_session is null)
			{
			return;
			}

		_ = SendAndLogAsync (() => _session.SendHidUpAsync (command));
		}

	private void SendHid (HidCommand command)
		{
		if (_session is not null)
			{
			_ = SendAndLogAsync (() => _session.SendHidAsync (command));
			}
		}

	private void SendMedia (MediaControlCommand command)
		{
		if (_session is not null)
			{
			_ = SendAndLogAsync (() => _session.SendMediaCommandAsync (command));
			}
		}

	// Wake/Sleep must be sent as a single button-up event, not the down+up pair
	// SendHid uses for genuine remote buttons; sending Wake as a down+up pair
	// is silently ignored by the device, which previously made the PowerOn
	// button appear to do nothing while PowerOff (Sleep) worked.
	private void SendPowerCommand (bool isOn)
		{
		if (_session is not null)
			{
			_ = SendAndLogAsync (() => isOn ? _session.SendWakeAsync () : _session.SendSleepAsync ());
			}
		}

	private async Task SendAndLogAsync (Func<Task> operation)
		{
		try
			{
			// Serialize all outbound commands so overlapping calls (e.g. a held arrow
			// key firing faster than the round trip to the Apple TV) cannot interleave
			// their down/up HID pairs on the wire.
			await _sendGate.WaitAsync ().ConfigureAwait (false);
			try
				{
				await operation ().ConfigureAwait (false);
				}
			finally
				{
				_ = _sendGate.Release ();
				}
			}
		catch (Exception exception)
			{
			LogDiagnostic (exception.Message);
			}
		}
	}