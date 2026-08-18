// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;

using Directory = Crestron.SimplSharp.CrestronIO.Directory;
using File = Crestron.SimplSharp.CrestronIO.File;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Default <see cref="ICredentialFileStore"/> implementation backed by the real
/// credential directory on the Crestron control system's filesystem.
/// </summary>
internal sealed class CrestronCredentialFileStore (string baseModel) : ICredentialFileStore
	{
	public IEnumerable<string> EnumerateEntries ()
		{
		foreach (string path in Directory.GetFiles (SharedStorage.GetCredentialDirectory (baseModel), "*.json"))
			{
			yield return Path.GetFileName (path);
			}
		}

	public Stream OpenRead (string entryId) => new CrestronStreamAdapter (File.OpenRead (Path.Combine (SharedStorage.GetCredentialDirectory (baseModel), entryId)));

	public Stream CreateWrite (string entryId) => new CrestronStreamAdapter (File.Create (Path.Combine (SharedStorage.GetCredentialDirectory (baseModel), entryId)));

	/// <summary>
	/// Adapts a <see cref="Crestron.SimplSharp.CrestronIO.FileStream"/> to the BCL <see cref="Stream"/>
	/// type expected by <see cref="ICredentialFileStore"/>. On the real Crestron SDK, that type does
	/// not derive from <see cref="System.IO.Stream"/>; referencing it through <c>FileStream</c> rather
	/// than a shared base keeps this code compiling identically across alternate implementations of
	/// the SimplSharp APIs that may or may not define a standalone
	/// <c>Crestron.SimplSharp.CrestronIO.Stream</c> type.
	/// </summary>
	private sealed class CrestronStreamAdapter (Crestron.SimplSharp.CrestronIO.FileStream inner) : Stream
		{
		public override bool CanRead => inner.CanRead;

		public override bool CanSeek => inner.CanSeek;

		public override bool CanWrite => inner.CanWrite;

		public override long Length => inner.Length;

		public override long Position
			{
			get => inner.Position;
			set => inner.Position = value;
			}

		public override void Flush () => inner.Flush ();

		public override int Read (byte[] buffer, int offset, int count) => inner.Read (buffer, offset, count);

		public override long Seek (long offset, SeekOrigin origin) => inner.Seek (offset, (Crestron.SimplSharp.CrestronIO.SeekOrigin)(int)origin);

		public override void SetLength (long value) => inner.SetLength (value);

		public override void Write (byte[] buffer, int offset, int count) => inner.Write (buffer, offset, count);

		protected override void Dispose (bool disposing)
			{
			if (disposing)
				{
				inner.Dispose ();
				}

			base.Dispose (disposing);
			}
		}
	}
