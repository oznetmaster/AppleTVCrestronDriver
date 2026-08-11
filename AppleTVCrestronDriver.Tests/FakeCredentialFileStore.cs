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
	private readonly Dictionary<string, byte[]> _entries = new ();

	public IEnumerable<string> EnumerateEntries () => _entries.Keys.ToList ();

	public Stream OpenRead (string entryId) => new MemoryStream (_entries[entryId], writable: false);

	public Stream CreateWrite (string entryId)
		{
		return new CaptureOnDisposeStream (bytes => _entries[entryId] = bytes);
		}

	internal void AddRawEntry (string entryId, byte[] content) => _entries[entryId] = content;

	internal bool ContainsEntry (string entryId) => _entries.ContainsKey (entryId);

	private sealed class CaptureOnDisposeStream : MemoryStream
		{
		private readonly System.Action<byte[]> _onDispose;
		private bool _captured;

		internal CaptureOnDisposeStream (System.Action<byte[]> onDispose) => _onDispose = onDispose;

		protected override void Dispose (bool disposing)
			{
			if (disposing && !_captured)
				{
				_captured = true;
				_onDispose (ToArray ());
				}

			base.Dispose (disposing);
			}
		}
	}
