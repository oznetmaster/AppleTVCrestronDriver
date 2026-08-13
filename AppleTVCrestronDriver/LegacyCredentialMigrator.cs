// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.IO;

namespace AppleTV.CrestronDriver;

/// <summary>
/// One-time migration for the Crestron Video Server driver upgrading from a previously released
/// version that stored every driver's credential files directly in the shared
/// <c>credentials</c> folder. Any files found there are moved into the new per-driver credential
/// subfolder before any other credential lookup/validation happens, so upgraded installs keep
/// their existing pairing without requiring the user to re-pair. This is intentionally only
/// present in the Crestron Video Server driver: the Entity V2 extension driver will always be
/// paired fresh and must not migrate/consume the Video Server driver's legacy credential files.
/// </summary>
internal static class LegacyCredentialMigrator
	{
	internal static void MigrateIfNeeded (string baseModel)
		{
		string legacyPath = SharedStorage.GetLegacyCredentialDirectory ();
		string path = SharedStorage.GetCredentialDirectory (baseModel);

		if (string.Equals (legacyPath, path, StringComparison.Ordinal) || !Directory.Exists (legacyPath))
			{
			return;
			}

		foreach (string legacyFile in Directory.EnumerateFiles (legacyPath, "*.json"))
			{
			string destination = Path.Combine (path, Path.GetFileName (legacyFile));
			if (!File.Exists (destination))
				{
				File.Move (legacyFile, destination);
				}
			}
		}
	}
