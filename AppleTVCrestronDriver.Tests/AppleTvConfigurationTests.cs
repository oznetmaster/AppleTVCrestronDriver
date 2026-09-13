// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;
using AppleTvControlLibrary.Auth;
using AppleTvControlLibrary.Discovery.Companion;
using NUnit.Framework;

namespace AppleTVCrestronDriver.Tests;

[TestFixture, NonParallelizable]
public sealed class AppleTvConfigurationTests
	{
	private Host _host;
	private Scanner _scanner;
	private FakeCredentialFileStore _store;
	private AppleTvVideoServerLogic _logic;
	private Protocol _protocol;
	private CancellationTokenSource _cancellation;
	[SetUp]
	public void SetUp ()
		{
		_host = new Host ();
		_scanner = new Scanner ();
		_store = new FakeCredentialFileStore ();
		_protocol = new Protocol ();
		_logic = new AppleTvVideoServerLogic (_host, () => _host, discovery: _scanner, credentialStore: _store);
		_cancellation = new CancellationTokenSource ();
		AppleTvPairingSessionState.Instance.ConfigureCancellation = _cancellation;
		AppleTvPairingSessionState.Instance.CurrentProtocol = _protocol;
		}
	[TearDown]
	public void TearDown ()
		{
		_cancellation.Dispose ();
		AppleTvPairingSessionState.Instance.ConfigureCancellation = null;
		AppleTvPairingSessionState.Instance.CurrentProtocol = null;
		}
	private static AppleTvStoredDevice Paired () => new () { Name = "Lounge", UniqueId = "test-id", Address = "192.0.2.1", Port = 4321, Ltpk = new byte[] { 1 }, Ltsk = new byte[] { 1 }, AtvId = new byte[] { 1 }, ClientId = new byte[] { 1 } };
	private static CompanionDiscoveryResult Found () => new ("Lounge", IPAddress.Parse ("192.0.2.2"), 4322, "test-id", CompanionPairingRequirement.Mandatory, new Dictionary<string, string> ());
	[Test]
	public async Task SavedPairingConnectsWithoutDiscovery ()
		{
		_host.Device = Paired ();
		int connects = 0;
		await _logic.ConfigureAppleTvAsync (_protocol, "Lounge", (p, device, address, port) =>
			{
			connects++;
			Assert.That (device, Is.SameAs (_host.Device));
			Assert.That (address, Is.EqualTo ("192.0.2.1"));
			Assert.That (port, Is.EqualTo (4321));
			return Task.CompletedTask;
			});
		Assert.That (connects, Is.EqualTo (1));
		Assert.That (_scanner.Calls, Is.Zero);
		}
	[Test]
	public async Task StoredUnpairedIdentityIsRestoredWithoutRescanningOrConnecting ()
		{
		AppleTvStoredDevice.Save (new AppleTvStoredDevice { Name = "Lounge", UniqueId = "test-id", Address = "192.0.2.1", Port = 4321 }, _store);
		await _logic.ConfigureAppleTvAsync (_protocol, "Lounge", NoConnect);
		Assert.That (_host.Device.UniqueId, Is.EqualTo ("test-id"));
		Assert.That (_host.Device.IsPaired, Is.False);
		Assert.That (_scanner.Calls, Is.Zero);
		}
	[Test]
	public async Task DiscoverySavesIdentityForPairingWithoutConnecting ()
		{
		_scanner.Result = Found ();
		await _logic.ConfigureAppleTvAsync (_protocol, "Lounge", NoConnect);
		Assert.That (_host.Device.Address, Is.EqualTo ("192.0.2.2"));
		Assert.That (AppleTvStoredDevice.LoadForName ("Lounge", _store).UniqueId, Is.EqualTo ("test-id"));
		Assert.That (_scanner.Calls, Is.EqualTo (1));
		}
	[Test]
	public async Task ChangingConfiguredNameDiscardsTheOldPairing ()
		{
		_host.Device = Paired ();
		_protocol.IsConnected = true;
		await _logic.ConfigureAppleTvAsync (_protocol, "Office", NoConnect);
		Assert.That (_host.Device, Is.Null);
		Assert.That (_protocol.IsConnected, Is.False);
		Assert.That (_scanner.Calls, Is.EqualTo (1));
		}
	[Test]
	public async Task SupersededDiscoveryDoesNotPersistOrChangeStatus ()
		{
		_scanner.Result = Found ();
		_scanner.BeforeReturn = () => _cancellation.Cancel ();
		await _logic.ConfigureAppleTvAsync (_protocol, "Lounge", NoConnect);
		Assert.That (_host.Device, Is.Null);
		Assert.That (_host.Status, Is.Empty);
		Assert.That (_store.EnumerateEntries (), Is.Empty);
		}
	[Test]
	public async Task SupersededSavedConnectionDoesNotOverwriteNewerStatus ()
		{
		_host.Device = Paired ();
		await _logic.ConfigureAppleTvAsync (_protocol, "Lounge", (p, d, a, port) =>
			{
			_cancellation.Cancel ();
			_host.Status.Clear ();
			return Task.CompletedTask;
			});
		Assert.That (_host.Status, Is.Empty);
		}
	private static Task NoConnect (IAppleTvProtocol protocol, AppleTvStoredDevice device, string address, int port)
		{
		Assert.Fail ("Discovery must not connect an unpaired device.");
		return Task.CompletedTask;
		}
	private sealed class Host : IAppleTvDriverHost
		{
		internal AppleTvStoredDevice Device;
		internal readonly List<string> Status = new ();
		public string BaseModel => "configuration-test";
		public object GetSetting (string key) => Device;
		public void SaveSetting (string key, object value) => Device = value as AppleTvStoredDevice;
		public void ModifyUserAttribute (string id, string description) => Status.Add (description);
		public void LogDiagnostic (string message) { }
		}
	private sealed class Protocol : IAppleTvProtocol
		{
		public string AppleTvName { get; set; }
		public string PairingPin { get; set; }
		public bool IsConnected { get; set; }
		public void SetCompanionConnectionState (bool connected) => IsConnected = connected;
		public Task ConnectCompanionAsync (string address, int port, HapCredentials credentials, string stableIdentifier, string appleTvName) => Task.CompletedTask;
		}
	private sealed class Scanner : IAppleTvDiscovery
		{
		internal CompanionDiscoveryResult Result;
		internal int Calls;
		internal System.Action BeforeReturn;
		public Task<CompanionDiscoveryResult> DiscoverByNameAsync (string name, TimeSpan timeout, CancellationToken cancellationToken)
			{
			Calls++;
			BeforeReturn?.Invoke ();
			return Task.FromResult (Result);
			}
		public Task<IReadOnlyList<CompanionDiscoveryResult>> ScanAsync (TimeSpan timeout, CancellationToken cancellationToken)
			{
			Calls++;
			BeforeReturn?.Invoke ();
			return Task.FromResult<IReadOnlyList<CompanionDiscoveryResult>> (Result == null ? Array.Empty<CompanionDiscoveryResult> () : new[] { Result });
			}
		}
	}