using Crestron.RAD.Common.Interfaces;
using Crestron.RAD.Common.Transports;
using Crestron.RAD.DeviceTypes.VideoServer;

namespace AppleTV.ManifestTest;

/// <summary>
/// Isolated Video Server used solely to validate manifest conversion on the processor.
/// </summary>
public sealed class ManifestTestVideoServer : ABasicVideoServer, ICloudConnected, ISerial
{
	public void Initialize()
	{
		ConnectionTransport = new NoOpTransport();
	}

	private sealed class NoOpTransport : ATransportDriver
	{
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
}
