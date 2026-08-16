// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Linq;

using CrestronDirectory = Crestron.SimplSharp.CrestronIO.Directory;

namespace AppleTV.CrestronDriver;

internal static class SharedStorage
	{
	private const string DEVELOPER_NAME = "NeilColvin";
	private const string PRODUCT_SUITE_NAME = "AppleTVCompanion";

	/// <summary>
	/// Builds (and creates if needed) the credential directory for a given driver, scoped to a
	/// subfolder named after that driver's manifest <c>BaseModel</c> (e.g.
	/// <c>Crestron.RAD.Common.Interfaces.IBasicInformation.BaseModel</c> for the Video Server
	/// driver, or <c>Crestron.DeviceDrivers.SDK.GeneralInformationDefinition.BaseModel</c> for
	/// the Entity V2 extension driver). This scopes stored credential files (and thus
	/// pairing/HAP identity) to the specific driver that created them: the Video Server driver
	/// and the Entity V2 extension driver each pair independently and never share a paired
	/// identity or a live Companion Link session with one another. Reusing the same pairing
	/// identity/session across both drivers is what caused tvOS to treat a second driver's
	/// connect as superseding the first's, since Apple TV has no protocol-level way to
	/// distinguish "the same paired remote reconnecting" from "a second live client" - both
	/// present the same stable identifier and pairing. Giving each driver its own separate
	/// pairing (and thus its own credential subfolder) avoids that collision entirely.
	/// </summary>
	/// <param name="baseModel">The driver's manifest <c>BaseModel</c> value.</param>
	internal static string GetCredentialDirectory (string baseModel)
		{
		string path = BuildDriverPath (baseModel);

		if (!CrestronDirectory.Exists (path))
			{
			_ = CrestronDirectory.CreateDirectory (path);
			}

		return path;
		}

	/// <summary>
	/// Returns the legacy shared <c>credentials</c> folder that every driver used to store its
	/// credential files in directly, before per-driver subfolders were introduced. Used only by
	/// the Crestron Video Server driver's one-time upgrade migration; the Entity V2 extension
	/// driver does not perform this migration since it is re-paired independently.
	/// </summary>
	internal static string GetLegacyCredentialDirectory () => BuildLegacyPath ();

	private static string BuildLegacyPath ()
		{
		string root = CrestronDirectory.GetApplicationRootDirectory ();
		string legacyRelativePath = "user/Data/ThirdParty/" + DEVELOPER_NAME + "/" + PRODUCT_SUITE_NAME + "/credentials";

		return string.IsNullOrEmpty (root) ? "/" + legacyRelativePath : root.TrimEnd ('/') + "/" + legacyRelativePath;
		}

	private static string BuildDriverPath (string baseModel)
		{
		string driverSubfolder = ToSafeFolderName (baseModel);
		string legacyPath = BuildLegacyPath ();

		return legacyPath + "/" + driverSubfolder;
		}

	private static string ToSafeFolderName (string baseModel)
		{
		if (string.IsNullOrWhiteSpace (baseModel))
			{
			return "default";
			}

		char[] chars = [.. baseModel.Trim ().Select (c => char.IsLetterOrDigit (c) ? char.ToLowerInvariant (c) : '-')];
		string name = new string (chars);

		while (name.Contains ("--"))
			{
			name = name.Replace ("--", "-");
			}

		name = name.Trim ('-');

		return string.IsNullOrEmpty (name) ? "default" : name;
		}
	}