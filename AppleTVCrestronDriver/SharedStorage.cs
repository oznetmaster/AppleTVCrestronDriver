using CrestronDirectory = Crestron.SimplSharp.CrestronIO.Directory;

namespace AppleTV.CrestronDriver;

internal static class SharedStorage
	{
	private const string DEVELOPER_NAME = "NeilColvin";
	private const string PRODUCT_SUITE_NAME = "AppleTVCompanion";

	internal static string GetCredentialDirectory ()
		{
		string root = CrestronDirectory.GetApplicationRootDirectory ();
		string relativePath = "user/Data/ThirdParty/" + DEVELOPER_NAME + "/" + PRODUCT_SUITE_NAME + "/credentials";
		string path = string.IsNullOrEmpty (root) ? "/" + relativePath : root.TrimEnd ('/') + "/" + relativePath;

		if (!CrestronDirectory.Exists (path))
			{
			_ = CrestronDirectory.CreateDirectory (path);
			}

		return path;
		}
	}