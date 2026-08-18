// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

using AppleTV.CrestronDriver;
using AppleTV.CrestronDriver.Extension;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

/// <summary>
/// Exercises <see cref="AppleTvExtensionDriverLogic"/>, the pure/side-effect-free helper logic
/// factored out of <see cref="AppleTvExtensionDriver"/> so the extension driver's bridge-line
/// parsing and app-list selection behavior can be unit tested without constructing the real
/// Crestron <c>ReflectedAttributeDriverEntity</c>/<c>DriverImplementationResources</c> pipeline.
/// </summary>
[TestClass]
public sealed class AppleTvExtensionDriverLogicTests
	{
	[TestMethod]
	public void TryParseBridgeLine_Connected_ReturnsConnectedKind ()
		{
		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (AppleTvBridgeProtocol.EVENT_CONNECTED);

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.Connected, result.Kind);
		}

	[TestMethod]
	public void TryParseBridgeLine_Disconnected_ReturnsDisconnectedKind ()
		{
		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (AppleTvBridgeProtocol.EVENT_DISCONNECTED);

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.Disconnected, result.Kind);
		}

	[TestMethod]
	public void TryParseBridgeLine_PowerOn_ReturnsPowerTrue ()
		{
		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (AppleTvBridgeProtocol.EVENT_POWER_PREFIX + "On");

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.Power, result.Kind);
		Assert.IsTrue (result.BoolValue);
		}

	[TestMethod]
	public void TryParseBridgeLine_PowerOff_ReturnsPowerFalse ()
		{
		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (AppleTvBridgeProtocol.EVENT_POWER_PREFIX + "Off");

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.Power, result.Kind);
		Assert.IsFalse (result.BoolValue);
		}

	[TestMethod]
	public void TryParseBridgeLine_SystemStatus_ReturnsStatusText ()
		{
		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (AppleTvBridgeProtocol.EVENT_SYSTEM_STATUS_PREFIX + "Awake");

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.SystemStatus, result.Kind);
		Assert.AreEqual ("Awake", result.StringValue);
		}

	[TestMethod]
	public void TryParseBridgeLine_VolumeSupported_ParsesFlag ()
		{
		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (AppleTvBridgeProtocol.EVENT_VOLUME_SUPPORTED_PREFIX + "1");

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.VolumeSupported, result.Kind);
		Assert.IsTrue (result.BoolValue);
		}

	[TestMethod]
	public void TryParseBridgeLine_Mute_ParsesFlag ()
		{
		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (AppleTvBridgeProtocol.EVENT_MUTE_PREFIX + "1");

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.Mute, result.Kind);
		Assert.IsTrue (result.BoolValue);
		}

	[TestMethod]
	public void TryParseBridgeLine_Apps_DecodesAppList ()
		{
		var apps = new List<(string BundleId, string Name)> { ("com.apple.tv", "Apple TV"), ("com.netflix.Netflix", "Netflix") };
		string encoded = AppleTvBridgeProtocol.EncodeApps (apps);

		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (AppleTvBridgeProtocol.EVENT_APPS_PREFIX + encoded);

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.Apps, result.Kind);
		Assert.AreEqual (2, result.Apps.Count);
		Assert.AreEqual ("com.apple.tv", result.Apps[0].BundleId);
		Assert.AreEqual ("Apple TV", result.Apps[0].Name);
		}

	[TestMethod]
	public void TryParseBridgeLine_KeyboardFocus_ParsesFlag ()
		{
		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (AppleTvBridgeProtocol.EVENT_KEYBOARD_FOCUS_PREFIX + "1");

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.KeyboardFocus, result.Kind);
		Assert.IsTrue (result.BoolValue);
		}

	[TestMethod]
	public void TryParseBridgeLine_Text_DecodesText ()
		{
		string encoded = AppleTvBridgeProtocol.EncodeText ("Hello");

		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (AppleTvBridgeProtocol.EVENT_TEXT_PREFIX + encoded);

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.Text, result.Kind);
		Assert.AreEqual ("Hello", result.StringValue);
		}

	[TestMethod]
	public void TryParseBridgeLine_UnrecognizedLine_ReturnsUnrecognizedKind ()
		{
		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine ("garbage");

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.Unrecognized, result.Kind);
		}

	[TestMethod]
	public void TryParseBridgeLine_NullLine_ReturnsUnrecognizedKind ()
		{
		AppleTvExtensionDriverLogic.BridgeLineResult result = AppleTvExtensionDriverLogic.TryParseBridgeLine (null);

		Assert.AreEqual (AppleTvExtensionDriverLogic.BridgeLineKind.Unrecognized, result.Kind);
		}

	[TestMethod]
	public void SortApps_OrdersByNameCaseInsensitively ()
		{
		var apps = new List<(string BundleId, string Name)>
			{
			("com.netflix.Netflix", "netflix"),
			("com.apple.tv", "Apple TV"),
			("com.zzz.Zebra", "Zebra"),
			};

		List<(string BundleId, string Name)> sorted = AppleTvExtensionDriverLogic.SortApps (apps);

		Assert.AreEqual ("Apple TV", sorted[0].Name);
		Assert.AreEqual ("netflix", sorted[1].Name);
		Assert.AreEqual ("Zebra", sorted[2].Name);
		}

	[TestMethod]
	public void DetermineSelection_CurrentSelectionStillPresent_DoesNotChange ()
		{
		var apps = new List<(string BundleId, string Name)> { ("com.apple.tv", "Apple TV"), ("com.netflix.Netflix", "Netflix") };

		(bool shouldChange, string bundleId, string name) = AppleTvExtensionDriverLogic.DetermineSelection (apps, "com.netflix.Netflix");

		Assert.IsFalse (shouldChange);
		Assert.IsNull (bundleId);
		Assert.IsNull (name);
		}

	[TestMethod]
	public void DetermineSelection_CurrentSelectionMissing_ClearsSelection ()
		{
		var apps = new List<(string BundleId, string Name)> { ("com.apple.tv", "Apple TV"), ("com.netflix.Netflix", "Netflix") };

		(bool shouldChange, string bundleId, string name) = AppleTvExtensionDriverLogic.DetermineSelection (apps, "com.missing.App");

		Assert.IsTrue (shouldChange);
		Assert.AreEqual (string.Empty, bundleId);
		Assert.AreEqual (string.Empty, name);
		}

	[TestMethod]
	public void DetermineSelection_EmptyAppList_ClearsSelection ()
		{
		List<(string BundleId, string Name)> apps = [];

		(bool shouldChange, string bundleId, string name) = AppleTvExtensionDriverLogic.DetermineSelection (apps, "com.apple.tv");

		Assert.IsTrue (shouldChange);
		Assert.AreEqual (string.Empty, bundleId);
		Assert.AreEqual (string.Empty, name);
		}

	[TestMethod]
	public void DetermineSelection_NoCurrentSelection_DoesNotAutoSelectFirstApp ()
		{
		var apps = new List<(string BundleId, string Name)> { ("com.apple.tv", "Apple TV"), ("com.netflix.Netflix", "Netflix") };

		(bool shouldChange, string bundleId, string name) = AppleTvExtensionDriverLogic.DetermineSelection (apps, string.Empty);

		Assert.IsFalse (shouldChange);
		Assert.IsNull (bundleId);
		Assert.IsNull (name);
		}
	}
