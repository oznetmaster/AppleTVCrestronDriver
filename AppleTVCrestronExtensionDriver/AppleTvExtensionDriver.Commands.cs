// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;

namespace AppleTV.CrestronDriver.Extension;

/// <content>
/// Entity Model commands: full remote control (arrow navigation, Select/Menu/Home, play/pause and
/// transport controls, power, volume/mute) plus app launching. Every command here is translated
/// into a single tokenized <c>CMD:</c> line (see <see cref="AppleTvBridgeProtocol"/>) sent to the
/// Apple TV Companion Link video server driver's loopback bridge - this driver never talks to the
/// Apple TV directly. See <c>AppleTvExtensionDriver.cs</c> for the driver's core lifecycle/bridge
/// connection and <c>AppleTvExtensionDriver.Properties.cs</c> for the Entity Model property
/// declarations these commands' resulting bridge events update.
/// </content>
public sealed partial class AppleTvExtensionDriver
	{
	#region Entity commands

	/// <summary>Handles selection of an app from the dynamic app-list selector, launching it.</summary>
	[EntityCommand (Id = "setSelectedApp", FriendlyName = "Set Selected App")]
	[EntityCommandMetadata (Programmable = true)]
	public void SetSelectedApp (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value)
		{
		string bundleId = value ?? string.Empty;
		if (string.IsNullOrEmpty (bundleId))
			{
			return;
			}

		SelectedApp = bundleId;

		string appName = _apps.FirstOrDefault (app => string.Equals (app.BundleId, bundleId, StringComparison.Ordinal)).Name ?? bundleId;
		SelectedAppName = appName;
		_ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_LAUNCH}:{bundleId}", "launch app " + appName);
		}

	/// <summary>Turns the Apple TV on or off in response to the power toggle.</summary>
	[EntityCommand (Id = "setPowerIsOn")]
	[EntityCommandMetadata (Programmable = true)]
	public void SetPowerIsOn ([EntityParameter] bool value) =>
		_ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_POWER}:{(value ? "On" : "Off")}", "power " + (value ? "on" : "off"));

	// Not marked Programmable: pure navigation whose effect depends entirely on whatever is
	// currently on-screen, so it has no deterministic meaning in an unattended sequence or
	// conditional - these remain usable only via the live UI's dpad/buttongroup bindings.
	/// <summary>Presses and releases the Up arrow.</summary>
	[EntityCommand (Id = "arrowUp")]
	public void ArrowUp () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_ARROW}:Up", "arrow up");

	/// <summary>Presses and releases the Down arrow.</summary>
	[EntityCommand (Id = "arrowDown")]
	public void ArrowDown () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_ARROW}:Down", "arrow down");

	/// <summary>Presses and releases the Left arrow.</summary>
	[EntityCommand (Id = "arrowLeft")]
	public void ArrowLeft () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_ARROW}:Left", "arrow left");

	/// <summary>Presses and releases the Right arrow.</summary>
	[EntityCommand (Id = "arrowRight")]
	public void ArrowRight () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_ARROW}:Right", "arrow right");

	/// <summary>Sends Select.</summary>
	[EntityCommand (Id = "select")]
	public void Select () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_HID}:Select", "select");

	/// <summary>Sends Menu/Back.</summary>
	[EntityCommand (Id = "menu")]
	public void Menu () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_HID}:Menu", "menu");

	/// <summary>Sends Home, and resets the app launcher selector back to its default text.</summary>
	[EntityCommand (Id = "home")]
	[EntityCommandMetadata (Programmable = true)]
	public void Home ()
		{
		SelectedApp = string.Empty;
		SelectedAppName = string.Empty;
		_ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_HID}:Home", "home");
		}

	/// <summary>Sends Play/Pause.</summary>
	[EntityCommand (Id = "playPause")]
	[EntityCommandMetadata (Programmable = true)]
	public void PlayPause () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_HID}:PlayPause", "play/pause");

	// Not marked Programmable: only a *Begin command is sent, with no corresponding stop/end
	// command exposed as an EntityCommand, so a sequence invoking these would have no
	// programmable way to ever stop the scan.
	/// <summary>Starts rewinding (fast reverse) playback.</summary>
	[EntityCommand (Id = "rewind")]
	public void Rewind () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_MEDIA}:RewindBegin", "rewind");

	/// <summary>Starts fast-forwarding playback.</summary>
	[EntityCommand (Id = "fastForward")]
	public void FastForward () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_MEDIA}:FastForwardBegin", "fast forward");

	/// <summary>Skips to the previous track/chapter.</summary>
	[EntityCommand (Id = "reverseSkip")]
	public void ReverseSkip () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_MEDIA}:PreviousTrack", "reverse skip");

	/// <summary>Skips to the next track/chapter.</summary>
	[EntityCommand (Id = "forwardSkip")]
	public void ForwardSkip () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_MEDIA}:NextTrack", "forward skip");

	/// <summary>Raises the volume, when supported by the paired Apple TV.</summary>
	[EntityCommand (Id = "volumeUp")]
	[EntityCommandMetadata (Programmable = true)]
	public void VolumeUp () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_HID}:VolumeUp", "volume up");

	/// <summary>Lowers the volume, when supported by the paired Apple TV.</summary>
	[EntityCommand (Id = "volumeDown")]
	[EntityCommandMetadata (Programmable = true)]
	public void VolumeDown () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_HID}:VolumeDown", "volume down");

	/// <summary>Toggles mute, when supported by the paired Apple TV.</summary>
	[EntityCommand (Id = "toggleMute")]
	[EntityCommandMetadata (Programmable = true)]
	public void ToggleMute () => _ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_MUTE}:Toggle", "toggle mute");

	/// <summary>
	/// Handles Crestron Home's two-way <c>textentry</c> binding (value="{keyboardText}"): invoked
	/// whenever the user types in the on-screen keyboard text field, mirroring how
	/// <see cref="SetPowerIsOn"/> backs the <c>powerIsOn</c> toggle binding. Without this command,
	/// Crestron Home has nothing to invoke for edits and rejects keystrokes in the UI. Not marked
	/// Programmable: only meaningful while the Apple TV's on-screen keyboard already has focus,
	/// which an unattended sequence/conditional has no way to ensure.
	/// </summary>
	[EntityCommand (Id = "setKeyboardText")]
	public void SetKeyboardText (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) =>
		KeyboardText = value ?? string.Empty;

	#endregion Entity commands

	private async Task SendBridgeCommandAsync (string commandLine, string description)
		{
		// OnlineIndicatorIsOnline is the authoritative "is the bridge actually usable right
		// now" flag - set false the instant a disconnect is detected (see
		// HandleBridgeDisconnected) - so check it first rather than relying solely on
		// AppleTvBridgeClient.IsConnected, which reflects only the raw socket state and can
		// still be momentarily true for a client that is about to be torn down.
		if (!OnlineIndicatorIsOnline)
			{
			LogWarning ($"Cannot {description}: the Apple TV bridge is offline.");
			return;
			}

		AppleTvBridgeClient client;
		lock (_stateLock)
			{
			client = _bridgeClient;
			}

		if (client is null || !client.IsConnected)
			{
			LogWarning ($"Cannot {description}: no active Apple TV bridge connection.");
			return;
			}

		try
			{
			await client.SendCommandAsync (commandLine).ConfigureAwait (false);
			}
		catch (Exception exception)
			{
			LogError ($"Failed to {description}: {exception.Message}");
			}
		}
	}
