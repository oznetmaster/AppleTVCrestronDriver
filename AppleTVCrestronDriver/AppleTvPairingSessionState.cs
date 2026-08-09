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
	}
