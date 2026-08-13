// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using AppleTvControlLibrary.FakeDevice;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

/// <summary>
/// Exercises <see cref="AppleTvCompanionSession"/>'s ConnectionClosed event (backed by
/// AppleTVControlLibrary v1.1.4+'s <c>CompanionApi.ConnectionClosed</c>) against
/// <see cref="FakeCompanionTcpHost"/>, a real socket-backed fake Apple TV, so the reconnect
/// signal both <c>AppleTvVideoServerProtocol</c> and the extension driver rely on is validated
/// end-to-end instead of only against the video-server/extension-driver code that consumes it.
/// </summary>
/// <remarks>
/// Loosely mirrors AppleTVControlLibrary's own
/// <c>CompanionApiIntegrationTests.ConnectionClosedFiresWithExceptionWhenConnectionIsFaulted</c>/
/// <c>ConnectionClosedFiresOnProtocolDispose</c>, but drives the real TCP/session layer this
/// project owns (AppleTvCompanionSession) rather than CompanionApi/CompanionProtocol directly.
/// </remarks>
[TestClass]
public sealed class AppleTvCompanionSessionConnectionClosedTests
	{
	[TestMethod]
	public async Task ConnectionClosed_WhenHostClosesSocket_FiresWithNullException ()
		{
		using FakeCompanionTcpHost host = new (pin: FakeCompanionDevice.PIN_CODE);
		host.AcceptOne ();

		AppleTvCompanionSession session = await ConnectSessionAsync (host).ConfigureAwait (false);
		try
			{
			var raised = new TaskCompletionSource<Exception> (TaskCreationOptions.RunContinuationsAsynchronously);
			session.ConnectionClosed += exception => raised.TrySetResult (exception);

			// Simulates the remote end closing the connection (e.g. the Apple TV
			// dropping Companion Link) rather than this session's own Dispose().
			host.Dispose ();

			Task completed = await Task.WhenAny (raised.Task, Task.Delay (TimeSpan.FromSeconds (5))).ConfigureAwait (false);
			Assert.AreSame (raised.Task, completed, "Expected ConnectionClosed to fire after the remote end closed the socket.");
			Assert.IsNull (raised.Task.Result, "A graceful remote close should report a null exception.");
			}
		finally
			{
			session.Dispose ();
			}
		}

	[TestMethod]
	public async Task ConnectionClosed_DoesNotFire_OnOwnDispose ()
		{
		using FakeCompanionTcpHost host = new (pin: FakeCompanionDevice.PIN_CODE);
		host.AcceptOne ();

		AppleTvCompanionSession session = await ConnectSessionAsync (host).ConfigureAwait (false);

		bool raised = false;
		session.ConnectionClosed += _ => raised = true;

		session.Dispose ();

		// Give any (incorrect) asynchronous ReadLoop/event delivery a chance to
		// happen before asserting it didn't.
		await Task.Delay (TimeSpan.FromMilliseconds (250)).ConfigureAwait (false);

		Assert.IsFalse (raised, "Disposing our own session must not raise ConnectionClosed.");
		}

	private static async Task<AppleTvCompanionSession> ConnectSessionAsync (FakeCompanionTcpHost host)
		{
		using AppleTvCompanionPairing pairing = await AppleTvCompanionPairing.BeginAsync ("127.0.0.1", host.Port, CancellationToken.None).ConfigureAwait (false);
		AppleTvStoredDevice device = await pairing.CompleteAsync (FakeCompanionDevice.PIN_CODE, "Living Room", "127.0.0.1", host.Port, CancellationToken.None).ConfigureAwait (false);

		host.AcceptOne ();
		return await AppleTvCompanionSession.ConnectAsync (
			"127.0.0.1",
			host.Port,
			device.ToCredentials (),
			device.StableIdentifier,
			"Living Room",
			CancellationToken.None,
			log: null).ConfigureAwait (false);
		}
	}
