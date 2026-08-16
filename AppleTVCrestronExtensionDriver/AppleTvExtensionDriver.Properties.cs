// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;

namespace AppleTV.CrestronDriver.Extension;

/// <content>
/// Entity Model property declarations (main tile/status bindings, and the dynamic, selector-driven
/// app list). See <c>AppleTvExtensionDriver.cs</c> for the driver's core lifecycle/configuration
/// and <c>AppleTvExtensionDriver.Commands.cs</c> for entity commands.
/// </content>
public sealed partial class AppleTvExtensionDriver
	{
	#region Entity properties

	[EntityProperty (Id = "tileDisplay")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string TileDisplay
		{
		get;
		private set => SetAndNotify ("tileDisplay", value, ref field);
		}

	[EntityProperty (Id = "tileIcon")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string TileIcon
		{
		get;
		private set => SetAndNotify ("tileIcon", value, ref field);
		}

	[EntityProperty (Id = "deviceLabel")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string DeviceLabel
		{
		get;
		private set => SetAndNotify ("deviceLabel", value, ref field);
		}

	[EntityProperty (Id = "onlineIndicator:isOnline")]
	[EntityPropertyMetadata (Programmable = true)]
	public bool OnlineIndicatorIsOnline
		{
		get;
		private set => SetAndNotify ("onlineIndicator:isOnline", value, ref field);
		}

	// Signals to Crestron Home that this entity's dynamic UI state (e.g. the app selector's
	// AppListValues/SelectedApp) has been initialized and is safe to render. Mirrors
	// OnlineIndicatorIsOnline: true once the bridge connection is established, false while
	// offline/reconnecting.
	[EntityProperty (Id = "readyIndicator:isReady")]
	[EntityPropertyMetadata (Programmable = true)]
	public bool ReadyIndicatorIsReady
		{
		get;
		private set => SetAndNotify ("readyIndicator:isReady", value, ref field);
		}

	[EntityProperty (Id = "statusSummary")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string StatusSummary
		{
		get;
		private set => SetAndNotify ("statusSummary", value, ref field);
		}

	[EntityProperty (Id = "powerIsOn")]
	[EntityPropertyMetadata (ExtensionUiProperty = true, Programmable = true)]
	public bool PowerIsOn
		{
		get;
		private set => SetAndNotify ("powerIsOn", value, ref field);
		}

	[EntityProperty (Id = "powerStatusLabel")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string PowerStatusLabel
		{
		get;
		private set => SetAndNotify ("powerStatusLabel", value, ref field);
		}

	// Gates the volume/mute controls in the UI: only true once a session is connected and the
	// paired Apple TV has advertised media-control Volume support via its _mcF flags
	// (CompanionApi.IsVolumeControlSupported). Not every Apple TV / app combination supports
	// volume control, so these controls must not be shown unconditionally.
	[EntityProperty (Id = "volumeControlSupported")]
	[EntityPropertyMetadata (ExtensionUiProperty = true, Programmable = true)]
	public bool VolumeControlSupported
		{
		get;
		private set => SetAndNotify ("volumeControlSupported", value, ref field);
		}

	[EntityProperty (Id = "muteIsOn")]
	[EntityPropertyMetadata (ExtensionUiProperty = true, Programmable = true)]
	public bool MuteIsOn
		{
		get;
		private set => SetAndNotify ("muteIsOn", value, ref field);
		}

	#endregion Entity properties

	#region App list selector

	// Dynamic runtime app list: AppList is a homogenous array of AppleTvAppItem (an
	// ExtensionObject), so the Crestron Home UI's listbutton control (source="{appList}") can
	// render the current app list without any fixed row count. Each item's ExtensionObjectId
	// carries the bundle ID; the listbutton's action="command:setSelectedApp"
	// actionparameters="{.extensionObjectId}" binding raises SetSelectedApp with the chosen
	// bundle ID, which is used to launch the app.

	[EntityProperty (Id = "appList")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public AppleTvAppItem[] AppList
		{
		get;
		private set => SetAndNotify ("appList", value, ref field);
		}

	[EntityProperty (Id = "selectedApp", FriendlyName = "Selected App", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true, Programmable = true)]
	public string SelectedApp
		{
		get;
		private set => SetAndNotify ("selectedApp", value, ref field);
		}

	// Displayed as the listbutton's label so it reads as the currently-launched app's name
	// (e.g. "Netflix") rather than a static, translated "Launch App" caption - a proper name
	// isn't something that should ever be run through translation. When there's no selected
	// app, falls back to the "^launchApplication" translation key (a property's value can
	// itself be a translation binding), so that fallback text remains localizable.
	[EntityProperty (Id = "selectedAppName", FriendlyName = "Selected App Name", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true, Programmable = true)]
	public string SelectedAppName
		{
		get;
		private set => SetAndNotify ("selectedAppName", string.IsNullOrEmpty (value) ? "^launchApplication" : value, ref field);
		}

	#endregion App list selector

	#region Keyboard text entry

	// Mirrors AppleTv.Remote.Wpf's reactive TextInputDialog: gates the textentry control
	// group's visibility so it only appears once the Apple TV's on-screen keyboard actually
	// has focus (pushed via the bridge's EVT:KBFOCUS: event), rather than being shown always.
	[EntityProperty (Id = "keyboardFocused")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool KeyboardFocused
		{
		get;
		private set => SetAndNotify ("keyboardFocused", value, ref field);
		}

	// Seeded from the bridge's EVT:TEXT: event (the device's current on-screen keyboard text)
	// so the textentry control starts in sync with what's already on the device. Crestron
	// Home's textentry control invokes this property's setter (via
	// ExtensionSetPropertyValueExecutor) on every keystroke when bound as value="{keyboardText}",
	// so - mirroring AppleTv.Remote.Wpf's TextInputDialog, which forwards each edit immediately
	// via OnTextInputChanged rather than waiting for a submit action - each keystroke is
	// forwarded to the Apple TV as it is typed. See the public setter below for the forwarding
	// logic and the _suppressKeyboardTextForward guard that prevents echoing a device-originated
	// update (from EVT:TEXT:) back to the device.
	[EntityProperty (Id = "keyboardText", FriendlyName = "Keyboard Text", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string KeyboardText
		{
		get;
		set
			{
			SetAndNotify ("keyboardText", value, ref field);

			if (!_suppressKeyboardTextForward)
				{
				_ = SendBridgeCommandAsync ($"CMD:{AppleTvBridgeProtocol.COMMAND_SET_TEXT}:{AppleTvBridgeProtocol.EncodeText (value ?? string.Empty)}", "set keyboard text");
				}
			}
		}

	// Set around updates to KeyboardText that originate from the bridge's EVT:TEXT: event (i.e.
	// the Apple TV's own current text) so that re-applying the device's own text back to it
	// isn't mistaken for a fresh local edit.
	private bool _suppressKeyboardTextForward;

	// Applies a device-originated text update (from the bridge's EVT:TEXT: event) without
	// forwarding it back to the Apple TV as if it were a local edit.
	internal void ApplyKeyboardTextFromDevice (string text)
		{
		_suppressKeyboardTextForward = true;
		try
			{
			KeyboardText = text;
			}
		finally
			{
			_suppressKeyboardTextForward = false;
			}
		}

	#endregion Keyboard text entry
	}
