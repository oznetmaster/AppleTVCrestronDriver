// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using AppleTvControlLibrary.Auth;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

/// <summary>
/// Covers <see cref="AppleTvVideoServerLogic.HandleCompanionDisconnectedAsync"/>'s bounded backoff
/// retry schedule off-box, including abandoning the schedule when a newer driver instance has taken
/// over and stopping as soon as reconnection succeeds.
/// </summary>
[TestClass]
public sealed class AppleTvReconnectTests
	{
	[TestInitialize]
	public void Setup ()
		{
		AppleTvPairingSessionState.Instance.CurrentProtocol = null;
		AppleTvPairingSessionState.Instance.CurrentDriver = null;
		}

	[TestMethod]
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

	[TestMethod]
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

	[TestMethod]
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
			((FakeProtocol) p).IsConnected = true;
			return Task.CompletedTask;
			});

		Assert.AreEqual (1, delays.Count);
		Assert.IsTrue (host.Log.Any (m => m.Contains ("Reconnected successfully")));
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

		public bool IsConnected { get; set; }

		public Task ConnectCompanionAsync (string address, int port, HapCredentials credentials, string stableIdentifier, string appleTvName)
			=> Task.CompletedTask;

		public void SetCompanionConnectionState (bool connected) => IsConnected = connected;
		}
	}
