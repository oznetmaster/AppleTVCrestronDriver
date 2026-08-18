// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Threading;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Identifies the Apple TV a pairing session is (or was) started against.
/// Address, Port, UniqueId, and Name are always set and cleared together as
/// a single logical identity, so grouping them here (rather than as four
/// independent fields on <see cref="AppleTvPairingSessionState"/>) makes
/// that invariant a compile-time fact instead of a convention every call
/// site has to reproduce by hand.
/// </summary>
internal sealed record PairingTarget (string Address, int Port, string UniqueId, string Name)
	{
	internal static readonly PairingTarget Empty = new(string.Empty, 0, string.Empty, string.Empty);
	}

/// <summary>
/// Holds in-flight Companion Link pairing state outside of any single
/// <see cref="AppleTvVideoServer"/> instance. Crestron Home reinitializes
/// (disposes and recreates) the driver instance whenever a configuration
/// attribute is applied, including PairNow and PairingPin themselves. If
/// the pairing session, gate, and handshake bookkeeping lived only on the
/// instance, a mid-pairing reinit would dispose the active session and/or
/// let a recreated instance start a second, competing BeginAsync against
/// the same Apple TV. This driver only ever manages a single Apple TV, so
/// a single static instance (which survives across Initialize/Dispose
/// cycles within the same process) is all that is needed for the pairing
/// session to survive instance recreation, letting the PIN entered against
/// the recreated instance still complete the original handshake.
/// </summary>
internal sealed class AppleTvPairingSessionState
	{
	internal static readonly AppleTvPairingSessionState Instance = new();

	private AppleTvPairingSessionState ()
		{
		}

	// Crestron Home can apply PairNow and PairingPin as part of the same
	// configuration batch, back-to-back with no delay between them, and can
	// also reinitialize the driver instance mid-handshake. This gate makes
	// CompletePairingAsync wait for any in-flight BeginPairingAsync (on this
	// or a recreated instance) to finish before it inspects Pairing.
	internal readonly SemaphoreSlim Gate = new(1, 1);
	internal AppleTvCompanionPairing Pairing { get; set; }

	// The Apple TV identity (address/port/unique id/name) the active Pairing
	// session was started for. Crestron Home replays AppleTvName (with its
	// unchanged value) on every config reinit, including reinits triggered by
	// PairNow/PairingPin themselves. Comparing Target.Name lets
	// HandleAppleTvNameChangedAsync tell a genuine user rename apart from
	// that replay, so it does not tear down its own in-flight or
	// just-completing pairing session.
	internal PairingTarget Target { get; set; } = PairingTarget.Empty;

	// Serializes ConfigureAppleTvAsync (which includes a discovery scan of up
	// to five seconds) across driver instances. This must be static rather
	// than an instance-owned SemaphoreSlim: Crestron Home can recreate the
	// driver instance (e.g. right after PairNow starts pairing) while an
	// older instance's ConfigureAppleTvAsync call is still awaiting its
	// discovery scan. With a per-instance gate that stale call is not
	// serialized against the new instance's pairing/connect flow at all, so
	// it can resume after pairing has already succeeded and overwrite the
	// just-saved paired credentials with a stale, unpaired discovery record.
	internal readonly SemaphoreSlim ConfigureGate = new(1, 1);

	// Last observed PairNow attribute value, used to edge-trigger pairing
	// (false -> true) instead of treating every True as a new request. This
	// must live here rather than on AppleTvVideoServerProtocol because that
	// protocol instance is itself recreated on every reinit; an instance
	// field would reset to false and see a replayed True as a fresh edge.
	internal bool LastPairNowValue { get; set; }

	// Whether LastPairNowValue reflects a value actually observed from
	// Crestron Home yet in this process's lifetime. Crestron Home persists
	// and replays the PairNow attribute's last-known value - including True,
	// left over from a previous manual Pair Now - on the very first
	// Initialize after a reload/reboot, when there is no prior in-memory
	// value to compare against. Without this flag, LastPairNowValue's default
	// of false would make that replayed True look like a fresh false->true
	// edge and silently kick off a real, unwanted pairing handshake on every
	// startup. The first observed value, whatever it is, must only be
	// recorded, never treated as a user request.
	internal bool HasObservedPairNow { get; set; }

	// The protocol instance Crestron Home currently holds a live reference to
	// (i.e. the one created by the most recent Initialize()). Crestron Home
	// can reinitialize the driver again while an older instance's
	// BeginPairingAsync/CompletePairingAsync/ConnectCompanionAsync chain is
	// still awaiting network I/O (TCP connect, pair verify, Companion API
	// session setup). That older instance's async chain does not stop just
	// because a newer instance was created - it keeps running and, if it
	// succeeds, sets IsConnected/fires ConnectionChangedEvent on itself. But
	// Crestron Home already switched to displaying the newer instance, so
	// that success is invisible to the host and the device is shown offline
	// despite every diagnostic log showing a fully successful connection.
	// Tracking the current instance here lets the async pairing/connect
	// paths detect this and redirect the final connected-state notification
	// to whichever protocol instance is actually current when the async work
	// completes, instead of the possibly-stale instance captured when the
	// chain started.
	internal IAppleTvProtocol CurrentProtocol { get; set; }

	// The AppleTvVideoServer driver instance Crestron Home currently holds a
	// live reference to (mirrors CurrentProtocol, but for the driver object
	// itself). ModifyUserAttribute is an instance method inherited from
	// ABasicDriver: calling it on a superseded driver instance still runs
	// and logs successfully, but Crestron Home only shows attribute
	// description updates raised by the instance it currently has a live
	// reference to. Async work (discovery scans, pairing handshakes)
	// started by an older instance must therefore route its status updates
	// through whichever driver instance is actually current, or those
	// updates are silently invisible even though the log shows them firing.
	internal AppleTvVideoServer CurrentDriver { get; set; }

	// Cancels the previous driver instance's in-flight ConfigureAppleTvAsync
	// pass (discovery scan and/or connect attempt) whenever Initialize() runs
	// again. Crestron Home reinitializes the driver in response to a
	// RequiredForConnection: Before attribute (AppleTvName) changing, but does
	// not itself stop the old instance's async work; without this, both the
	// old instance's pass (started by the live SetUserAttribute callback) and
	// the new instance's replay-triggered pass run concurrently to
	// completion, each independently discovering/connecting/reporting status
	// for what is - from the user's perspective - a single edit. This is
	// static, like the other session state, because the old instance itself
	// is about to be discarded and has no opportunity to cancel its own
	// pending work once Crestron Home has moved on to the new instance.
	internal CancellationTokenSource ConfigureCancellation { get; set; }

	// A PairingPin that arrived while the protocol instance handling it was
	// (or turned out to be) stale - i.e. no longer ReferenceEquals to
	// CurrentProtocol - is never completed on that stale instance. Instead
	// it is stashed here, and the next Initialize() (which runs on the new,
	// current instance) picks it up and runs the entire completion/connect
	// flow itself. This guarantees the completed-pairing connect always
	// happens on the instance Crestron Home is actually watching, instead
	// of relying on the stale instance to hand off a partially completed
	// operation.
	internal string PendingPairingPin { get; set; }

	/// <summary>
	/// Ends the active pairing session, if any, and resets the pairing
	/// target back to empty. This lives here, rather than being reproduced
	/// field-by-field at each call site, so resetting the session's related
	/// fields together stays a single, compiler-checked operation.
	/// </summary>
	internal void Clear ()
		{
		Pairing?.Dispose ();
		Pairing = null;
		Target = PairingTarget.Empty;
		}
	}
