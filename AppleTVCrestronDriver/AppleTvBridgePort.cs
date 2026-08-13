// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Derives a deterministic loopback TCP port for the local command/event bridge from a paired
/// Apple TV's stable identity (<see cref="AppleTvStoredDevice.UniqueId"/> - the same value used
/// as the on-disk credential file lookup key). Every Crestron driver instance that ends up
/// owning the same Apple TV computes the exact same port from its <c>UniqueId</c>, and the
/// extension driver (a separate process/driver with no direct access to the credential store)
/// independently computes that same port once it has resolved the Apple TV's <c>UniqueId</c>, so
/// both sides agree on where to connect without any additional coordination.
/// </summary>
/// <remarks>
/// The primary port (<see cref="GetPort"/>) can occasionally be unavailable - either because it
/// collides with another Apple TV's derived port, or because some unrelated process on the host
/// already holds it. Neither side can tell the other "try a different port" out of band (the
/// extension driver has no channel to the Crestron driver except the bridge port itself), so
/// <see cref="GetPortCandidates"/> instead derives a full deterministic probing sequence from the
/// same <c>UniqueId</c>: both sides independently compute the identical ordered list and walk it
/// in lockstep - the server binds the first free candidate, and the client tries to connect to
/// each candidate in the same order until one accepts.
/// </remarks>
internal static class AppleTvBridgePort
{
	// A deliberately narrow, high, rarely-used range that keeps generated ports away from both
	// the well-known/registered range and common ephemeral-port collisions, while still leaving
	// enough room (10000 values) that hash collisions between different Apple TVs are unlikely.
	private const int MIN_PORT = 20000;
	private const int PORT_RANGE = 10000;

	// Both sides must agree on how many candidates to try before giving up. This is generous
	// enough to survive any realistic number of colliding/occupied ports while still failing
	// fast instead of scanning the whole range.
	internal const int MAX_CANDIDATES = 20;

	/// <summary>
	/// Computes the primary (first-choice) loopback bridge port for the given Apple TV stable
	/// identifier. Equivalent to <c>GetPortCandidates(uniqueId)</c>'s first value.
	/// </summary>
	/// <param name="uniqueId">
	/// The Apple TV's stable identifier (<see cref="AppleTvStoredDevice.UniqueId"/>).
	/// </param>
	/// <returns>A deterministic TCP port number in the range [20000, 29999].</returns>
	internal static int GetPort (string uniqueId)
		{
		using IEnumerator<int> candidates = GetPortCandidates (uniqueId).GetEnumerator ();
		_ = candidates.MoveNext ();
		return candidates.Current;
		}

	/// <summary>
	/// Computes the deterministic, ordered fallback sequence of loopback bridge ports for the
	/// given Apple TV stable identifier. Both the Crestron driver (binding) and the extension
	/// driver (connecting) enumerate this exact same sequence, in the exact same order, so they
	/// always agree on which port is "next" without needing to communicate that choice.
	/// </summary>
	/// <param name="uniqueId">
	/// The Apple TV's stable identifier (<see cref="AppleTvStoredDevice.UniqueId"/>).
	/// </param>
	/// <returns>
	/// An ordered sequence of up to <see cref="MAX_CANDIDATES"/> distinct TCP port numbers, each
	/// in the range [20000, 29999], starting with the primary hash-derived port followed by a
	/// deterministic linear probe of subsequent ports (wrapping within the range).
	/// </returns>
	internal static IEnumerable<int> GetPortCandidates (string uniqueId)
		{
		if (string.IsNullOrWhiteSpace (uniqueId))
			{
			throw new ArgumentException ("A stable Apple TV identifier is required.", nameof (uniqueId));
			}

		// SHA256 (rather than string.GetHashCode, which is not guaranteed stable across
		// processes/.NET versions) so that the Crestron driver process and the extension driver
		// process always compute the identical port sequence for the same UniqueId.
		byte[] hash;
		using (var sha256 = SHA256.Create ())
			{
			hash = sha256.ComputeHash (Encoding.UTF8.GetBytes (uniqueId.Trim ().ToUpperInvariant ()));
			}

		uint baseValue = BitConverter.ToUInt32 (hash, 0);

		for (int attempt = 0; attempt < MAX_CANDIDATES; attempt++)
			{
			uint offset = (uint)((baseValue + (uint)attempt) % PORT_RANGE);
			yield return MIN_PORT + (int)offset;
			}
		}
}
