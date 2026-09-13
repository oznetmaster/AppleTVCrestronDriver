// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using AppleTvControlLibrary.Discovery.Companion;

using NUnit.Framework;
using NUnit.Framework.Legacy;

using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace AppleTVCrestronDriver.Tests;

/// <summary>
/// Covers the discovery adapter's cancellation contract. Configuration and saved-identity
/// orchestration are covered by AppleTvConfigurationTests with an in-memory discovery source.
/// </summary>
[TestFixture]
public sealed class AppleTvDiscoveryTests
	{
	[Test]
	public void AppleTvMulticastDiscoveryAdapter_ImplementsIAppleTvDiscovery ()
		{
		Assert.IsInstanceOf<IAppleTvDiscovery> (new AppleTvMulticastDiscoveryAdapter ());
		}

	[Test]
	public async Task DiscoverByNameAsync_AlreadyCancelled_CompletesWithoutFindingAnything ()
		{
		// MulticastCompanionDiscovery.ScanCoreAsync catches OperationCanceledException/
		// ObjectDisposedException internally (the cancellation closes its socket to unblock a
		// pending receive) rather than letting it propagate, so an already-cancelled token simply
		// yields no result instead of throwing.
		IAppleTvDiscovery discovery = new AppleTvMulticastDiscoveryAdapter ();
		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		CompanionDiscoveryResult result = await discovery.DiscoverByNameAsync ("Lounge", TimeSpan.FromSeconds (5), cts.Token);

		Assert.IsNull (result);
		}

	[Test]
	public async Task ScanAsync_AlreadyCancelled_CompletesWithoutFindingAnything ()
		{
		IAppleTvDiscovery discovery = new AppleTvMulticastDiscoveryAdapter ();
		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		IReadOnlyList<CompanionDiscoveryResult> results = await discovery.ScanAsync (TimeSpan.FromSeconds (5), cts.Token);

		Assert.AreEqual (0, results.Count);
		}

	[Test]
	public async Task FakeDiscovery_CanSubstituteForTheRealAdapter ()
		{
		// Demonstrates the seam's actual purpose: orchestration code written against
		// IAppleTvDiscovery (rather than MulticastCompanionDiscovery directly) can be driven with
		// a deterministic fake in tests.
		var expected = new CompanionDiscoveryResult ("Lounge", null, 0, "unique-1", CompanionPairingRequirement.Mandatory, new Dictionary<string, string> ());
		IAppleTvDiscovery discovery = new FakeDiscovery { ResultToReturn = expected };

		CompanionDiscoveryResult actual = await discovery.DiscoverByNameAsync ("Lounge", TimeSpan.FromSeconds (5), CancellationToken.None);

		Assert.AreSame (expected, actual);
		}

	private sealed class FakeDiscovery : IAppleTvDiscovery
		{
		internal CompanionDiscoveryResult ResultToReturn
			{
			get; set;
			}

		public Task<CompanionDiscoveryResult> DiscoverByNameAsync (string appleTvName, TimeSpan timeout, CancellationToken cancellationToken)
			=> Task.FromResult (ResultToReturn);

		public Task<IReadOnlyList<CompanionDiscoveryResult>> ScanAsync (TimeSpan timeout, CancellationToken cancellationToken)
			=> Task.FromResult<IReadOnlyList<CompanionDiscoveryResult>> ([]);
		}
	}