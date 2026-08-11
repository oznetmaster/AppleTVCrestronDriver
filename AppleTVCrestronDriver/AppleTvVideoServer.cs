// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AppleTvControlLibrary.Discovery.Companion;

using Crestron.RAD.Common.Interfaces;
using Crestron.RAD.DeviceTypes.VideoServer;
using Crestron.SimplSharp;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Provides Crestron Home Video Server control for a paired Apple TV through Companion Link.
/// </summary>
public sealed class AppleTvVideoServer : ABasicVideoServer, ICloudConnected, ISerial
	{
	private const string STORED_DEVICE_SETTING_KEY = "AppleTvStoredDevice";
	private const string APPLE_TV_NAME_ATTRIBUTE_ID = "AppleTvName";
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
		protocol.PairNowRequested += () => _ = HandlePairNowRequestedAsync ();
		protocol.PairNowTurnedOff += () => HandlePairNowTurnedOff (protocol);
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
	public override void Dispose () =>
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
			if (session.Pairing is not null && !string.Equals (session.Target.Name, appleTvName, StringComparison.OrdinalIgnoreCase))
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
			await CompletePairingAsync (protocol, pairingPin).ConfigureAwait (false);
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
	private static bool IsPairNowKnownOff ()
		{
		AppleTvPairingSessionState session = AppleTvPairingSessionState.Instance;
		return session.HasObservedPairNow && !session.LastPairNowValue;
		}

	private static string DescribePairNowRetry (string action)
		{
		return IsPairNowKnownOff ()
			? $"Turn this on to {action}."
			: $"Turn this off and then on to {action}.";
		}

	// PairNow is a persistent toggle, so turning it back off (whether the user did
	// so deliberately, or an in-flight pairing attempt was cancelled/abandoned by
	// turning it off before a PIN was entered) is itself a state change worth
	// reflecting in the description, rather than leaving whatever in-progress or
	// failure text was showing beforehand.
	private void HandlePairNowTurnedOff (AppleTvVideoServerProtocol protocol)
		{
		try
			{
			LogDiagnostic ("Pair Now was turned off.");
			if (AppleTvPairingSessionState.Instance.Pairing is not null)
				{
				ClearPairing ();
				}

			AppleTvStoredDevice device = LoadStoredDevice ();
			bool isPaired = device is not null && device.IsPaired;
			SetPairNowStatus (isPaired
				? "The Apple TV is already paired. Turn this on to re-pair."
				: "Turn this on to pair.");

			if (isPaired)
				{
				SetPairingPinStatus ("Pairing is complete; no code is currently needed.");
				}
			else
				{
				// PairingPin is itself persistent: a PIN entered for the
				// abandoned attempt is still sitting in the configuration UI
				// rather than clearing on its own, so say "new" to make clear
				// a fresh code is expected even though the field still shows
				// the old one.
				bool hasPriorPin = !string.IsNullOrEmpty (protocol.PairingPin);
				SetPairingPinStatus (hasPriorPin
					? "Enter the new four-digit pairing code currently displayed on the Apple TV."
					: "Enter the four-digit pairing code currently displayed on the Apple TV.");
				}
			}
		catch (Exception exception)
			{
			LogException (exception);
			}
		}

	private async Task ConfigureAppleTvAsync (AppleTvVideoServerProtocol protocol, string appleTvName)
		{
		AppleTvPairingSessionState session = AppleTvPairingSessionState.Instance;

		// Captured once, at the start of this specific pass: identifies the
		// CancellationTokenSource that was current (i.e. belongs to this
		// pass's own driver instance) when this call began. Initialize()
		// cancels and replaces session.ConfigureCancellation every time it
		// runs, so a still-running older pass keeps observing its own,
		// now-cancelled token even after a newer instance has replaced
		// session.ConfigureCancellation with a fresh one for itself.
		CancellationToken cancellationToken = session.ConfigureCancellation.Token;

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
		//
		// This only briefly touches session.Gate rather than holding it for
		// this method's entire duration (including its own network I/O):
		// holding it throughout would serialize this method's discovery scan
		// and connect attempt against every other instance's pairing/connect
		// work for the whole process lifetime, and if any one of those calls
		// never released the gate (e.g. is genuinely stuck on slow/hung
		// network I/O), every future Initialize would then deadlock waiting
		// for it - permanently taking the device offline. The actual race
		// this exists to prevent (a stale saved-endpoint connect attempt
		// clobbering a concurrently-completing pairing handshake) is instead
		// avoided below by checking whether a pairing session is active
		// before attempting the saved-endpoint connect.
		await session.Gate.WaitAsync ().ConfigureAwait (false);
		_ = session.Gate.Release ();

		await session.ConfigureGate.WaitAsync ().ConfigureAwait (false);
		try
			{
			// A newer instance may have already started (and even finished)
			// its own pass by the time this pass gets its turn at
			// ConfigureGate - e.g. this pass was started by the old
			// instance's live SetUserAttribute callback, then Crestron Home
			// reinitialized before this pass reached the gate. Continuing
			// here would redundantly repeat discovery/connect and overwrite
			// whatever status the current instance's own pass already set.
			if (cancellationToken.IsCancellationRequested)
				{
				LogDiagnostic ("Skipping this pass because a newer driver instance has since taken over configuration.");
				return;
				}

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

				// Discarding device here only cleared the local variable, not the
				// persisted settings value LoadStoredDevice() reads from. Crestron Home
				// frequently reinitializes the driver again almost immediately after a
				// name change (e.g. because PairNow/PairingPin are replayed alongside
				// it), which starts a concurrent ConfigureAppleTvAsync/BeginPairingAsync
				// pass that calls LoadStoredDevice() itself. Without persisting the
				// discard here first, that reinit's LoadStoredDevice() still returns the
				// stale record, reconnects using it, and overwrites the correct
				// not-found/discovered status for the new name with the old device's
				// paired status.
				SaveStoredDevice (null);
				device = null;
				}

			if (device is null)
				{
				if (string.IsNullOrWhiteSpace (appleTvName))
					{
					LogDiagnostic ("No Apple TV name is configured and device settings do not contain a discovered or paired identity.");
					SetBlankNameStatus ();
					return;
					}

				device = AppleTvStoredDevice.LoadForName (appleTvName);
				if (device is not null)
					{
					SaveStoredDevice (device);
					if (!device.IsPaired)
						{
						SetDiscoveredUnpairedStatus (device.Name);
						}
					LogDiagnostic ($"Initialized device settings from the shared record for '{device.Name}'.");
					}
				else
					{
					// No stored discovery/paired record for this name yet; the discovery scan
					// below will run and set the appropriate status once it completes.
					}
				}

			if (device is not null && device.IsPaired)
				{
				try
					{
					LogDiagnostic ($"Attempting saved endpoint for '{device.Name}' at {device.Address}:{device.Port}.");
					await ConnectCompanionAsync (protocol, device, device.Address, device.Port).ConfigureAwait (false);
					LogDiagnostic ($"Connected to '{device.Name}' using its saved endpoint.");
					SetPairedStatus (device.Name);
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
				SetDiscoveredUnpairedStatus (device.Name);
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
				discovered = await MulticastCompanionDiscovery.DiscoveryAsync (appleTvName, TimeSpan.FromSeconds (5), cancellationToken).ConfigureAwait (false);
				}
			else
				{
				LogDiagnostic ("Discovering the known Apple TV by its saved identity for up to five seconds.");
				discovered = (await new MulticastCompanionDiscovery ().ScanAsync (TimeSpan.FromSeconds (5), cancellationToken).ConfigureAwait (false))
					.FirstOrDefault (result => string.Equals (result.UniqueId, device.UniqueId, StringComparison.Ordinal));
				}

			// A newer instance's own pass may have started and even completed
			// while this discovery scan (up to five seconds) was running.
			// Continuing to report a status or reconnect from here would
			// clobber whatever the current instance's pass already
			// determined and set.
			if (cancellationToken.IsCancellationRequested)
				{
				LogDiagnostic ("Discarding this discovery result because a newer driver instance has since taken over configuration.");
				return;
				}

			if (discovered is null || discovered.Address is null)
				{
				LogDiagnostic (device is null
					? $"Discovery did not find '{appleTvName}'."
					: "Discovery did not find the known Apple TV identity.");
				LogException (new InvalidOperationException ("The required Apple TV was not found by Companion Link discovery."));
				SetNotFoundStatus (appleTvName);
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

				// The in-memory 'device' already holds whatever credentials were most
				// recently loaded/saved, so validate them directly at the freshly
				// discovered address first - there is nothing on disk that isn't
				// already reflected here. Only if that fails do we reload from disk
				// below, in case a concurrent Pair Now/PairingPin completion (on this
				// or a recreated instance) saved newer credentials while the discovery
				// scan above was in flight.
				try
					{
					await ConnectCompanionAsync (protocol, device, discovered.Address.ToString (), discovered.Port).ConfigureAwait (false);
					LogDiagnostic ($"Connected to '{device.Name}' using its current stored credentials.");
					SetPairedStatus (device.Name);
					return;
					}
				catch (Exception exception)
					{
					LogDiagnostic ($"Current stored credentials for '{device.Name}' failed to connect at the discovered endpoint; checking for a newer pairing before continuing recovery.");
					LogException (exception);
					}

				AppleTvStoredDevice currentDevice = AppleTvStoredDevice.LoadForName (device.Name);
				if (currentDevice is not null && currentDevice.IsPaired)
					{
					try
						{
						await ConnectCompanionAsync (protocol, currentDevice, currentDevice.Address, currentDevice.Port).ConfigureAwait (false);
						SaveStoredDevice (currentDevice);
						LogDiagnostic ($"Connected to '{currentDevice.Name}' using its current stored credentials.");
						SetPairedStatus (currentDevice.Name);
						return;
						}
					catch (Exception exception)
						{
						LogDiagnostic ($"Reloaded stored credentials for '{currentDevice.Name}' also failed to connect; continuing endpoint recovery.");
						LogException (exception);
						}

					device = currentDevice;
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
				SetPairedStatus (device.Name);
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
			SetDiscoveredUnpairedStatus (discovered.Name);
			LogDiagnostic ($"Discovered but unpaired identity persisted for '{discovered.Name}'; awaiting Pair Now.");
			}
		catch (Exception exception)
			{
			LogException (exception);
			SetAppleTvNameStatus ($"The Apple TV name could not be validated: {exception.Message}");
			SetPairNowStatus ("The Apple TV name could not be validated; pairing cannot start.");
			SetPairingPinStatus ("The Apple TV name could not be validated; a pairing code cannot be entered.");
			}
		finally
			{
			_ = session.ConfigureGate.Release ();
			}
		}

	/// <summary>
	/// Starts Companion Link pairing using the persisted discovery identity (address/port),
	/// requiring no additional network discovery, and shows the PIN entry attributes.
	/// </summary>
	private async Task BeginPairingAsync ()
		{
		AppleTvPairingSessionState session = AppleTvPairingSessionState.Instance;
		await session.Gate.WaitAsync ().ConfigureAwait (false);
		try
			{
			AppleTvStoredDevice device = LoadStoredDevice ();

			// Pair Now is an explicit user request to (re-)pair, so a stored
			// record already marked IsPaired must not block it: pair
			// verification against those credentials can fail permanently
			// (e.g. the user removed this driver from the Apple TV's paired
			// accessories), in which case IsPaired stays true forever and the
			// only way to recover is to let the user re-pair. Only bail out
			// when there is no address at all to pair against.
			if (device is null || string.IsNullOrWhiteSpace (device.Address))
				{
				LogDiagnostic ("Pair Now was requested, but no discovered Apple TV identity is available.");
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

			session.Target = new PairingTarget (device.Address, device.Port, device.UniqueId, device.Name);
			LogDiagnostic ($"Starting pairing for '{device.Name}'. Enter the PIN shown on the Apple TV.");
			session.Pairing = await AppleTvCompanionPairing.BeginAsync (session.Target.Address, session.Target.Port, default).ConfigureAwait (false);
			SetAppleTvNameStatus ("Pairing is active; enter the code shown on the Apple TV.");
			SetPairNowStatus ("Pairing is now in progress. A pairing code is displayed on the Apple TV.");

			// PairingPin is a persistent attribute: if a PIN was already entered
			// for a previous pairing attempt, its value (and this description)
			// otherwise stays exactly as it was left, which reads as though
			// nothing changed. Telling the user to enter the "new" code makes it
			// clear a fresh PIN is expected even though the field is not blank.
			bool hasPriorPin = !string.IsNullOrEmpty (AppleTvPairingSessionState.Instance.CurrentProtocol?.PairingPin);
			SetPairingPinStatus (hasPriorPin
				? $"Enter the new four-digit pairing code currently displayed on '{device.Name}'."
				: $"Enter the four-digit pairing code currently displayed on '{device.Name}'.");
			LogDiagnostic ($"Pairing setup is active for '{device.Name}'.");
			}
		catch (Exception exception)
			{
			LogException (exception);
			SetAppleTvNameStatus (session.Target is not null
				? $"'{session.Target.Name}' was found on the network but pairing could not be started: {exception.Message}"
				: $"Pairing could not be started: {exception.Message}");
			SetPairNowStatus ($"Pairing could not be started: {exception.Message} {DescribePairNowRetry ("try again")}");
			}
		finally
			{
			_ = session.Gate.Release ();
			}
		}

	private async Task CompletePairingAsync (AppleTvVideoServerProtocol protocol, string pairingPin)
		{
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
			// HandlePairingPinChangedAsync already filters out the common case
			// of a replayed PIN when no pairing is in progress, so reaching
			// here with no active session means one ended (completed, failed,
			// or was cleared) while this call was waiting on the gate above.
			if (session.Pairing is null)
				{
				LogDiagnostic ("Pairing PIN was ignored because pairing is no longer active.");
				return;
				}

			// 'protocol' is whichever instance was live when Crestron Home
			// delivered SetUserAttribute for PairingPin, but Crestron Home can
			// reinitialize the driver again before this method got to run
			// (e.g. while it was awaiting the Gate above). Running the
			// handshake on a stale instance is pointless: even on success, the
			// stale instance's connected-state notification is invisible to
			// Crestron Home, which has already switched to the new instance.
			// Never start the handshake on a stale instance - stash the PIN
			// instead and let the new instance's own Initialize() run the
			// entire completion/connect flow on itself.
			if (!ReferenceEquals (AppleTvPairingSessionState.Instance.CurrentProtocol, protocol))
				{
				LogDiagnostic ($"Deferring pairing completion for '{protocol.AppleTvName}' because a newer driver instance is now current; it will complete pairing using this PIN.");
				session.PendingPairingPin = pairingPin;
				return;
				}

			LogDiagnostic ($"Completing pairing for '{protocol.AppleTvName}'.");
			AppleTvStoredDevice device = await session.Pairing.CompleteAsync (pin, protocol.AppleTvName, session.Target.Address, session.Target.Port, default).ConfigureAwait (false);
			device.UniqueId = session.Target.UniqueId;
			AppleTvStoredDevice.Save (device);
			SaveStoredDevice (device);
			LogDiagnostic ($"Credentials were saved for '{device.Name}'.");
			ClearPairing ();
			SetAppleTvNameStatus ($"'{device.Name}' is paired.");
			SetPairNowStatus ($"'{device.Name}' is already paired. {DescribePairNowRetry ("re-pair")}");
			SetPairingPinStatus ("Pairing is complete; no code is currently needed.");

			// Crestron Home can reinitialize the driver again while the
			// handshake above was in flight (it is the only actual await in
			// this method), creating a newer instance. If that happened,
			// 'protocol' here is now a superseded instance: connecting
			// through it would succeed but be invisible to Crestron Home,
			// which is already displaying the newer instance. Credentials are
			// already saved at this point (ClearPairing has already run, so
			// there is no pairing session left to replay), so connect using
			// the new instance's own protocol reference instead.
			AppleTvVideoServerProtocol currentProtocol = AppleTvPairingSessionState.Instance.CurrentProtocol;
			if (!ReferenceEquals (currentProtocol, protocol))
				{
				LogDiagnostic ($"Pairing completed for '{device.Name}', but a newer driver instance became current while completing; connecting using that instance instead.");
				await ConnectCompanionAsync (currentProtocol, device, device.Address, device.Port).ConfigureAwait (false);
				LogDiagnostic ($"Pairing completed and '{device.Name}' is connected on the current driver instance.");
				return;
				}

			await ConnectCompanionAsync (protocol, device, device.Address, device.Port).ConfigureAwait (false);
			LogDiagnostic ($"Pairing completed and '{device.Name}' is connected.");
			}
		catch (Exception exception)
			{
			ClearPairing ();
			LogDiagnostic ($"Pairing failed for '{protocol.AppleTvName}'; showing Pair Now for retry.");
			LogException (exception);
			SetAppleTvNameStatus ($"'{protocol.AppleTvName}' was found on the network but pairing failed: {exception.Message}");
			SetPairNowStatus ($"Pairing failed: {exception.Message} {DescribePairNowRetry ("try again")}");
			SetPairingPinStatus ("Enter the four-digit pairing code currently displayed on the Apple TV.");
			}
		finally
			{
			_ = session.Gate.Release ();
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

		session.Clear ();
		}

	// ModifyUserAttribute (documented at
	// https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V1/Create-a-Driver/Create-the-Driver-Files/Dynamic-User-Attributes.htm)
	// updates a user attribute's description in place and raises
	// UserAttributesChanged so Crestron Home refreshes the label shown next to
	// the AppleTvName field in the configuration UI, giving the user
	// human-readable feedback on where pairing currently stands (discovered,
	// pairing in progress, failed, etc.) without a separate status attribute.
	// This only ever changes the AppleTvName attribute's description, never
	// its value, so it does not raise AppleTvNameChanged/SetUserAttribute and
	// cannot trigger a redundant driver reinitialization or ConfigureAppleTvAsync pass.
	private void SetAppleTvNameStatus (string description)
		{
		LogDiagnostic (description);
		CurrentDriverOrSelf.ModifyUserAttribute (APPLE_TV_NAME_ATTRIBUTE_ID, description);
		}

	// The three attributes describe a single, shared pairing state, so whenever one
	// changes because of what ConfigureAppleTvAsync just found (paired/discovered/not
	// found/blank), all three must be updated together - otherwise a stale PairNow or
	// PairingPin description (e.g. still saying "already paired") is left behind after
	// the Apple TV name is changed to something that no longer resolves, or is changed
	// to a name that is on the network but not yet paired, or on a reload where the
	// configured name was already invalid/unpaired before this driver instance existed.
	private void SetPairedStatus (string name)
		{
		SetAppleTvNameStatus ($"'{name}' was found on the network and is paired.");
		SetPairNowStatus ($"'{name}' is already paired. {DescribePairNowRetry ("re-pair")}");
		SetPairingPinStatus ("Pairing is complete; no code is currently needed.");
		}

	private void SetDiscoveredUnpairedStatus (string name)
		{
		SetAppleTvNameStatus ($"'{name}' was found on the network. Press Pair Now to complete pairing.");
		SetPairNowStatus ($"'{name}' was found on the network but is not yet paired. {DescribePairNowRetry ("start pairing")}");
		SetPairingPinStatus ("Enter the four-digit pairing code currently displayed on the Apple TV once pairing has started.");
		}

	private void SetNotFoundStatus (string name)
		{
		SetAppleTvNameStatus ($"No Apple TV named '{name}' was found. Check the name and ensure the Apple TV is online on the local network.");
		SetPairNowStatus ("The Apple TV must be found on the network before pairing can start.");
		SetPairingPinStatus ("A pairing code cannot be entered until the Apple TV is found on the network.");
		}

	private void SetBlankNameStatus ()
		{
		SetAppleTvNameStatus ("Enter the name of the Apple TV to pair with.");
		SetPairNowStatus ("Enter the Apple TV name above before pairing.");
		SetPairingPinStatus ("Enter the Apple TV name above before entering a pairing code.");
		}

	// Reflects current pairing state into the PairNow and PairingPin
	// attributes' descriptions, the same way SetAppleTvNameStatus does for
	// AppleTvName, so the whole pairing form (not just the name field) shows
	// what is actually happening instead of always displaying its static,
	// generic manifest text.
	private void SetPairNowStatus (string description)
		{
		LogDiagnostic (description);
		CurrentDriverOrSelf.ModifyUserAttribute (PAIR_NOW_ATTRIBUTE_ID, description);
		}

	private void SetPairingPinStatus (string description)
		{
		LogDiagnostic (description);
		CurrentDriverOrSelf.ModifyUserAttribute (PAIRING_PIN_ATTRIBUTE_ID, description);
		}

	// ModifyUserAttribute is an instance method: calling it on a driver
	// instance Crestron Home has already superseded (via a reinit that
	// happened while this instance's own async discovery/pairing work was
	// still running) executes and logs successfully, but the resulting
	// description update is invisible to Crestron Home, which only reflects
	// updates raised by whichever instance it currently holds a live
	// reference to. Falling back to 'this' keeps this safe to call even
	// before Initialize() has run (CurrentDriver not yet set).
	private AppleTvVideoServer CurrentDriverOrSelf => AppleTvPairingSessionState.Instance.CurrentDriver ?? this;

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

	private void LogException (Exception exception) =>
		LogDiagnostic ($"{exception.GetType ().FullName}: {exception.Message}");

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