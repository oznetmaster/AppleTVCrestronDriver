// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

/// <summary>
/// Exercises <see cref="AppleTvBridgeServer"/> end-to-end over a real loopback TCP socket: a
/// fake bridge client connects, sends a tokenized command line, and receives a broadcast
/// tokenized event line, mirroring how the future extension-driver client and this Crestron
/// driver's bridge server are expected to communicate.
/// </summary>
[TestClass]
public sealed class AppleTvBridgeServerTests
	{
	[TestMethod]
	public async Task BroadcastEvent_ConnectedClient_ReceivesLine ()
		{
		int port = GetFreeLoopbackPort ();
		using AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);

		using TcpClient client = new ();
		await client.ConnectAsync (IPAddress.Loopback, port).ConfigureAwait (false);

		// Give the server's accept loop a moment to register the new connection before
		// broadcasting, since AcceptTcpClientAsync completes asynchronously.
		await WaitUntilAsync (() => server.ConnectedClientCountForTests > 0).ConfigureAwait (false);

		server.BroadcastEvent ("EVT:CONNECTED");

		string line = await ReadLineAsync (client).ConfigureAwait (false);
		Assert.AreEqual ("EVT:CONNECTED", line);
		}

	[TestMethod]
	public async Task ClientCommand_IsDeliveredToHandler ()
		{
		int port = GetFreeLoopbackPort ();
		using AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);
		var received = new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);
		server.Handler = new RecordingHandler (received);

		using TcpClient client = new ();
		await client.ConnectAsync (IPAddress.Loopback, port).ConfigureAwait (false);
		await WriteLineAsync (client, "CMD:HID:Select").ConfigureAwait (false);

		Task completed = await Task.WhenAny (received.Task, Task.Delay (TimeSpan.FromSeconds (5))).ConfigureAwait (false);
		Assert.AreSame (received.Task, completed, "Expected the bridge server to deliver the command line to the handler.");
		Assert.AreEqual ("CMD:HID:Select", received.Task.Result);
		}

	[TestMethod]
	public async Task ClientLaunchCommand_IsDeliveredToHandler ()
		{
		int port = GetFreeLoopbackPort ();
		using AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);
		var received = new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);
		server.Handler = new RecordingHandler (received);

		using TcpClient client = new ();
		await client.ConnectAsync (IPAddress.Loopback, port).ConfigureAwait (false);
		await WriteLineAsync (client, "CMD:LAUNCH:com.apple.tv").ConfigureAwait (false);

		Task completed = await Task.WhenAny (received.Task, Task.Delay (TimeSpan.FromSeconds (5))).ConfigureAwait (false);
		Assert.AreSame (received.Task, completed, "Expected the bridge server to deliver the launch command line to the handler.");
		Assert.AreEqual ("CMD:LAUNCH:com.apple.tv", received.Task.Result);
		}

	[TestMethod]
	public async Task BroadcastEvent_AppsLine_IsReceivedByClient ()
		{
		int port = GetFreeLoopbackPort ();
		using AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);

		using TcpClient client = new ();
		await client.ConnectAsync (IPAddress.Loopback, port).ConfigureAwait (false);
		await WaitUntilAsync (() => server.ConnectedClientCountForTests > 0).ConfigureAwait (false);

		string appsToken = AppleTvBridgeProtocol.EncodeApps ([("com.apple.tv", "Apple TV")]);
		server.BroadcastEvent (AppleTvBridgeProtocol.EVENT_APPS_PREFIX + appsToken);

		string line = await ReadLineAsync (client).ConfigureAwait (false);
		Assert.AreEqual (AppleTvBridgeProtocol.EVENT_APPS_PREFIX + appsToken, line);
		}

	// Regression tests for AppleTvBridgeServerHandlerRegistration: a stale-handler race between
	// AppleTvBridgeServer (kept alive across Crestron Home reinitializations via
	// AppleTvBridgeServerRegistry) and successive AppleTvVideoServerProtocol instances.
	// AppleTvVideoServer.Dispose() must only clear the bridge server's Handler if it is still
	// the one this instance itself installed, so a disposed instance being torn down can never
	// clobber a newer instance's already-registered handler. Without the "only if still current"
	// guard, disposal would unconditionally null out whichever handler happens to be registered -
	// including a newer, live one - leaving every subsequent bridge command silently dropped even
	// though the newer instance's Companion Link session is perfectly healthy.
	[TestMethod]
	public async Task HandlerRegistration_ClearIfCurrent_SupersededByNewerRegistration_DoesNotClearNewerHandler ()
		{
		int port = GetFreeLoopbackPort ();
		using AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);

		var firstReceived = new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);
		AppleTvBridgeServerHandlerRegistration firstRegistration = AppleTvBridgeServerHandlerRegistration.Install (server, new RecordingHandler (firstReceived));

		// Simulate Crestron Home reinitializing the driver: a second instance connects and
		// installs its own handler before the first instance is disposed.
		var secondReceived = new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);
		var secondHandler = new RecordingHandler (secondReceived);
		AppleTvBridgeServerHandlerRegistration secondRegistration = AppleTvBridgeServerHandlerRegistration.Install (server, secondHandler);

		// Mirrors AppleTvVideoServer.Dispose(): the superseded first instance clears its own
		// registration, which must be a no-op since it is no longer current.
		firstRegistration.ClearIfCurrent ();

		Assert.AreSame (secondHandler, server.Handler, "Disposing the superseded first instance must not clear the newer, current handler.");

		using TcpClient client = new ();
		await client.ConnectAsync (IPAddress.Loopback, port).ConfigureAwait (false);
		await WriteLineAsync (client, "CMD:HID:Select").ConfigureAwait (false);

		Task completed = await Task.WhenAny (secondReceived.Task, Task.Delay (TimeSpan.FromSeconds (5))).ConfigureAwait (false);
		Assert.AreSame (secondReceived.Task, completed, "Expected the command to still reach the current (second) handler.");
		Assert.IsFalse (firstReceived.Task.IsCompleted, "The superseded first handler must never receive commands after being replaced.");
		}

	[TestMethod]
	public async Task HandlerRegistration_ClearIfCurrent_StillCurrent_StopsDeliveringToDisposedHandler ()
		{
		int port = GetFreeLoopbackPort ();
		using AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);

		var received = new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);
		AppleTvBridgeServerHandlerRegistration registration = AppleTvBridgeServerHandlerRegistration.Install (server, new RecordingHandler (received));

		// Mirrors AppleTvVideoServer.Dispose(): it is still current, so it is cleared.
		registration.ClearIfCurrent ();

		Assert.IsNull (server.Handler, "The handler must be cleared once its owning instance is disposed and nothing newer replaced it.");

		using TcpClient client = new ();
		await client.ConnectAsync (IPAddress.Loopback, port).ConfigureAwait (false);
		await WriteLineAsync (client, "CMD:HID:Select").ConfigureAwait (false);

		Task completed = await Task.WhenAny (received.Task, Task.Delay (TimeSpan.FromMilliseconds (500))).ConfigureAwait (false);
		Assert.AreNotSame (received.Task, completed, "A command arriving after the owning instance disposed must not reach the stale handler.");
		}

	[TestMethod]
	public void HandlerRegistration_Install_NullBridgeServer_Throws ()
		{
		_ = Assert.ThrowsExactly<ArgumentNullException> (() => AppleTvBridgeServerHandlerRegistration.Install (null, new RecordingHandler (new TaskCompletionSource<string> ())));
		}

	[TestMethod]
	public void HandlerRegistration_Install_NullHandler_Throws ()
		{
		int port = GetFreeLoopbackPort ();
		using AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);

		_ = Assert.ThrowsExactly<ArgumentNullException> (() => AppleTvBridgeServerHandlerRegistration.Install (server, null));
		}

	[TestMethod]
	public void StartFirstAvailable_FirstCandidateTaken_BindsNextCandidate ()
		{
		int taken = GetFreeLoopbackPort ();
		var blocker = new TcpListener (IPAddress.Loopback, taken);
		blocker.Start ();
		try
			{
			int fallback = GetFreeLoopbackPort ();
			using AppleTvBridgeServer server = AppleTvBridgeServer.StartFirstAvailable ([taken, fallback], log: null);

			Assert.AreEqual (fallback, server.Port);
			}
		finally
			{
			blocker.Stop ();
			}
		}

	[TestMethod]
	public void StartFirstAvailable_AllCandidatesTaken_Throws ()
		{
		int first = GetFreeLoopbackPort ();
		int second = GetFreeLoopbackPort ();
		var firstBlocker = new TcpListener (IPAddress.Loopback, first);
		var secondBlocker = new TcpListener (IPAddress.Loopback, second);
		firstBlocker.Start ();
		secondBlocker.Start ();
		try
			{
			_ = Assert.ThrowsExactly<SocketException> (() => AppleTvBridgeServer.StartFirstAvailable ([first, second], log: null));
			}
		finally
			{
			firstBlocker.Stop ();
			secondBlocker.Stop ();
			}
		}

	private static int GetFreeLoopbackPort ()
		{
		var listener = new TcpListener (IPAddress.Loopback, 0);
		listener.Start ();
		try
			{
			return ((IPEndPoint)listener.LocalEndpoint).Port;
			}
		finally
			{
			listener.Stop ();
			}
		}

	private static async Task WaitUntilAsync (Func<bool> condition)
		{
		DateTime deadline = DateTime.UtcNow.AddSeconds (5);
		while (!condition () && DateTime.UtcNow < deadline)
			{
			await Task.Delay (10).ConfigureAwait (false);
			}
		}

	private static async Task<string> ReadLineAsync (TcpClient client)
		{
		NetworkStream stream = client.GetStream ();
		var buffer = new byte[1024];
		var text = new StringBuilder ();
		while (true)
			{
			int read = await stream.ReadAsync (buffer, 0, buffer.Length).ConfigureAwait (false);
			if (read <= 0)
				{
				return text.ToString ();
				}

			text.Append (Encoding.UTF8.GetString (buffer, 0, read));
			int newlineIndex = text.ToString ().IndexOf ('\n');
			if (newlineIndex >= 0)
				{
				return text.ToString (0, newlineIndex).TrimEnd ('\r');
				}
			}
		}

	private static async Task WriteLineAsync (TcpClient client, string line)
		{
		byte[] payload = Encoding.UTF8.GetBytes (line + "\n");
		await client.GetStream ().WriteAsync (payload, 0, payload.Length).ConfigureAwait (false);
		}

	private sealed class RecordingHandler : IAppleTvBridgeCommandHandler
		{
		private readonly TaskCompletionSource<string> _received;

		internal RecordingHandler (TaskCompletionSource<string> received) => _received = received;

		public void HandleBridgeCommand (string commandLine) => _received.TrySetResult (commandLine);
		}
	}
