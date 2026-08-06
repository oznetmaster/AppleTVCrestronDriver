using Crestron.RAD.Common.BasicDriver;
using Crestron.RAD.Common.Interfaces;
using Crestron.RAD.Common.Transports;
using Crestron.RAD.DeviceTypes.Gateway;
using Crestron.RAD.DeviceTypes.VideoServer;
using Crestron.SimplSharp;

namespace AppleTV.CrestronPlatformProof
{
	public sealed class AppleTvPlatformProof : AGateway, ITcp
	{
		public void Initialize(IPAddress ipAddress, int port)
		{
			var transport = new TcpTransport(
				true,
				InternalEnableLogging,
				InternalCustomLogger,
				InternalEnableRxDebug,
				InternalEnableTxDebug);
			transport.Initialize(ipAddress, port);
			ConnectionTransport = transport;

			Protocol = new AppleTvPlatformProofProtocol(ConnectionTransport, Id)
			{
				EnableLogging = InternalEnableLogging,
				CustomLogger = InternalCustomLogger
			};
		}

		public override void Connect()
		{
			base.Connect();
			((AppleTvPlatformProofProtocol)Protocol).RegisterProofDevice();
		}

		public override void Disconnect()
		{
			((AppleTvPlatformProofProtocol)Protocol).RemoveProofDevice();
			base.Disconnect();
		}
	}

	internal sealed class AppleTvPlatformProofProtocol : AGatewayProtocol
	{
		private AppleTvProofVideoServer _videoServer;

		public AppleTvPlatformProofProtocol(ISerialTransport transport, byte id)
			: base(transport, id)
		{
		}

		public void RegisterProofDevice()
		{
			if (_videoServer != null)
			{
				return;
			}

			_videoServer = new AppleTvProofVideoServer("AppleTVProof", "Apple TV Proof");
			AddPairedDevice(_videoServer.PairedDeviceInformation, _videoServer);
		}

		public void RemoveProofDevice()
		{
			if (_videoServer == null)
			{
				return;
			}

			RemovePairedDevice(_videoServer.PairedDeviceInformation.Id);
			_videoServer.Dispose();
			_videoServer = null;
		}

		public override void Dispose()
		{
			RemoveProofDevice();
			base.Dispose();
		}
	}

	internal sealed class AppleTvProofVideoServer : ABasicVideoServer, ITcp
	{
		private readonly GatewayPairedDeviceInformation _pairedDeviceInformation;

		public AppleTvProofVideoServer(string id, string name)
		{
			_pairedDeviceInformation = new GatewayPairedDeviceInformation(
				id,
				name,
				Description,
				Manufacturer,
				BaseModel,
				DriverData.CrestronSerialDeviceApi.GeneralInformation.DeviceType,
				string.Empty);
		}

		public GatewayPairedDeviceInformation PairedDeviceInformation => _pairedDeviceInformation;

		public void Initialize(IPAddress ipAddress, int port)
		{
			var transport = new TcpTransport(
				true,
				InternalEnableLogging,
				InternalCustomLogger,
				InternalEnableRxDebug,
				InternalEnableTxDebug);
			transport.Initialize(ipAddress, port);
			ConnectionTransport = transport;

			var protocol = new AppleTvProofVideoServerProtocol(ConnectionTransport, Id)
			{
				EnableLogging = InternalEnableLogging,
				CustomLogger = InternalCustomLogger
			};
			protocol.StateChange += StateChange;
			protocol.RxOut += SendRxOut;
			protocol.Initialize(VideoServerData);
			VideoServerProtocol = protocol;
		}
	}

	internal sealed class AppleTvProofVideoServerProtocol : AVideoServerProtocol
	{
		public AppleTvProofVideoServerProtocol(ISerialTransport transport, byte id)
			: base(transport, id)
		{
		}

		protected override void ConnectionChangedEvent(bool connection)
		{
			base.ConnectionChangedEvent(connection);
			IsConnected = connection;
		}
	}
}