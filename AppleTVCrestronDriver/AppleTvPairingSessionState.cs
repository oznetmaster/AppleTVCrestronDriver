using System.Threading;

namespace AppleTV.CrestronDriver;

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
	internal static readonly AppleTvPairingSessionState Instance = new AppleTvPairingSessionState ();

	private AppleTvPairingSessionState ()
		{
		}

	// Crestron Home can apply PairNow and PairingPin as part of the same
	// configuration batch, back-to-back with no delay between them, and can
	// also reinitialize the driver instance mid-handshake. This gate makes
	// CompletePairingAsync wait for any in-flight BeginPairingAsync (on this
	// or a recreated instance) to finish before it inspects Pairing.
	internal readonly SemaphoreSlim Gate = new SemaphoreSlim (1, 1);
	internal AppleTvCompanionPairing Pairing;
	internal string Address = string.Empty;
	internal int Port;
	internal string UniqueId = string.Empty;

	// Serializes ConfigureAppleTvAsync (which includes a discovery scan of up
	// to five seconds) across driver instances. This must be static rather
	// than an instance-owned SemaphoreSlim: Crestron Home can recreate the
	// driver instance (e.g. right after PairNow starts pairing) while an
	// older instance's ConfigureAppleTvAsync call is still awaiting its
	// discovery scan. With a per-instance gate that stale call is not
	// serialized against the new instance's pairing/connect flow at all, so
	// it can resume after pairing has already succeeded and overwrite the
	// just-saved paired credentials with a stale, unpaired discovery record.
	internal readonly SemaphoreSlim ConfigureGate = new SemaphoreSlim (1, 1);

	// The Apple TV name the active Pairing session was started for. Crestron Home
	// replays AppleTvName (with its unchanged value) on every config reinit,
	// including reinits triggered by PairNow/PairingPin themselves. Comparing
	// against this lets HandleAppleTvNameChangedAsync tell a genuine user rename
	// apart from that replay, so it does not tear down its own in-flight or
	// just-completing pairing session.
	internal string Name = string.Empty;

	// Last observed PairNow attribute value, used to edge-trigger pairing
	// (false -> true) instead of treating every True as a new request. This
	// must live here rather than on AppleTvVideoServerProtocol because that
	// protocol instance is itself recreated on every reinit; an instance
	// field would reset to false and see a replayed True as a fresh edge.
	internal bool LastPairNowValue;
	}
