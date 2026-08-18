// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using AppleTvControlLibrary.Discovery.Companion;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

/// <summary>
/// Covers <see cref="IAppleTvDiscovery"/> and <see cref="AppleTvMulticastDiscoveryAdapter"/>: the
/// seam extracted so discovery-dependent orchestration logic (currently
/// <c>AppleTvVideoServer.ConfigureAppleTvAsync</c>) can eventually be unit-tested off-box against a
/// fake instead of the real mDNS-based <c>MulticastCompanionDiscovery</c>, whose static/instance
/// calls require a real network and cannot be driven deterministically in a unit test.
/// </summary>
/// <remarks>
/// <c>ConfigureAppleTvAsync</c> itself has not been extracted off of <see cref="AppleTvVideoServer"/>
/// yet (that is step 5, tracked separately), so this class can only cover the seam - the interface
/// contract and the adapter's delegation to the real scanner - not yet the orchestration logic that
/// consumes it.
/// </remarks>
[TestClass]
public sealed class AppleTvDiscoveryTests
	{
	[TestMethod]
	public void AppleTvMulticastDiscoveryAdapter_ImplementsIAppleTvDiscovery ()
		{
		Assert.IsInstanceOfType<IAppleTvDiscovery> (new AppleTvMulticastDiscoveryAdapter ());
		}

	[TestMethod]
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

	[TestMethod]
	public async Task ScanAsync_AlreadyCancelled_CompletesWithoutFindingAnything ()
		{
		IAppleTvDiscovery discovery = new AppleTvMulticastDiscoveryAdapter ();
		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		IReadOnlyList<CompanionDiscoveryResult> results = await discovery.ScanAsync (TimeSpan.FromSeconds (5), cts.Token);

		Assert.AreEqual (0, results.Count);
		}

	[TestMethod]
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
		internal CompanionDiscoveryResult ResultToReturn { get; set; }

		public Task<CompanionDiscoveryResult> DiscoverByNameAsync (string appleTvName, TimeSpan timeout, CancellationToken cancellationToken)
			=> Task.FromResult (ResultToReturn);

		public Task<IReadOnlyList<CompanionDiscoveryResult>> ScanAsync (TimeSpan timeout, CancellationToken cancellationToken)
			=> Task.FromResult<IReadOnlyList<CompanionDiscoveryResult>> ([]);
		}
	}
