// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

using Crestron.RAD.Common.Interfaces;
using Crestron.RAD.DeviceTypes.VideoServer;
using Crestron.SimplSharp;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Provides Crestron Home Video Server control for a paired Apple TV through Companion Link.
/// </summary>
public sealed class AppleTvVideoServer : ABasicVideoServer, ICloudConnected, ISerial, IAppleTvDriverHost
	{
	private AppleTvNoOpTransport _transport;
	private AppleTvBridgeServerHandlerRegistration _bridgeHandlerRegistration;

	// Depends only on IAppleTvDriverHost (implemented by this class below), so this logic is
	// constructible and testable off-box. See AppleTvVideoServerLogic for details. CurrentDriverOrSelf
	// is passed as the accessor so ModifyUserAttribute calls keep routing to whichever driver instance
	// Crestron Home currently holds a live reference to, exactly as before this was extracted.
	private readonly AppleTvVideoServerLogic _logic;

	/// <summary>
	/// Creates the driver instance.
	/// </summary>
	public AppleTvVideoServer () => _logic = new AppleTvVideoServerLogic (this, () => CurrentDriverOrSelf);

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
		LegacyCredentialMigrator.MigrateIfNeeded (BaseModel);
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
		protocol.PairNowRequested += () => _ = HandlePairNowRequestedAsync ();
		protocol.PairNowTurnedOff += () => HandlePairNowTurnedOff (protocol);
		protocol.CompanionDisconnected += () => _ = HandleCompanionDisconnectedAsync (protocol);
		protocol.StateChange += StateChange;
		protocol.RxOut += SendRxOut;

		// Record this as the instance Crestron Home currently holds a live
		// reference to BEFORE calling protocol.Initialize below. Initialize
		// replays every configured user attribute (AppleTvName, PairNow,
		// PairingPin) through SetUserAttribute, which raises
		// AppleTvNameChanged/PairNowRequested/PairingPinChanged synchronously.
		// Those handlers are fire-and-forget async methods that await
		// SemaphoreSlim.WaitAsync() - which completes synchronously whenever
		// the semaphore is uncontested - so the entire discard/discover/
		// status-setting chain (ConfigureAppleTvAsync and its SetXxxStatus
		// calls, which route through CurrentDriverOrSelf) can run inline,
		// synchronously, during this very call to protocol.Initialize.
		// Assigning CurrentProtocol/CurrentDriver only after Initialize
		// returned meant that inline chain used the previous, now-superseded
		// instance: ModifyUserAttribute ran and logged successfully on it,
		// but the resulting description update was invisible to Crestron
		// Home, which only reflects updates raised by the instance it
		// currently holds a live reference to. Async work (discovery scans,
		// pairing handshakes) started by an older, now-superseded instance
		// can still be running when this reinit happens (Crestron Home does
		// not cancel it); that older work must redirect its eventual
		// connected-state notification to whichever instance is current when
		// it completes, not to itself, or the device is left showing offline
		// despite a fully successful connect.
		AppleTvPairingSessionState.Instance.CurrentProtocol = protocol;
		AppleTvPairingSessionState.Instance.CurrentDriver = this;

		// Cancel whatever ConfigureAppleTvAsync pass the previous, now-
		// superseded instance may still have in flight. AppleTvName is
		// RequiredForConnection: Before, so Crestron Home reinitializes the
		// driver in direct response to the user editing it - but does not
		// itself stop the old instance's in-flight discovery/connect work,
		// which keeps running and eventually calls its own SetXxxStatus and
		// ConnectCompanionAsync redundantly alongside the new instance's own
		// replay-triggered pass for what is, from the user's perspective, a
		// single edit. Cancelling the old pass here (and disposing its
		// token source) means it observes the cancellation the next time it
		// checks (see ConfigureAppleTvAsync) and quietly stops instead of
		// racing the new instance to completion.
		AppleTvPairingSessionState.Instance.ConfigureCancellation?.Cancel ();
		AppleTvPairingSessionState.Instance.ConfigureCancellation?.Dispose ();
		AppleTvPairingSessionState.Instance.ConfigureCancellation = new CancellationTokenSource ();

		protocol.Initialize (VideoServerData);
		VideoServerProtocol = protocol;

		// If a PairingPin arrived on a now-stale instance (Crestron Home
		// reinitialized again before that instance could complete pairing),
		// it stashed the PIN here instead of completing on itself. This new,
		// current instance is the one Crestron Home actually watches, so it
		// must run the entire completion/connect flow itself rather than
		// letting the stale instance finish it invisibly.
		string pendingPin = AppleTvPairingSessionState.Instance.PendingPairingPin;
		if (!string.IsNullOrEmpty (pendingPin))
			{
			AppleTvPairingSessionState.Instance.PendingPairingPin = null;
			LogDiagnostic ("Resuming pairing completion on the current driver instance using a pending PIN.");
			_ = CompletePairingAsync (protocol, pendingPin);
			}

		// PairNow and PairingPin are declared statically in the
		// driver's json manifest, so they always exist and are never added or removed at
		// runtime. Any in-flight pairing session survives this reinitialization because
		// it lives in the static AppleTvPairingSessionState singleton rather than on
		// this instance.
		//
		// protocol.Initialize (VideoServerData) above already replays every configured
		// user attribute - including AppleTvName - through SetUserAttribute, which raises
		// AppleTvNameChanged and runs ConfigureAppleTvAsync via
		// HandleAppleTvNameChangedAsync. Calling ConfigureAppleTvAsync again here would
		// start a second, fully redundant discovery/connect pass concurrently with that
		// one on every single Initialize, rather than only when the name actually has no
		// configured value to replay.
		if (string.IsNullOrWhiteSpace (protocol.AppleTvName))
			{
			_ = ConfigureAppleTvAsync (protocol, protocol.AppleTvName);
			}
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
		//
		// Clear this instance's own bridge command handler registration, but
		// only if it is still the one currently installed: a bridge server
		// outlives this driver instance across Crestron Home reinitializations
		// (see AppleTvBridgeServerRegistry), so leaving a stale handler in
		// place would let commands relayed from a connected extension driver
		// keep being routed to this now-disposed protocol - whose Companion
		// Link session is gone - instead of failing loudly or waiting for the
		// next instance to install its own handler.
		_bridgeHandlerRegistration?.ClearIfCurrent ();

		base.Dispose ();
		}

	// These are invoked as fire-and-forget from synchronous RAD SDK event delegates
	// (Action/Action<string>), which offer no way to await a result back into
	// SetUserAttribute. Every path must therefore be wrapped in try/catch so that
	// nothing throws synchronously into the SDK's callback and no exception,
	// synchronous or asynchronous, is ever left unobserved.
	private async Task HandleAppleTvNameChangedAsync (IAppleTvProtocol protocol, string appleTvName)
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
			if (session.Pairing is not null && !string.Equals (session.Target.Name, appleTvName, StringComparison.OrdinalIgnoreCase))
				{
				ClearPairing ();
				}

			// ConfigureAppleTvAsync still needs the concrete protocol type: it starts
			// the bridge server (StartBridgeServer/BridgeCommandHandler), which is out
			// of scope for IAppleTvProtocol until that seam is extracted too.
			await ConfigureAppleTvAsync ((AppleTvVideoServerProtocol) protocol, appleTvName).ConfigureAwait (false);
			}
		catch (Exception exception)
			{
			LogException (exception);
			}
		}

	private async Task HandlePairingPinChangedAsync (IAppleTvProtocol protocol, string pairingPin)
		{
		try
			{
			// Crestron Home applies the configured PairingPin value (including
			// a leftover value from a previous pairing) on every Initialize,
			// not just when the user actually enters a new PIN - including the
			// very first Initialize after a reload/reboot, when there is no
			// prior in-memory value to compare against. There is nothing to do
			// unless a pairing handshake is actually in progress, so check
			// that first and short-circuit before logging or doing any work.
			if (AppleTvPairingSessionState.Instance.Pairing is null)
				{
				return;
				}

			LogDiagnostic ($"Pairing PIN received ({pairingPin.Length} digits).");

			// CompletePairingAsync still needs the concrete protocol type: it calls
			// ConnectCompanionAsync, which starts the bridge server.
			await CompletePairingAsync ((AppleTvVideoServerProtocol) protocol, pairingPin).ConfigureAwait (false);
			}
		catch (Exception exception)
			{
			LogException (exception);
			}
		}

	private async Task HandlePairNowRequestedAsync ()
		{
		try
			{
			LogDiagnostic ("Pair Now was requested.");
			await BeginPairingAsync ().ConfigureAwait (false);
			}
		catch (Exception exception)
			{
			LogException (exception);
			}
		}

	// Whether PairNow is currently known to be off. Crestron Home replays the
	// last-known value of every persistent attribute (including PairNow) on
	// every Initialize, so on the very first Initialize after a reload -
	// before HasObservedPairNow has been set by the initial SetUserAttribute
	// replay - the actual current value is not yet known here and must be
	// treated conservatively (i.e. not assumed off), since a stuck True would
	// otherwise be told the wrong turn-on-only instruction and never
	// re-trigger a fresh edge.
	private static bool IsPairNowKnownOff () => AppleTvVideoServerLogic.IsPairNowKnownOff ();

	private static string DescribePairNowRetry (string action) => AppleTvVideoServerLogic.DescribePairNowRetry (action);

	// PairNow is a persistent toggle, so turning it back off (whether the user did
	// so deliberately, or an in-flight pairing attempt was cancelled/abandoned by
	// turning it off before a PIN was entered) is itself a state change worth
	// reflecting in the description, rather than leaving whatever in-progress or
	// failure text was showing beforehand.
	private void HandlePairNowTurnedOff (IAppleTvProtocol protocol) => _logic.HandlePairNowTurnedOff (protocol);

	// Companion Link has no built-in keepalive/reconnect (confirmed against
	// AppleTVControlLibrary v1.1.4's CompanionApi.ConnectionClosed doc
	// remarks: "this library does not implement automatic reconnection;
	// consumers that want to reconnect must do so themselves"). Once the
	// session's TCP connection drops or its frame transport faults (e.g. a
	// transient Wi-Fi/network blip), AppleTvCompanionSession reports it once
	// and never retries on its own; without this handler the device would
	// stay offline in Crestron Home until the user manually reloads the
	// driver. This is only invoked for an unexpected fault (never for our
	// own intentional session teardown - see
	// AppleTvVideoServerProtocol.HandleSessionConnectionClosed), matching
	// the AppleTV.Remote.Wpf reference host's ConnectionClosed handling,
	// including its bounded, increasing retry schedule (2s/5s/10s/20s/30s)
	// that gives up rather than retrying forever once the device stays
	// unreachable.
	private async Task HandleCompanionDisconnectedAsync (IAppleTvProtocol protocol)
		=> await _logic.HandleCompanionDisconnectedAsync (
			protocol,
			p => ConfigureAppleTvAsync (p, p.AppleTvName)).ConfigureAwait (false);

	private async Task ConfigureAppleTvAsync (IAppleTvProtocol protocol, string appleTvName)
		=> await _logic.ConfigureAppleTvAsync (
			protocol,
			appleTvName,
			(p, device, address, port) => ConnectCompanionAsync ((AppleTvVideoServerProtocol) p, device, address, port)).ConfigureAwait (false);

	private async Task BeginPairingAsync () => await _logic.BeginPairingAsync ().ConfigureAwait (false);

	private async Task CompletePairingAsync (AppleTvVideoServerProtocol protocol, string pairingPin)
		=> await _logic.CompletePairingAsync (
			protocol,
			pairingPin,
			(p, device, address, port) => ConnectCompanionAsync ((AppleTvVideoServerProtocol) p, device, address, port)).ConfigureAwait (false);

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
		StartBridgeServer (protocol, device.UniqueId);
		}

	// Starts (or reattaches to an already-running) loopback bridge server for this Apple TV,
	// keyed by its stable UniqueId, so a local client (the Entity V2 extension driver) can send
	// tokenized commands and receive tokenized events through the single Companion Link
	// connection this driver instance owns, instead of connecting to the Apple TV directly.
	// Reattaching the handler/event subscription every time (rather than only on first start)
	// keeps the bridge pointed at whichever protocol/session instance is actually current after
	// a Crestron Home reinitialization, exactly like AppleTvPairingSessionState's own instance
	// hand-off.
	private void StartBridgeServer (AppleTvVideoServerProtocol protocol, string uniqueId)
		{
		if (string.IsNullOrWhiteSpace (uniqueId))
			{
			return;
			}

		AppleTvBridgeServer bridgeServer = AppleTvBridgeServerRegistry.GetOrStart (uniqueId, LogDiagnostic);
		var handler = new BridgeCommandHandler (protocol);
		protocol.BridgeEventRaised += bridgeServer.BroadcastEvent;

		// Tracked so Dispose() below can detect and clear this instance's own
		// handler registration if this driver instance is torn down (e.g. by a
		// Crestron Home reinitialization) before a subsequent instance finishes
		// reconnecting Companion Link and re-registers its own handler. Without
		// this, a bridge command arriving in that window would still be routed
		// to this now-disposed protocol, whose _session is null, so it would be
		// silently dropped instead of reaching the live session.
		_bridgeHandlerRegistration = AppleTvBridgeServerHandlerRegistration.Install (bridgeServer, handler);
		}

	// Adapts AppleTvVideoServerProtocol.DispatchBridgeCommand to IAppleTvBridgeCommandHandler so
	// the bridge server can apply relayed commands without depending on this driver's own type.
	private sealed class BridgeCommandHandler : IAppleTvBridgeCommandHandler
		{
		private readonly AppleTvVideoServerProtocol _protocol;

		internal BridgeCommandHandler (AppleTvVideoServerProtocol protocol) => _protocol = protocol;

		public void HandleBridgeCommand (string commandLine) => _protocol.DispatchBridgeCommand (commandLine);
		}

	private void ClearPairing () => _logic.ClearPairing ();

	private void SetAppleTvNameStatus (string description) => _logic.SetAppleTvNameStatus (description);

	private void SetPairedStatus (string name) => _logic.SetPairedStatus (name);

	private void SetDiscoveredUnpairedStatus (string name) => _logic.SetDiscoveredUnpairedStatus (name);

	private void SetNotFoundStatus (string name) => _logic.SetNotFoundStatus (name);

	private void SetBlankNameStatus () => _logic.SetBlankNameStatus ();

	private void SetPairNowStatus (string description) => _logic.SetPairNowStatus (description);

	private void SetPairingPinStatus (string description) => _logic.SetPairingPinStatus (description);

	// ModifyUserAttribute is an instance method: calling it on a driver
	// instance Crestron Home has already superseded (via a reinit that
	// happened while this instance's own async discovery/pairing work was
	// still running) executes and logs successfully, but the resulting
	// description update is invisible to Crestron Home, which only reflects
	// updates raised by whichever instance it currently holds a live
	// reference to. Falling back to 'this' keeps this safe to call even
	// before Initialize() has run (CurrentDriver not yet set).
	private AppleTvVideoServer CurrentDriverOrSelf => AppleTvPairingSessionState.Instance.CurrentDriver ?? this;

	private AppleTvStoredDevice LoadStoredDevice () => _logic.LoadStoredDevice ();

	private void SaveStoredDevice (AppleTvStoredDevice device) => _logic.SaveStoredDevice (device);

	private void LogException (Exception exception) =>
		LogDiagnostic ($"{exception.GetType ().FullName}: {exception.Message}");

	// IAppleTvDriverHost forwards. These exist so that AppleTvVideoServer's orchestration logic can
	// eventually be extracted into a testable class that depends on IAppleTvDriverHost instead of the
	// RAD base class directly (which cannot be constructed off-box).
	string IAppleTvDriverHost.BaseModel => BaseModel;

	object IAppleTvDriverHost.GetSetting (string key) => GetSetting (key);

	void IAppleTvDriverHost.SaveSetting (string key, object value) => SaveSetting (key, value);

	void IAppleTvDriverHost.ModifyUserAttribute (string attributeId, string description) =>
		ModifyUserAttribute (attributeId, description);

	void IAppleTvDriverHost.LogDiagnostic (string message) => LogDiagnostic (message);

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
	}
