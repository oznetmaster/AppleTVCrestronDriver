// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using AppleTV.CrestronDriver;

using NUnit.Framework;
using NUnit.Framework.Legacy;

using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace AppleTVCrestronDriver.Tests;

[TestFixture]
public sealed class AppleTvPairingSessionStateTests
	{
	[Test]
	public void Instance_IsSingleton ()
		{
		AppleTvPairingSessionState first = AppleTvPairingSessionState.Instance;
		AppleTvPairingSessionState second = AppleTvPairingSessionState.Instance;

		Assert.AreSame (first, second);
		}

	[Test]
	public void PairingTarget_Empty_HasEmptyValues ()
		{
		PairingTarget target = PairingTarget.Empty;

		Assert.AreEqual (string.Empty, target.Address);
		Assert.AreEqual (0, target.Port);
		Assert.AreEqual (string.Empty, target.UniqueId);
		Assert.AreEqual (string.Empty, target.Name);
		}

	[Test]
	public void PairingTarget_EqualValues_AreEqual ()
		{
		PairingTarget first = new ("10.0.0.1", 1234, "unique", "Living Room");
		PairingTarget second = new ("10.0.0.1", 1234, "unique", "Living Room");

		Assert.AreEqual (first, second);
		}

	[Test]
	public void PairingTarget_DifferentValues_AreNotEqual ()
		{
		PairingTarget first = new ("10.0.0.1", 1234, "unique", "Living Room");
		PairingTarget second = new ("10.0.0.2", 1234, "unique", "Living Room");

		Assert.AreNotEqual (first, second);
		}
	}