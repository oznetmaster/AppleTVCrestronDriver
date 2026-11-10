// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Abstracts the storage of paired Apple TV credential files away from the concrete
/// Crestron filesystem APIs, so <see cref="AppleTvStoredDevice"/>'s save/lookup logic
/// (JSON round-trip, name matching, malformed-file handling) can be exercised against
/// an in-memory fake in unit tests instead of requiring a real Crestron control system.
/// </summary>
internal interface ICredentialFileStore
	{
	/// <summary>
	/// Enumerates the identifiers (e.g. file names) of every stored credential entry.
	/// </summary>
	IEnumerable<string> EnumerateEntries ();

	/// <summary>
	/// Opens the entry with the given identifier for reading.
	/// </summary>
	Stream OpenRead (string entryId);

	/// <summary>
	/// Commits a complete record, retaining any existing entry if writing fails.
	/// </summary>
	void WriteAtomically (string entryId, byte[] contents);
	}