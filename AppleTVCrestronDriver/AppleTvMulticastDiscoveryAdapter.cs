// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AppleTvControlLibrary.Discovery.Companion;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Real, mDNS-based <see cref="IAppleTvDiscovery"/> implementation backed by
/// <see cref="MulticastCompanionDiscovery"/>. This is the implementation used in production;
/// tests substitute a fake instead.
/// </summary>
internal sealed class AppleTvMulticastDiscoveryAdapter : IAppleTvDiscovery
	{
	public Task<CompanionDiscoveryResult> DiscoverByNameAsync (string appleTvName, TimeSpan timeout, CancellationToken cancellationToken)
		=> MulticastCompanionDiscovery.DiscoveryAsync (appleTvName, timeout, cancellationToken);

	public Task<IReadOnlyList<CompanionDiscoveryResult>> ScanAsync (TimeSpan timeout, CancellationToken cancellationToken)
		=> new MulticastCompanionDiscovery ().ScanAsync (timeout, cancellationToken);
	}
