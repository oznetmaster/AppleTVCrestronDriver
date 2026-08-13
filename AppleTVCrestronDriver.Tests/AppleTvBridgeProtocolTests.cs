// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

using AppleTV.CrestronDriver;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

[TestClass]
public sealed class AppleTvBridgeProtocolTests
	{
	[TestMethod]
	public void EncodeApps_NullList_ReturnsEmptyString ()
		{
		string encoded = AppleTvBridgeProtocol.EncodeApps (null);

		Assert.AreEqual (string.Empty, encoded);
		}

	[TestMethod]
	public void EncodeApps_EmptyList_ReturnsEmptyString ()
		{
		string encoded = AppleTvBridgeProtocol.EncodeApps (new List<(string BundleId, string Name)> ());

		Assert.AreEqual (string.Empty, encoded);
		}

	[TestMethod]
	public void EncodeDecodeApps_RoundTripsSingleApp ()
		{
		var apps = new List<(string BundleId, string Name)> { ("com.apple.tv", "Apple TV") };

		string encoded = AppleTvBridgeProtocol.EncodeApps (apps);
		List<(string BundleId, string Name)> decoded = AppleTvBridgeProtocol.DecodeApps (encoded);

		Assert.AreEqual (1, decoded.Count);
		Assert.AreEqual ("com.apple.tv", decoded[0].BundleId);
		Assert.AreEqual ("Apple TV", decoded[0].Name);
		}

	[TestMethod]
	public void EncodeDecodeApps_RoundTripsMultipleApps ()
		{
		var apps = new List<(string BundleId, string Name)>
			{
			("com.apple.tv", "Apple TV"),
			("com.netflix.Netflix", "Netflix"),
			("com.google.ios.youtube", "YouTube"),
			};

		string encoded = AppleTvBridgeProtocol.EncodeApps (apps);
		List<(string BundleId, string Name)> decoded = AppleTvBridgeProtocol.DecodeApps (encoded);

		CollectionAssert.AreEqual (apps, decoded);
		}

	[TestMethod]
	public void EncodeDecodeApps_NameContainingColon_RoundTrips ()
		{
		var apps = new List<(string BundleId, string Name)> { ("com.example.app", "App: The Sequel") };

		string encoded = AppleTvBridgeProtocol.EncodeApps (apps);
		List<(string BundleId, string Name)> decoded = AppleTvBridgeProtocol.DecodeApps (encoded);

		Assert.AreEqual (1, decoded.Count);
		Assert.AreEqual ("App: The Sequel", decoded[0].Name);
		}

	[TestMethod]
	public void DecodeApps_NullOrEmpty_ReturnsEmptyList ()
		{
		Assert.AreEqual (0, AppleTvBridgeProtocol.DecodeApps (null).Count);
		Assert.AreEqual (0, AppleTvBridgeProtocol.DecodeApps (string.Empty).Count);
		}

	[TestMethod]
	public void EncodeText_NullOrEmpty_ReturnsEmptyString ()
		{
		Assert.AreEqual (string.Empty, AppleTvBridgeProtocol.EncodeText (null));
		Assert.AreEqual (string.Empty, AppleTvBridgeProtocol.EncodeText (string.Empty));
		}

	[TestMethod]
	public void DecodeText_NullOrEmpty_ReturnsEmptyString ()
		{
		Assert.AreEqual (string.Empty, AppleTvBridgeProtocol.DecodeText (null));
		Assert.AreEqual (string.Empty, AppleTvBridgeProtocol.DecodeText (string.Empty));
		}

	[TestMethod]
	public void EncodeDecodeText_RoundTripsPlainText ()
		{
		string encoded = AppleTvBridgeProtocol.EncodeText ("hello world");

		Assert.AreEqual ("hello world", AppleTvBridgeProtocol.DecodeText (encoded));
		}

	[TestMethod]
	public void EncodeDecodeText_RoundTripsTextContainingColonAndNewline ()
		{
		string text = "user: line1\nline2:more";

		string encoded = AppleTvBridgeProtocol.EncodeText (text);

		Assert.AreEqual (text, AppleTvBridgeProtocol.DecodeText (encoded));
		}

	[TestMethod]
	public void EncodeDecodeText_RoundTripsUnicodeText ()
		{
		string text = "caf\u00E9 \uD83D\uDE00 \u65E5\u672C\u8A9E";

		string encoded = AppleTvBridgeProtocol.EncodeText (text);

		Assert.AreEqual (text, AppleTvBridgeProtocol.DecodeText (encoded));
		}

	[TestMethod]
	public void DecodeText_InvalidBase64_ReturnsEmptyString ()
		{
		Assert.AreEqual (string.Empty, AppleTvBridgeProtocol.DecodeText ("not valid base64!!!"));
		}
	}
