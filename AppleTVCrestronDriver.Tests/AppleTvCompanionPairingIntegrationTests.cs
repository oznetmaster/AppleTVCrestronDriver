// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using AppleTvControlLibrary.FakeDevice;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

/// <summary>
/// Exercises <see cref="AppleTvCompanionPairing"/>'s BeginAsync/CompleteAsync pairing
/// handshake against AppleTVControlLibrary's reusable <see cref="FakeCompanionTcpHost"/>,
/// a real socket-backed fake Apple TV, instead of requiring physical hardware.
/// </summary>
[TestClass]
public sealed class AppleTvCompanionPairingIntegrationTests
	{
	[TestMethod]
	public async Task BeginAsync_ThenCompleteAsync_WithCorrectPin_ReturnsPairedStoredDevice ()
		{
		using FakeCompanionTcpHost host = new (pin: FakeCompanionDevice.PIN_CODE);
		host.AcceptOne ();

		using AppleTvCompanionPairing pairing = await AppleTvCompanionPairing.BeginAsync ("127.0.0.1", host.Port, CancellationToken.None);
		AppleTvStoredDevice device = await pairing.CompleteAsync (FakeCompanionDevice.PIN_CODE, "Living Room", "127.0.0.1", host.Port, CancellationToken.None);

		Assert.IsNotNull (device);
		Assert.AreEqual ("Living Room", device.Name);
		Assert.AreEqual ("127.0.0.1", device.Address);
		Assert.AreEqual (host.Port, device.Port);
		Assert.IsFalse (string.IsNullOrEmpty (device.StableIdentifier));
		Assert.IsTrue (device.IsPaired);
		Assert.IsTrue (host.AuthDevice.HasPaired);
		}

	[TestMethod]
	public async Task CompleteAsync_WithWrongPin_ThrowsAuthenticationException ()
		{
		using FakeCompanionTcpHost host = new (pin: FakeCompanionDevice.PIN_CODE);
		host.AcceptOne ();

		using AppleTvCompanionPairing pairing = await AppleTvCompanionPairing.BeginAsync ("127.0.0.1", host.Port, CancellationToken.None);

		// AppleTvCompanionPairing.CompleteAsync throws AppleTvControlLibrary.Auth.AuthenticationException,
		// but that type name is ambiguous here (CS0433): the driver assembly IL-merges its own copy of
		// AppleTvControlLibrary alongside this test project's direct package reference to the original.
		// Asserting on the exception type name avoids binding to either assembly's copy explicitly.
		bool threw = false;
		try
			{
			_ = await pairing.CompleteAsync (FakeCompanionDevice.PIN_CODE + 1, "Living Room", "127.0.0.1", host.Port, CancellationToken.None);
			}
		catch (System.Exception exception)
			{
			threw = true;
			Assert.AreEqual ("AuthenticationException", exception.GetType ().Name);
			}

		Assert.IsTrue (threw, "Expected CompleteAsync to throw for an incorrect PIN.");
		}
	}
