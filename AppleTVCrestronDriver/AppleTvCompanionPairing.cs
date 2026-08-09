// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using AppleTvControlLibrary.Auth;
using AppleTvControlLibrary.Connection;
using AppleTvControlLibrary.Protocol;
using AppleTvControlLibrary.Tlv8;

namespace AppleTV.CrestronDriver;

internal sealed class AppleTvCompanionPairing : IDisposable
	{
	private readonly TcpClient _client;
	private readonly CompanionProtocol _protocol;
	private readonly SrpAuthHandler _srp;
	private byte[] _atvSalt;
	private byte[] _atvPublicKey;
	private readonly Thread _readThread;
	private readonly CompanionConnection _connection;
	private volatile bool _disposed;

	private AppleTvCompanionPairing (TcpClient client, CompanionConnection connection, CompanionProtocol protocol, SrpAuthHandler srp, byte[] atvSalt, byte[] atvPublicKey)
		{
		_client = client;
		_connection = connection;
		_protocol = protocol;
		_srp = srp;
		_atvSalt = atvSalt;
		_atvPublicKey = atvPublicKey;
		_readThread = new Thread (ReadLoop)
			{
			IsBackground = true,
			Name = "AppleTVCompanionPairing"
			};
		}

	internal static async Task<AppleTvCompanionPairing> BeginAsync (string host, int port, CancellationToken cancellationToken)
		{
		var client = new TcpClient ();
		using (cancellationToken.Register (state => ((TcpClient)state).Close (), client))
			{
			await client.ConnectAsync (host, port).ConfigureAwait (false);
			}

		var connection = new CompanionConnection ();
		var protocol = new CompanionProtocol (connection, new SrpAuthHandler ());
		var srp = new SrpAuthHandler ();
		var pairing = new AppleTvCompanionPairing (client, connection, protocol, srp, null, null);
		protocol.AsyncSender = pairing.SendAsync;
		pairing._readThread.Start ();

		try
			{
			_ = srp.Initialize ();
			byte[] m1 = Tlv8.WriteTlv (new Dictionary<int, byte[]>
				{
					{ (int)TlvValue.Method, new byte[] { 0 } },
					{ (int)TlvValue.SeqNo, new byte[] { 1 } },
				});
			Dictionary<object, object> response = await protocol.ExchangeAuthAsync (
				FrameType.PS_Start,
				new Dictionary<string, object> { { "_pd", m1 }, { "_pwTy", 1 } },
				cancellationToken).ConfigureAwait (false);
			Dictionary<int, byte[]> m2 = Tlv8.ReadTlv ((byte[])response["_pd"]);
			if (m2.TryGetValue ((int)TlvValue.Error, out byte[] errorValue))
				{
				int errorCode = errorValue is { Length: > 0 } ? errorValue[0] : -1;
				throw new InvalidOperationException ($"The Apple TV rejected the pairing request (error code {errorCode}). The Apple TV may already be in an active pairing session; try again in a few seconds.");
				}

			return !m2.TryGetValue ((int)TlvValue.Salt, out pairing._atvSalt) || !m2.TryGetValue ((int)TlvValue.PublicKey, out pairing._atvPublicKey)
				? throw new InvalidOperationException ("The Apple TV's pairing response did not include the expected salt/public key values.")
				: pairing;
			}
		catch
			{
			pairing.Dispose ();
			throw;
			}
		}

	internal async Task<AppleTvStoredDevice> CompleteAsync (int pin, string name, string address, int port, CancellationToken cancellationToken)
		{
		_srp.Step1 (pin);
		(byte[] clientPublicKey, byte[] clientProof) = _srp.Step2 (_atvPublicKey, _atvSalt);
		byte[] m3 = Tlv8.WriteTlv (new Dictionary<int, byte[]>
			{
				{ (int)TlvValue.SeqNo, new byte[] { 3 } },
				{ (int)TlvValue.PublicKey, clientPublicKey },
				{ (int)TlvValue.Proof, clientProof },
			});
		Dictionary<object, object> m4Response = await _protocol.ExchangeAuthAsync (
			FrameType.PS_Next,
			new Dictionary<string, object> { { "_pd", m3 }, { "_pwTy", 1 } },
			cancellationToken).ConfigureAwait (false);
		if (Tlv8.ReadTlv ((byte[])m4Response["_pd"]).ContainsKey ((int)TlvValue.Error))
			{
			throw new AuthenticationException ("Apple TV pairing failed. Verify the displayed PIN.");
			}

		byte[] m5 = Tlv8.WriteTlv (new Dictionary<int, byte[]>
			{
				{ (int)TlvValue.SeqNo, new byte[] { 5 } },
				{ (int)TlvValue.EncryptedData, _srp.Step3 (Environment.MachineName) },
				});
		Dictionary<object, object> m6Response = await _protocol.ExchangeAuthAsync (
			FrameType.PS_Next,
			new Dictionary<string, object> { { "_pd", m5 }, { "_pwTy", 1 } },
			cancellationToken).ConfigureAwait (false);
		HapCredentials credentials = _srp.Step4 (Tlv8.ReadTlv ((byte[])m6Response["_pd"])[(int)TlvValue.EncryptedData]);

		return new AppleTvStoredDevice
			{
			Name = name,
			Address = address,
			Port = port,
			StableIdentifier = GenerateStableIdentifier (),
			Ltpk = credentials.Ltpk,
			Ltsk = credentials.Ltsk,
			AtvId = credentials.AtvId,
			ClientId = credentials.ClientId,
			};
		}

	public void Dispose ()
		{
		if (_disposed)
			{
			return;
			}

		_disposed = true;
		_client.Close ();
		}

	private static string GenerateStableIdentifier ()
		{
		var bytes = new byte[6];
		using (var random = new RNGCryptoServiceProvider ())
			{
			random.GetBytes (bytes);
			}

		return BitConverter.ToString (bytes).Replace ("-", string.Empty).ToLowerInvariant ();
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
					return;
					}

				var received = new byte[read];
				Array.Copy (buffer, received, read);
				_connection.ReceiveData (received);
				}
			}
		catch
			{
			if (!_disposed)
				{
				_connection.Fault (null);
				}
			}
		}
	}
