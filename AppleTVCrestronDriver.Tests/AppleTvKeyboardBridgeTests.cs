// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using AppleTvControlLibrary.FakeDevice;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

/// <summary>
/// Exercises <see cref="AppleTvKeyboardBridge"/>'s bridging of the Apple TV's on-screen keyboard
/// (RTI text input) focus state and text, driven against <see cref="FakeCompanionTcpHost"/>'s real
/// socket-backed fake device, mirroring how AppleTv.Remote.Wpf's MainViewModel reactively
/// shows/hides its TextInputDialog on TextFocusStateChanged.
/// </summary>
/// <remarks>
/// This targets <see cref="AppleTvKeyboardBridge"/> and <see cref="AppleTvCompanionSession"/>
/// directly rather than <see cref="AppleTvVideoServerProtocol"/>, so these tests do not require
/// constructing the real Crestron AVideoServerProtocol base-driver chain (which pulls in the
/// Crestron.SimplSharp.SDK.Library runtime and its ManagedUtilitiesCE dependency).
///
/// This test project references both AppleTVCrestronDriver.csproj (which IL-merges
/// AppleTVControlLibrary into its own output assembly) and AppleTV.Companion.FakeDevice.csproj
/// (which references the original AppleTvControlLibrary assembly directly), so the
/// AppleTvControlLibrary.Protocol.KeyboardFocusState enum exists twice at compile time and any
/// direct reference to it here is ambiguous (CS0433). <see cref="SetRtiFocusState"/> sidesteps
/// this by setting <c>FakeCompanionOpackDevice.RtiFocusState</c> via reflection instead of
/// naming the enum type directly.
/// </remarks>
[TestClass]
public sealed class AppleTvKeyboardBridgeTests
	{
	private static void SetRtiFocusState (FakeCompanionOpackDevice device, bool focused)
		{
		MethodInfo method = typeof (FakeCompanionOpackDevice).GetMethod ("SetRtiFocusState");
		Type enumType = method.GetParameters ()[0].ParameterType;
		object value = Enum.Parse (enumType, focused ? "Focused" : "Unfocused");
		method.Invoke (device, [value]);
		}

	[TestMethod]
	public async Task KeyboardFocusGained_RaisesKeyboardFocusAndTextBridgeEvents ()
		{
		using FakeCompanionTcpHost host = new (pin: FakeCompanionDevice.PIN_CODE);
		SetRtiFocusState (host.OpackDevice, focused: false);
		host.AcceptOne ();

		AppleTvCompanionSession session = await ConnectSessionAsync (host).ConfigureAwait (false);
		try
			{
			AppleTvKeyboardBridge bridge = new (() => session, log: null);
			session.Api.TextFocusStateChanged += (sender, e) => _ = bridge.HandleTextFocusStateChangedAsync (session);

			var focusEvent = new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);
			var textEvent = new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);
			bridge.BridgeEventRaised += line =>
				{
				if (line.StartsWith (AppleTvBridgeProtocol.EVENT_KEYBOARD_FOCUS_PREFIX, StringComparison.Ordinal))
					{
					focusEvent.TrySetResult (line);
					}
				else if (line.StartsWith (AppleTvBridgeProtocol.EVENT_TEXT_PREFIX, StringComparison.Ordinal))
					{
					textEvent.TrySetResult (line);
					}
				};

			host.OpackDevice.RtiText = "Hello";
			SetRtiFocusState (host.OpackDevice, focused: true);

			string focusLine = await WaitAsync (focusEvent.Task).ConfigureAwait (false);
			Assert.AreEqual (AppleTvBridgeProtocol.EVENT_KEYBOARD_FOCUS_PREFIX + "1", focusLine);

			string textLine = await WaitAsync (textEvent.Task).ConfigureAwait (false);
			string encoded = textLine[AppleTvBridgeProtocol.EVENT_TEXT_PREFIX.Length..];
			Assert.AreEqual ("Hello", AppleTvBridgeProtocol.DecodeText (encoded));
			}
		finally
			{
			session.Dispose ();
			}
		}

	[TestMethod]
	public async Task KeyboardFocusLost_RaisesKeyboardFocusZeroEvent ()
		{
		using FakeCompanionTcpHost host = new (pin: FakeCompanionDevice.PIN_CODE);
		SetRtiFocusState (host.OpackDevice, focused: true);
		host.AcceptOne ();

		AppleTvCompanionSession session = await ConnectSessionAsync (host).ConfigureAwait (false);
		try
			{
			AppleTvKeyboardBridge bridge = new (() => session, log: null);
			session.Api.TextFocusStateChanged += (sender, e) => _ = bridge.HandleTextFocusStateChangedAsync (session);

			var focusEvent = new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);
			bridge.BridgeEventRaised += line =>
				{
				if (line.StartsWith (AppleTvBridgeProtocol.EVENT_KEYBOARD_FOCUS_PREFIX, StringComparison.Ordinal))
					{
					focusEvent.TrySetResult (line);
					}
				};

			SetRtiFocusState (host.OpackDevice, focused: false);

			string focusLine = await WaitAsync (focusEvent.Task).ConfigureAwait (false);
			Assert.AreEqual (AppleTvBridgeProtocol.EVENT_KEYBOARD_FOCUS_PREFIX + "0", focusLine);
			}
		finally
			{
			session.Dispose ();
			}
		}

	[TestMethod]
	public async Task SetTextAsync_UpdatesDeviceRtiText ()
		{
		using FakeCompanionTcpHost host = new (pin: FakeCompanionDevice.PIN_CODE);
		SetRtiFocusState (host.OpackDevice, focused: true);
		host.AcceptOne ();

		AppleTvCompanionSession session = await ConnectSessionAsync (host).ConfigureAwait (false);
		try
			{
			AppleTvKeyboardBridge bridge = new (() => session, log: null);

			await bridge.SetTextAsync ("New Text").ConfigureAwait (false);

			DateTime deadline = DateTime.UtcNow.AddSeconds (5);
			while (!string.Equals (host.OpackDevice.RtiText, "New Text", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
				{
				await Task.Delay (25).ConfigureAwait (false);
				}

			Assert.AreEqual ("New Text", host.OpackDevice.RtiText);
			}
		finally
			{
			session.Dispose ();
			}
		}

	private static async Task<string> WaitAsync (Task<string> task)
		{
		Task completed = await Task.WhenAny (task, Task.Delay (TimeSpan.FromSeconds (5))).ConfigureAwait (false);
		Assert.AreSame (task, completed, "Expected the bridge event to be raised within the timeout.");
		return task.Result;
		}

	private static async Task<AppleTvCompanionSession> ConnectSessionAsync (FakeCompanionTcpHost host)
		{
		using AppleTvCompanionPairing pairing = await AppleTvCompanionPairing.BeginAsync ("127.0.0.1", host.Port, CancellationToken.None).ConfigureAwait (false);
		AppleTvStoredDevice device = await pairing.CompleteAsync (FakeCompanionDevice.PIN_CODE, "Living Room", "127.0.0.1", host.Port, CancellationToken.None).ConfigureAwait (false);

		host.AcceptOne ();
		return await AppleTvCompanionSession.ConnectAsync (
			"127.0.0.1",
			host.Port,
			device.ToCredentials (),
			device.StableIdentifier,
			"Living Room",
			CancellationToken.None,
			log: null).ConfigureAwait (false);
		}
	}
