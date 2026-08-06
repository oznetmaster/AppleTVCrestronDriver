using Crestron.RAD.Common.Transports;

namespace AppleTV.CrestronDriver;

internal sealed class AppleTvNoOpTransport : ATransportDriver
{
	internal void SetConnectionState(bool connected)
	{
		IsConnected = connected;
	}

	public override void SendMethod(string method, params object[] parameters)
	{
	}

	public override void Start()
	{
	}

	public override void Stop()
	{
	}
}
