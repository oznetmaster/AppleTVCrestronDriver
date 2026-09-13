// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using AppleTvControlLibrary.Auth;

using NUnit.Framework;
using NUnit.Framework.Legacy;

using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace AppleTVCrestronDriver.Tests;

/// <summary>
/// Covers <see cref="AppleTvVideoServerLogic.HandleCompanionDisconnectedAsync"/>'s bounded backoff
/// retry schedule off-box, including abandoning the schedule when a newer driver instance has taken
/// over and stopping as soon as reconnection succeeds.
/// </summary>
[TestFixture]
public sealed class AppleTvReconnectTests
	{
	[SetUp]
	public void Setup ()
		{
		AppleTvPairingSessionState.Instance.CurrentProtocol = null;
		AppleTvPairingSessionState.Instance.CurrentDriver = null;
		}

	[Test]
	public async Task Reconnect_follows_the_bounded_backoff_schedule ()
		{
		var delays = new List<TimeSpan> ();
		var host = new FakeDriverHost ();
		var protocol = new FakeProtocol { AppleTvName = "Lounge", IsConnected = false };
		AppleTvPairingSessionState.Instance.CurrentProtocol = protocol;

		var logic = new AppleTvVideoServerLogic (host, () => host, duration =>
			{
				delays.Add (duration);
				return Task.CompletedTask;
			});

		await logic.HandleCompanionDisconnectedAsync (protocol, _ => Task.CompletedTask);

		CollectionAssert.AreEqual (
			new[] { 2, 5, 10, 20, 30 }.Select (seconds => TimeSpan.FromSeconds (seconds)).ToArray (),
			delays);
		Assert.IsTrue (host.Log.Any (m => m.Contains ("Could not reconnect")));
		}

	[Test]
	public async Task Reconnect_abandons_when_a_newer_instance_takes_over ()
		{
		var delays = new List<TimeSpan> ();
		var host = new FakeDriverHost ();
		var stale = new FakeProtocol { AppleTvName = "Lounge" };
		AppleTvPairingSessionState.Instance.CurrentProtocol = new FakeProtocol ();

		var logic = new AppleTvVideoServerLogic (host, () => host, duration =>
			{
				delays.Add (duration);
				return Task.CompletedTask;
			});

		await logic.HandleCompanionDisconnectedAsync (stale, _ => Task.CompletedTask);

		Assert.AreEqual (0, delays.Count, "A superseded instance must not retry at all.");
		Assert.IsTrue (host.Log.Any (m => m.Contains ("newer driver instance")));
		}

	[Test]
	public async Task Reconnect_stops_as_soon_as_the_connection_is_restored ()
		{
		var delays = new List<TimeSpan> ();
		var host = new FakeDriverHost ();
		var protocol = new FakeProtocol { AppleTvName = "Lounge" };
		AppleTvPairingSessionState.Instance.CurrentProtocol = protocol;

		var logic = new AppleTvVideoServerLogic (host, () => host, duration =>
			{
				delays.Add (duration);
				return Task.CompletedTask;
			});

		await logic.HandleCompanionDisconnectedAsync (protocol, p =>
			{
				((FakeProtocol)p).IsConnected = true;
				return Task.CompletedTask;
			});

		Assert.AreEqual (1, delays.Count);
		Assert.IsTrue (host.Log.Any (m => m.Contains ("Reconnected successfully")));
		}

	[Test]
	public async Task Reconnect_abandons_when_replaced_during_backoff ()
		{
		var host = new FakeDriverHost ();
		var protocol = new FakeProtocol ();
		AppleTvPairingSessionState.Instance.CurrentProtocol = protocol;
		var logic = new AppleTvVideoServerLogic (host, () => host, duration =>
			{
			AppleTvPairingSessionState.Instance.CurrentProtocol = new FakeProtocol ();
			return Task.CompletedTask;
			});
		int attempts = 0;
		await logic.HandleCompanionDisconnectedAsync (protocol, p => { attempts++; return Task.CompletedTask; });
		Assert.AreEqual (0, attempts);
		}
	[Test]
	public async Task Reconnect_retries_after_a_failed_attempt ()
		{
		var host = new FakeDriverHost ();
		var protocol = new FakeProtocol ();
		AppleTvPairingSessionState.Instance.CurrentProtocol = protocol;
		var logic = new AppleTvVideoServerLogic (host, () => host, duration => Task.CompletedTask);
		int attempts = 0;
		await logic.HandleCompanionDisconnectedAsync (protocol, p =>
			{
			if (++attempts == 1) throw new InvalidOperationException ("Transient connection failure");
			protocol.IsConnected = true;
			return Task.CompletedTask;
			});
		Assert.AreEqual (2, attempts);
		Assert.IsTrue (host.Log.Any (line => line.Contains ("Reconnected successfully")));
		}
	private sealed class FakeDriverHost : IAppleTvDriverHost
		{
		internal readonly List<string> Log = [];

		public string BaseModel => "FakeModel";

		public object GetSetting (string key) => null;

		public void SaveSetting (string key, object value)
			{
			}

		public void ModifyUserAttribute (string attributeId, string description) => Log.Add (description);

		public void LogDiagnostic (string message) => Log.Add (message);
		}

	private sealed class FakeProtocol : IAppleTvProtocol
		{
		public string AppleTvName { get; set; } = string.Empty;

		public string PairingPin { get; set; } = string.Empty;

		public bool IsConnected
			{
			get; set;
			}

		public Task ConnectCompanionAsync (string address, int port, HapCredentials credentials, string stableIdentifier, string appleTvName)
			=> Task.CompletedTask;

		public void SetCompanionConnectionState (bool connected) => IsConnected = connected;
		}
	}