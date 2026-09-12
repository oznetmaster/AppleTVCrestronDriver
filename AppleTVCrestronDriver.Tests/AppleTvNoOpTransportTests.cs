// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using AppleTV.CrestronDriver;

using NUnit.Framework;
using NUnit.Framework.Legacy;

using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace AppleTVCrestronDriver.Tests;

[TestFixture]
public sealed class AppleTvNoOpTransportTests
	{
	[Test]
	public void SetConnectionState_True_SetsIsConnectedAndInvokesConnectionChanged ()
		{
		AppleTvNoOpTransport transport = new ();
		bool? raisedValue = null;
		transport.ConnectionChanged += connected => raisedValue = connected;

		transport.SetConnectionState (true);

		Assert.IsTrue (transport.IsConnected);
		Assert.AreEqual (true, raisedValue);
		}

	[Test]
	public void SetConnectionState_False_SetsIsConnectedAndInvokesConnectionChanged ()
		{
		AppleTvNoOpTransport transport = new ();
		transport.SetConnectionState (true);
		bool? raisedValue = null;
		transport.ConnectionChanged += connected => raisedValue = connected;

		transport.SetConnectionState (false);

		Assert.IsFalse (transport.IsConnected);
		Assert.AreEqual (false, raisedValue);
		}

	[Test]
	public void SetConnectionState_NoSubscriber_DoesNotThrow ()
		{
		AppleTvNoOpTransport transport = new ();

		transport.SetConnectionState (true);

		Assert.IsTrue (transport.IsConnected);
		}
	}