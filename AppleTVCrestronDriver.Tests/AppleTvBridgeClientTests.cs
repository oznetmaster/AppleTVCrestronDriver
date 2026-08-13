// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

/// <summary>
/// Exercises <see cref="AppleTvBridgeClient"/> end-to-end against a real
/// <see cref="AppleTvBridgeServer"/> over loopback TCP, mirroring how the extension driver is
/// expected to connect to and drive the bridge exposed by the Crestron video server driver.
/// </summary>
[TestClass]
public sealed class AppleTvBridgeClientTests
	{
	[TestMethod]
	public async Task ConnectAsync_ConnectsToServer ()
		{
		int port = GetFreeLoopbackPort ();
		using AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);
		using var client = new AppleTvBridgeClient ();

		await client.ConnectAsync (port, CancellationToken.None).ConfigureAwait (false);

		Assert.IsTrue (client.IsConnected);
		}

	[TestMethod]
	public async Task SendCommandAsync_IsDeliveredToServerHandler ()
		{
		int port = GetFreeLoopbackPort ();
		using AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);
		var received = new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);
		server.Handler = new RecordingHandler (received);

		using var client = new AppleTvBridgeClient ();
		await client.ConnectAsync (port, CancellationToken.None).ConfigureAwait (false);

		await client.SendCommandAsync ("CMD:LAUNCH:com.apple.tv").ConfigureAwait (false);

		Task completed = await Task.WhenAny (received.Task, Task.Delay (TimeSpan.FromSeconds (5))).ConfigureAwait (false);
		Assert.AreSame (received.Task, completed, "Expected the server to receive the command sent by the client.");
		Assert.AreEqual ("CMD:LAUNCH:com.apple.tv", received.Task.Result);
		}

	[TestMethod]
	public async Task BroadcastEvent_IsReceivedByClient_AsLineReceived ()
		{
		int port = GetFreeLoopbackPort ();
		using AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);
		using var client = new AppleTvBridgeClient ();
		var received = new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);
		client.LineReceived += line => received.TrySetResult (line);

		await client.ConnectAsync (port, CancellationToken.None).ConfigureAwait (false);
		await WaitUntilAsync (() => server.ConnectedClientCountForTests > 0).ConfigureAwait (false);

		server.BroadcastEvent (AppleTvBridgeProtocol.EventPowerPrefix + "On");

		Task completed = await Task.WhenAny (received.Task, Task.Delay (TimeSpan.FromSeconds (5))).ConfigureAwait (false);
		Assert.AreSame (received.Task, completed, "Expected the client to receive the broadcast event line.");
		Assert.AreEqual (AppleTvBridgeProtocol.EventPowerPrefix + "On", received.Task.Result);
		}

	[TestMethod]
	public async Task BroadcastEvent_AppsLine_IsReceivedByClient_AndDecodable ()
		{
		int port = GetFreeLoopbackPort ();
		using AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);
		using var client = new AppleTvBridgeClient ();
		var received = new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);
		client.LineReceived += line => received.TrySetResult (line);

		await client.ConnectAsync (port, CancellationToken.None).ConfigureAwait (false);
		await WaitUntilAsync (() => server.ConnectedClientCountForTests > 0).ConfigureAwait (false);

		string appsToken = AppleTvBridgeProtocol.EncodeApps (new[] { ("com.apple.tv", "Apple TV"), ("com.netflix.Netflix", "Netflix") });
		server.BroadcastEvent (AppleTvBridgeProtocol.EventAppsPrefix + appsToken);

		Task completed = await Task.WhenAny (received.Task, Task.Delay (TimeSpan.FromSeconds (5))).ConfigureAwait (false);
		Assert.AreSame (received.Task, completed);
		string line = received.Task.Result;
		Assert.IsTrue (line.StartsWith (AppleTvBridgeProtocol.EventAppsPrefix, StringComparison.Ordinal));
		var decoded = AppleTvBridgeProtocol.DecodeApps (line.Substring (AppleTvBridgeProtocol.EventAppsPrefix.Length));
		Assert.AreEqual (2, decoded.Count);
		Assert.AreEqual ("com.apple.tv", decoded[0].BundleId);
		Assert.AreEqual ("Netflix", decoded[1].Name);
		}

	[TestMethod]
	public async Task Disconnected_IsRaised_WhenServerDisposes ()
		{
		int port = GetFreeLoopbackPort ();
		AppleTvBridgeServer server = AppleTvBridgeServer.Start (port, log: null);
		using var client = new AppleTvBridgeClient ();
		var disconnected = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		client.Disconnected += () => disconnected.TrySetResult (true);

		await client.ConnectAsync (port, CancellationToken.None).ConfigureAwait (false);
		await WaitUntilAsync (() => server.ConnectedClientCountForTests > 0).ConfigureAwait (false);

		server.Dispose ();

		Task completed = await Task.WhenAny (disconnected.Task, Task.Delay (TimeSpan.FromSeconds (5))).ConfigureAwait (false);
		Assert.AreSame (disconnected.Task, completed, "Expected Disconnected to be raised when the server closes the connection.");
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

	private sealed class RecordingHandler : IAppleTvBridgeCommandHandler
		{
		private readonly TaskCompletionSource<string> _received;

		internal RecordingHandler (TaskCompletionSource<string> received) => _received = received;

		public void HandleBridgeCommand (string commandLine) => _received.TrySetResult (commandLine);
		}
	}
