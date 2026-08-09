using Crestron.RAD.Common.Transports;

namespace AppleTV.CrestronDriver;

internal sealed class AppleTvNoOpTransport : ATransportDriver
	{
	internal void SetConnectionState (bool connected)
		{
		IsConnected = connected;

		// IsConnected alone is just a property; the RAD framework's own
		// online/offline notification path listens for the ConnectionChanged
		// delegate, not the property setter. Without invoking it here, the
		// host (Crestron Home) never learns the transport connected and the
		// device keeps showing offline even after pairing/connect succeeds.
		ConnectionChanged?.Invoke (connected);
		}

	public override void SendMethod (string method, params object[] parameters)
		{
		}

	public override void Start ()
		{
		}

	public override void Stop ()
		{
		}
	}
