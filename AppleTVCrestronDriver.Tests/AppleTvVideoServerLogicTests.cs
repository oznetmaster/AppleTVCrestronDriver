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
/// Covers the remaining leaf behaviors on <see cref="AppleTvVideoServerLogic"/> that are now
/// testable off-box: persisted-device round-tripping, pairing-cleared status, and the
/// PairNow-turned-off handler (moved here from <c>AppleTvVideoServer</c> since it depends only on
/// <see cref="IAppleTvDriverHost"/>/<see cref="IAppleTvProtocol"/> plus shared pairing session
/// state).
/// </summary>
[TestClass]
public sealed class AppleTvVideoServerLogicTests
	{
	[TestInitialize]
	public void Setup ()
		{
		AppleTvPairingSessionState.Instance.CurrentProtocol = null;
		AppleTvPairingSessionState.Instance.CurrentDriver = null;
		AppleTvPairingSessionState.Instance.Clear ();
		}

	[TestMethod]
	public void SaveStoredDevice_then_LoadStoredDevice_RoundTrips_the_device ()
		{
		var host = new FakeDriverHost ();
		var logic = new AppleTvVideoServerLogic (host, () => host);
		var device = new AppleTvStoredDevice { Name = "Lounge", UniqueId = "unique-1" };

		logic.SaveStoredDevice (device);

		Assert.AreSame (device, logic.LoadStoredDevice ());
		}

	[TestMethod]
	public void LoadStoredDevice_Discards_a_device_with_a_blank_UniqueId ()
		{
		var host = new FakeDriverHost { StoredDevice = new AppleTvStoredDevice { Name = "Lounge", UniqueId = "" } };
		var logic = new AppleTvVideoServerLogic (host, () => host);

		Assert.IsNull (logic.LoadStoredDevice ());
		}

	[TestMethod]
	public void LoadStoredDevice_Returns_null_and_logs_When_the_host_throws ()
		{
		var host = new FakeDriverHost { ThrowOnGetSetting = true };
		var logic = new AppleTvVideoServerLogic (host, () => host);

		Assert.IsNull (logic.LoadStoredDevice ());
		Assert.IsTrue (host.Log.Any (m => m.Contains ("InvalidOperationException")));
		}

	[TestMethod]
	public void ClearPairing_Logs_only_When_a_pairing_session_was_active ()
		{
		var host = new FakeDriverHost ();
		var logic = new AppleTvVideoServerLogic (host, () => host);

		logic.ClearPairing ();

		Assert.IsFalse (host.Log.Any (m => m.Contains ("Clearing the active pairing session")));
		}

	[TestMethod]
	public void HandlePairNowTurnedOff_When_paired_Shows_the_repair_prompt_and_no_pin_needed ()
		{
		var host = new FakeDriverHost { StoredDevice = PairedDevice () };
		var logic = new AppleTvVideoServerLogic (host, () => host);
		var protocol = new FakeProtocol ();

		logic.HandlePairNowTurnedOff (protocol);

		Assert.IsTrue (host.Log.Any (m => m.Contains ("already paired. Turn this on to re-pair")));
		Assert.IsTrue (host.Log.Any (m => m.Contains ("Pairing is complete; no code is currently needed")));
		}

	[TestMethod]
	public void HandlePairNowTurnedOff_When_not_paired_and_no_prior_pin_Prompts_for_a_pin ()
		{
		var host = new FakeDriverHost ();
		var logic = new AppleTvVideoServerLogic (host, () => host);
		var protocol = new FakeProtocol { PairingPin = string.Empty };

		logic.HandlePairNowTurnedOff (protocol);

		Assert.IsTrue (host.Log.Any (m => m.Contains ("Turn this on to pair.")));
		Assert.IsTrue (host.Log.Any (m => m == "Enter the four-digit pairing code currently displayed on the Apple TV."));
		}

	[TestMethod]
	public void HandlePairNowTurnedOff_When_not_paired_and_a_prior_pin_exists_Asks_for_a_new_pin ()
		{
		var host = new FakeDriverHost ();
		var logic = new AppleTvVideoServerLogic (host, () => host);
		var protocol = new FakeProtocol { PairingPin = "1234" };

		logic.HandlePairNowTurnedOff (protocol);

		Assert.IsTrue (host.Log.Any (m => m == "Enter the new four-digit pairing code currently displayed on the Apple TV."));
		}

	[TestMethod]
	public void HandlePairNowTurnedOff_Clears_an_active_pairing_session ()
		{
		var host = new FakeDriverHost ();
		var logic = new AppleTvVideoServerLogic (host, () => host);
		var protocol = new FakeProtocol ();
		AppleTvPairingSessionState.Instance.Target = new PairingTarget ("10.0.0.1", 1234, "unique", "Lounge");

		logic.HandlePairNowTurnedOff (protocol);

		Assert.IsTrue (host.Log.Any (m => m.Contains ("Pair Now was turned off")));
		}

	private static AppleTvStoredDevice PairedDevice ()
		{
		return new AppleTvStoredDevice
			{
			Name = "Lounge",
			UniqueId = "unique-1",
			Ltpk = [1],
			Ltsk = [1],
			AtvId = [1],
			ClientId = [1],
			};
		}

	private sealed class FakeDriverHost : IAppleTvDriverHost
		{
		internal readonly List<string> Log = [];

		internal AppleTvStoredDevice StoredDevice { get; set; }

		internal bool ThrowOnGetSetting { get; set; }

		public string BaseModel => "FakeModel";

		public object GetSetting (string key)
			{
			if (ThrowOnGetSetting)
				{
				throw new InvalidOperationException ("Simulated settings failure.");
				}

			return StoredDevice;
			}

		public void SaveSetting (string key, object value) => StoredDevice = value as AppleTvStoredDevice;

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
