// Copyright (c) 2026 Neil Colvin. MIT with Commons Clause; see LICENSE.

using System;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;
using AppleTvControlLibrary.Auth;
using AppleTvControlLibrary.Connection;
using AppleTvControlLibrary.FakeDevice;
using AppleTvControlLibrary.Tlv8;

using NUnit.Framework;

namespace AppleTVCrestronDriver.Tests;

[TestFixture]
public sealed class AppleTvPairingValidationTests
	{
	[TestCase (2, false)]
	[TestCase (2, true)]
	[TestCase (4, false)]
	[TestCase (4, true)]
	[TestCase (6, false)]
	[TestCase (6, true)]
	public async Task PairSetupRejectsMissingOrIncorrectResponseState (int state, bool missing)
		{
		using var host = new PairingResponseTcpHost ((_, data) => ChangeState (data, state, missing));
		await Assert.ThrowsAsync<AuthenticationException> (async () =>
			{
			using AppleTvCompanionPairing pairing = await AppleTvCompanionPairing.BeginAsync ("127.0.0.1", host.Port, CancellationToken.None);
			await pairing.CompleteAsync (FakeCompanionDevice.PIN_CODE, "Synthetic", "127.0.0.1", host.Port, CancellationToken.None);
			});
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task PairSetupRejectsMissingOrIncorrectServerProof (bool missing)
		{
		using var host = new PairingResponseTcpHost ((_, data) =>
			{
			var fields = Tlv8.ReadTlv (data);
			if (fields[(int)TlvValue.SeqNo][0] == 4)
				{
				if (missing) fields.Remove ((int)TlvValue.Proof);
				else fields[(int)TlvValue.Proof][0] ^= 1;
				}
			return Tlv8.WriteTlv (fields);
			});
		await Assert.ThrowsAsync<AuthenticationException> (async () =>
			{
			using AppleTvCompanionPairing pairing = await AppleTvCompanionPairing.BeginAsync ("127.0.0.1", host.Port, CancellationToken.None);
			await pairing.CompleteAsync (FakeCompanionDevice.PIN_CODE, "Synthetic", "127.0.0.1", host.Port, CancellationToken.None);
			});
		}

	[TestCase (2, false)]
	[TestCase (2, true)]
	[TestCase (4, false)]
	[TestCase (4, true)]
	public async Task PairVerifyRejectsMissingOrIncorrectAcknowledgement (int state, bool missing)
		{
		using var host = new PairingResponseTcpHost ((type, data) =>
			type is (int)FrameType.PV_Start or (int)FrameType.PV_Next ? ChangeState (data, state, missing) : data);
		AppleTvStoredDevice saved;
		using (AppleTvCompanionPairing pairing = await AppleTvCompanionPairing.BeginAsync ("127.0.0.1", host.Port, CancellationToken.None))
			saved = await pairing.CompleteAsync (FakeCompanionDevice.PIN_CODE, "Synthetic", "127.0.0.1", host.Port, CancellationToken.None);

		await Assert.ThrowsAsync<AuthenticationException> (async () =>
			{
			using AppleTvCompanionSession session = await AppleTvCompanionSession.ConnectAsync ("127.0.0.1", host.Port,
				saved.ToCredentials (), saved.StableIdentifier, "Synthetic", CancellationToken.None, null);
			});
		}

	[Test]
	public async Task RemoteCloseDuringPairSetupCompletesWithoutWaitingForAuthTimeout ()
		{
		using var host = new PairingResponseTcpHost ((_, data) => null);
		Task pairing = AppleTvCompanionPairing.BeginAsync ("127.0.0.1", host.Port, CancellationToken.None);
		Task completed = await Task.WhenAny (pairing, Task.Delay (TimeSpan.FromSeconds (3)));
		Assert.That (completed, Is.SameAs (pairing));
		Assert.That (pairing.IsFaulted, Is.True);
		_ = pairing.Exception;
		}

	private static byte[] ChangeState (byte[] data, int state, bool missing)
		{
		var fields = Tlv8.ReadTlv (data);
		if (fields[(int)TlvValue.SeqNo][0] == state)
			{
			if (missing) fields.Remove ((int)TlvValue.SeqNo);
			else fields[(int)TlvValue.SeqNo] = new[] { (byte)(state + 1) };
			}
		return Tlv8.WriteTlv (fields);
		}
	}
