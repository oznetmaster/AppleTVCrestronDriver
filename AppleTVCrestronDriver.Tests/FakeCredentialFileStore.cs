// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;

using AppleTV.CrestronDriver;

namespace AppleTVCrestronDriver.Tests;

/// <summary>
/// In-memory fake of <see cref="ICredentialFileStore"/> used to exercise
/// <see cref="AppleTvStoredDevice"/>'s save/lookup logic without touching a real filesystem.
/// </summary>
internal sealed class FakeCredentialFileStore : ICredentialFileStore
	{
	private readonly Dictionary<string, byte[]> _entries = [];

	public IEnumerable<string> EnumerateEntries () => [.. _entries.Keys];

	public Stream OpenRead (string entryId) => new MemoryStream (_entries[entryId], writable: false);

	internal bool FailNextWrite { get; set; }

	public void WriteAtomically (string entryId, byte[] contents)
		{
		if (FailNextWrite)
			{
			FailNextWrite = false;
			throw new IOException ("Synthetic credential commit failure.");
			}
		_entries[entryId] = (byte[])contents.Clone ();
		}

	internal void AddRawEntry (string entryId, byte[] content) => _entries[entryId] = content;

	internal bool ContainsEntry (string entryId) => _entries.ContainsKey (entryId);

	}
