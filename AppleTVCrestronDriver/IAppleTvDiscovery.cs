// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AppleTvControlLibrary.Discovery.Companion;

namespace AppleTV.CrestronDriver;

/// <summary>
/// The subset of <c>MulticastCompanionDiscovery</c>'s surface that
/// <see cref="AppleTvVideoServer"/>'s orchestration logic depends on. Extracted so that logic
/// which needs discovery can eventually be unit-tested off-box against a fake instead of the
/// real mDNS-based scanner, whose static/instance calls cannot be driven deterministically (or
/// at all, without a real network) in a unit test.
/// </summary>
internal interface IAppleTvDiscovery
	{
	/// <summary>
	/// Discovers a Companion Link device by its exact mDNS service instance name, completing as
	/// soon as that device has been resolved or the timeout elapses.
	/// </summary>
	Task<CompanionDiscoveryResult> DiscoverByNameAsync (string appleTvName, System.TimeSpan timeout, CancellationToken cancellationToken);

	/// <summary>
	/// Scans for every Companion Link device visible on the network within the given timeout.
	/// </summary>
	Task<IReadOnlyList<CompanionDiscoveryResult>> ScanAsync (System.TimeSpan timeout, CancellationToken cancellationToken);
	}
