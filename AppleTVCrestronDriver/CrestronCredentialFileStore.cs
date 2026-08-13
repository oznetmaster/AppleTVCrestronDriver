// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Default <see cref="ICredentialFileStore"/> implementation backed by the real
/// credential directory on the Crestron control system's filesystem.
/// </summary>
internal sealed class CrestronCredentialFileStore : ICredentialFileStore
	{
	private readonly string _baseModel;

	public CrestronCredentialFileStore (string baseModel) => _baseModel = baseModel;

	public IEnumerable<string> EnumerateEntries ()
		{
		foreach (string path in Directory.EnumerateFiles (SharedStorage.GetCredentialDirectory (_baseModel), "*.json"))
			{
			yield return Path.GetFileName (path);
			}
		}

	public Stream OpenRead (string entryId) => File.OpenRead (Path.Combine (SharedStorage.GetCredentialDirectory (_baseModel), entryId));

	public Stream CreateWrite (string entryId) => File.Create (Path.Combine (SharedStorage.GetCredentialDirectory (_baseModel), entryId));
	}
