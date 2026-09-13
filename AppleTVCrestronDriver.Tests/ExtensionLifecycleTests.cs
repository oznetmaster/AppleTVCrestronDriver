// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

using NUnit.Framework;

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;

using AppleTV.CrestronDriver.Extension;

namespace AppleTVCrestronDriver.Tests;

[TestFixture, FixtureLifeCycle (LifeCycle.InstancePerTestCase), Category ("Processor")]
public sealed class ExtensionLifecycleTests
	{
	private DriverLogger _logger;
	private AppleTvExtensionDriver _driver;
	[SetUp]
	public void SetUp ()
		{
#if NETFRAMEWORK
		if (Type.GetType ("Mono.Runtime") == null)
			Assert.Ignore ("Requires the SDK desktop harness or the processor runtime.");
#endif
		_logger = new DriverLogger ("extension-lifecycle-test");
		string root = TestContext.Parameters.Get ("TestDataDirectory", TestContext.CurrentContext.TestDirectory);
		string directory = Path.Combine (root, "ExtensionTestData");
		var resources = new DriverImplementationResources
			{
			Logger = _logger,
			InitLogger = _logger.GetComponentLogger ("test", "extension"),
			DriverDefinition = Serialization.DefinitionFromJsonString (File.ReadAllText (Path.Combine (directory, "DriverDefinition.json"))),
			Conditions = new Dictionary<string, ICondition> (),
			Transformations = new Dictionary<string, ITransformation> (),
			TransportConfigItems = new Dictionary<string, IList<ConfigurationItemDefinition>> ()
			};
		_driver = new AppleTvExtensionDriver (new DriverControllerCreationArgs ("extension-lifecycle-test", directory, _logger.AppLogger, null), resources);
		}
	[TearDown]
	public void TearDown ()
		{
		_driver?.Dispose ();
		_logger?.Dispose ();
		}
	private object Call (string method, params object[] args) => typeof (AppleTvExtensionDriver).GetMethod (method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke (_driver, args);
	private void Line (string line) => Call ("HandleBridgeLine", line);
	private T State<T> (string property) => _driver.GetState ().PropertyValues[property].GetValue<T> ();
	[Test]
	public void InitialSnapshot_HasUnavailableStateAndUsableSelectionPlaceholder ()
		{
		Assert.That (State<bool> ("onlineIndicator:isOnline"), Is.False);
		Assert.That (State<bool> ("readyIndicator:isReady"), Is.False);
		Assert.That (State<string> ("selectedAppName"), Is.EqualTo ("^launchApplication"));
		Assert.That (_driver.AppList, Is.Empty);
		}
	[TestCase ("EVT:POWER:On", "powerIsOn", true)]
	[TestCase ("EVT:POWER:Off", "powerIsOn", false)]
	[TestCase ("EVT:VOLSUPPORTED:1", "volumeControlSupported", true)]
	[TestCase ("EVT:MUTE:1", "muteIsOn", true)]
	public void BridgeEvents_UpdatePublishedEntityState (string line, string property, bool expected)
		{
		Line (line);
		Assert.That (State<bool> (property), Is.EqualTo (expected));
		}
	[Test]
	public void ConnectionEvents_KeepReadyOnlineAndTileStatusConsistent ()
		{
		foreach (bool online in new[] { true, false, true })
			{
			Line (online ? "EVT:CONNECTED" : "EVT:DISCONNECTED");
			Assert.That (State<bool> ("onlineIndicator:isOnline"), Is.EqualTo (online));
			Assert.That (State<bool> ("readyIndicator:isReady"), Is.EqualTo (online));
			Assert.That (State<string> ("tileDisplay"), Is.EqualTo (State<string> ("statusSummary")));
			}
		Line ("EVT:SYSSTATUS:Asleep");
		Assert.That (State<string> ("powerStatusLabel"), Is.EqualTo ("Asleep"));
		Assert.That (State<string> ("statusSummary"), Is.EqualTo ("Connected"));
		}
	[Test]
	public void LosingKeyboardFocus_ClearsPreviouslyEnteredText ()
		{
		Line ("EVT:KBFOCUS:1");
		Line ("EVT:TEXT:" + Convert.ToBase64String (Encoding.UTF8.GetBytes ("synthetic search text")));
		Assert.That (State<string> ("keyboardText"), Is.EqualTo ("synthetic search text"));
		Line ("EVT:KBFOCUS:0");
		Assert.That (State<bool> ("keyboardFocused"), Is.False);
		Assert.That (State<string> ("keyboardText"), Is.Empty);
		}
	[TestCase (null)]
	[TestCase ("")]
	[TestCase ("  ")]
	public void MissingDeviceName_IsRejectedBeforeReadingStoredCredentials (string name)
		{
		var errors = (ConfigurationItemErrors)Call ("ApplyConfigurationItems", DataDrivenConfigurationController.ApplyConfigurationAction.ApplyAll, null,
			new Dictionary<string, DriverEntityValue?> { ["AppleTvName"] = name == null ? null : new DriverEntityValue (name) });
		Assert.That (errors.ConfigurationErrorsByItemId.ContainsKey ("AppleTvName"), Is.True);
		Assert.That (State<bool> ("onlineIndicator:isOnline"), Is.False);
		}
	[Test]
	public void ClearConfiguration_RemovesDeviceSpecificUiState ()
		{
		Call ("ApplyAppList", new List<(string BundleId, string Name)> { ("test.app", "Test App") });
		Line ("EVT:KBFOCUS:1");
		Line ("EVT:TEXT:" + Convert.ToBase64String (Encoding.UTF8.GetBytes ("old device text")));
		Line ("EVT:VOLSUPPORTED:1");
		Line ("EVT:SYSSTATUS:Awake");
		Call ("ApplyConfigurationItems", DataDrivenConfigurationController.ApplyConfigurationAction.ClearValues, null, null);
		Assert.That (_driver.AppList, Is.Empty);
		Assert.That (State<string> ("selectedApp"), Is.Empty);
		Assert.That (State<string> ("keyboardText"), Is.Empty);
		Assert.That (State<bool> ("keyboardFocused"), Is.False);
		Assert.That (State<bool> ("volumeControlSupported"), Is.False);
		Assert.That (State<string> ("powerStatusLabel"), Is.EqualTo ("Unknown"));
		Assert.That (State<bool> ("onlineIndicator:isOnline"), Is.False);
		}
	}