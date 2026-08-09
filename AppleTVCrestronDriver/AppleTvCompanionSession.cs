// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using AppleTvControlLibrary.Auth;
using AppleTvControlLibrary.Connection;
using AppleTvControlLibrary.Protocol;
using AppleTvControlLibrary.Tlv8;

namespace AppleTV.CrestronDriver;

internal sealed class AppleTvCompanionSession : IDisposable
	{
	private readonly TcpClient _client;
	private readonly CompanionConnection _connection;
	private readonly CompanionProtocol _protocol;
	private readonly Thread _readThread;
	private readonly Action<string> _log;
	private volatile bool _disposed;
	private bool _isConnected;

	private AppleTvCompanionSession (TcpClient client, CompanionConnection connection, CompanionProtocol protocol, Action<string> log)
		{
		_client = client;
		_connection = connection;
		_protocol = protocol;
		_log = log;
		_readThread = new Thread (ReadLoop)
			{
			IsBackground = true,
			Name = "AppleTVCompanionLink"
			};
		}

	internal CompanionApi Api
		{
		get; private set;
		}

	internal event Action<bool> ConnectionStateChanged;

	internal static async Task<AppleTvCompanionSession> ConnectAsync (
		 string host,
		 int port,
		 HapCredentials credentials,
		 string stableIdentifier,
		 string appleTvName,
		 CancellationToken cancellationToken,
		 Action<string> log)
		{
		var client = new TcpClient ();
		#if DEBUG
		log?.Invoke ("Connecting TCP socket.");
		#endif
		using (cancellationToken.Register (state => ((TcpClient)state).Close (), client))
			{
			await client.ConnectAsync (host, port).ConfigureAwait (false);
			}
		#if DEBUG
		log?.Invoke ("TCP socket connected; starting pair verification.");
		#endif

		var connection = new CompanionConnection ();
		var protocol = new CompanionProtocol (connection, new SrpAuthHandler ());
		var session = new AppleTvCompanionSession (client, connection, protocol, log);
		protocol.AsyncSender = session.SendAsync;
		session._readThread.Start ();

		try
			{
			await session.PairVerifyAsync (credentials, cancellationToken).ConfigureAwait (false);
			#if DEBUG
			log?.Invoke ("Pair verification succeeded; starting Companion API session.");
			#endif
			session.Api = new CompanionApi (
				 protocol,
				 credentials,
				 stableIdentifier,
				 Convert.ToString (credentials.AtvId),
				 "AppleTV",
				 appleTvName);
			await session.Api.ConnectAsync ().ConfigureAwait (false);
			#if DEBUG
			log?.Invoke ("Companion API session connected.");
			#endif
			session.SetConnectionState (true);
			return session;
			}
		catch (Exception exception)
			{
			#if DEBUG
			log?.Invoke ($"Companion connection failed: {exception.GetType ().FullName}: {exception.Message}");
			#else
			_ = exception;
			#endif
			session.Dispose ();
			throw;
			}
		}

	internal async Task SendHidAsync (HidCommand command)
		{
		await Api.SendHidCommandAsync (true, command).ConfigureAwait (false);
		await Api.SendHidCommandAsync (false, command).ConfigureAwait (false);
		}

	// True key-down/key-up semantics, matching the Ultamation reference driver's
	// CLinkClient.Navigate(HidCommand, HidAction) model: the Companion transport
	// itself handles repeat while the key is held, so we must send a single down
	// on press and a single up on release rather than driving our own repeat.
	internal Task SendHidDownAsync (HidCommand command) => Api.SendHidCommandAsync (true, command);

	internal Task SendHidUpAsync (HidCommand command) => Api.SendHidCommandAsync (false, command);

	internal Task SendMediaCommandAsync (MediaControlCommand command) => Api.MediaControlCommandAsync (command);

	private async Task PairVerifyAsync (HapCredentials credentials, CancellationToken cancellationToken)
		{
		var srp = new SrpAuthHandler ();
		(byte[] _, byte[] publicBytes) = srp.Initialize ();
		byte[] pv1 = Tlv8.WriteTlv (new Dictionary<int, byte[]>
			{
				 { (int)TlvValue.SeqNo, new byte[] { 1 } },
				  { (int)TlvValue.PublicKey, publicBytes }
			});
		Dictionary<object, object> pv2Response = await _protocol.ExchangeAuthAsync (
			 FrameType.PV_Start,
			 new Dictionary<string, object> { { "_pd", pv1 }, { "_auTy", 4 } },
			 cancellationToken).ConfigureAwait (false);
		Dictionary<int, byte[]> pv2 = Tlv8.ReadTlv ((byte[])pv2Response["_pd"]);
		byte[] pv3 = Tlv8.WriteTlv (new Dictionary<int, byte[]>
			{
				 { (int)TlvValue.SeqNo, new byte[] { 3 } },
				 { (int)TlvValue.EncryptedData, srp.Verify1(credentials, pv2[(int)TlvValue.PublicKey], pv2[(int)TlvValue.EncryptedData]) }
			});
		Dictionary<object, object> pv4Response = await _protocol.ExchangeAuthAsync (
			 FrameType.PV_Next,
			 new Dictionary<string, object> { { "_pd", pv3 } },
			 cancellationToken).ConfigureAwait (false);
		if (Tlv8.ReadTlv ((byte[])pv4Response["_pd"]).ContainsKey ((int)TlvValue.Error))
			{
			throw new AuthenticationException ("Apple TV pair verification failed.");
			}

		(byte[] outputKey, byte[] inputKey) = srp.Verify2 (CompanionProtocol.SRP_SALT, CompanionProtocol.SRP_OUTPUT_INFO, CompanionProtocol.SRP_INPUT_INFO);
		_connection.EnableEncryption (outputKey, inputKey);
		}

	private Task SendAsync (byte[] frame) => _client.GetStream ().WriteAsync (frame, 0, frame.Length);

	private void ReadLoop ()
		{
		var buffer = new byte[4096];
		try
			{
			while (!_disposed)
				{
				int read = _client.GetStream ().Read (buffer, 0, buffer.Length);
				if (read == 0)
					{
					SetConnectionState (false);
					return;
					}

				var received = new byte[read];
				Array.Copy (buffer, received, read);
				_connection.ReceiveData (received);
				}
			}
		catch (Exception exception)
			{
			if (!_disposed)
				{
				#if DEBUG
				_log?.Invoke ($"Companion read loop failed: {exception.GetType ().FullName}: {exception.Message}");
				#else
				_ = exception;
				#endif
				_connection.Fault (null);
				SetConnectionState (false);
				}
			}
		}

	public void Dispose ()
		{
		if (_disposed)
			{
			return;
			}

		_disposed = true;
		SetConnectionState (false);
		_client.Close ();
		}

	private void SetConnectionState (bool connected)
		{
		if (_isConnected == connected)
			{
			return;
			}

		_isConnected = connected;
		#if DEBUG
		_log?.Invoke ($"Companion connection state changed to {(connected ? "connected" : "disconnected")}.");
		#endif
		ConnectionStateChanged?.Invoke (connected);
		}
	}