// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Defines the complete tokenized line vocabulary exchanged over the loopback bridge (see
/// the Apple TV Companion Link video server driver's bridge server) between the Crestron driver
/// (which owns the single live Companion Link session) and a local bridge client (the Entity V2
/// extension driver). Shared between both sides so the command/event names and the app-list
/// encoding scheme can never drift out of sync.
/// </summary>
/// <remarks>
/// Line-based protocol, UTF-8, newline (\n) terminated:
/// <list type="bullet">
/// <item><description>Client -&gt; server commands: <c>CMD:HID:&lt;HidCommand&gt;</c>, <c>CMD:MEDIA:&lt;MediaControlCommand&gt;</c>, <c>CMD:ARROW:&lt;ArrowDirections&gt;</c>, <c>CMD:ARROWDOWN:&lt;ArrowDirections&gt;</c>, <c>CMD:ARROWUP</c>, <c>CMD:POWER:On|Off</c>, <c>CMD:LAUNCH:&lt;bundleId&gt;</c>, <c>CMD:REFRESHAPPS</c>, <c>CMD:REFRESHSTATUS</c>, <c>CMD:MUTE:Toggle</c>, <c>CMD:SETTEXT:&lt;base64 text&gt;</c>.</description></item>
/// <item><description>Server -&gt; client events: <c>EVT:CONNECTED</c>, <c>EVT:DISCONNECTED</c>, <c>EVT:POWER:On|Off</c>, <c>EVT:SYSSTATUS:&lt;SystemStatus&gt;</c>, <c>EVT:VOLSUPPORTED:0|1</c>, <c>EVT:MUTE:0|1</c>, <c>EVT:APPS:&lt;encoded app list&gt;</c>, <c>EVT:KBFOCUS:0|1</c>, <c>EVT:TEXT:&lt;base64 text&gt;</c>.</description></item>
/// </list>
/// The app list is encoded as a sequence of <c>bundleId\u001Fname</c> records (using the ASCII
/// Unit Separator, 0x1F, between the bundle id and display name) joined by the ASCII Record
/// Separator (0x1E), so bundle ids/names containing ':' or other ordinary punctuation cannot be
/// confused with the line's own ':'-delimited token framing. An empty app list encodes as an
/// empty string following the "EVT:APPS:" prefix.
/// Free-form keyboard text (<c>CMD:SETTEXT:</c>/<c>EVT:TEXT:</c>) is Base64-encoded, since it may
/// contain ':', newlines, or other characters that would otherwise be confused with the line's
/// own token framing.
/// </remarks>
internal static class AppleTvBridgeProtocol
{
	private const char RECORD_SEPARATOR = '\u001E';
	private const char UNIT_SEPARATOR = '\u001F';

	internal const string CommandHid = "HID";
	internal const string CommandMedia = "MEDIA";
	internal const string CommandArrow = "ARROW";
	internal const string CommandArrowDown = "ARROWDOWN";
	internal const string CommandArrowUp = "ARROWUP";
	internal const string CommandPower = "POWER";
	internal const string CommandLaunch = "LAUNCH";
	internal const string CommandRefreshApps = "REFRESHAPPS";
	internal const string CommandRefreshStatus = "REFRESHSTATUS";
	internal const string CommandMute = "MUTE";
	internal const string CommandSetText = "SETTEXT";

	internal const string EventConnected = "EVT:CONNECTED";
	internal const string EventDisconnected = "EVT:DISCONNECTED";
	internal const string EventPowerPrefix = "EVT:POWER:";
	internal const string EventSystemStatusPrefix = "EVT:SYSSTATUS:";
	internal const string EventVolumeSupportedPrefix = "EVT:VOLSUPPORTED:";
	internal const string EventMutePrefix = "EVT:MUTE:";
	internal const string EventAppsPrefix = "EVT:APPS:";
	internal const string EventKeyboardFocusPrefix = "EVT:KBFOCUS:";
	internal const string EventTextPrefix = "EVT:TEXT:";

	/// <summary>
	/// Encodes an app list (bundle id/display name pairs) as a single token safe to place after
	/// the <c>EVT:APPS:</c> prefix on one bridge protocol line.
	/// </summary>
	internal static string EncodeApps (IEnumerable<(string BundleId, string Name)> apps)
		{
		if (apps is null)
			{
			return string.Empty;
			}

		var builder = new StringBuilder ();
		bool first = true;
		foreach ((string bundleId, string name) in apps)
			{
			if (!first)
				{
				_ = builder.Append (RECORD_SEPARATOR);
				}

			_ = builder.Append (bundleId ?? string.Empty).Append (UNIT_SEPARATOR).Append (name ?? string.Empty);
			first = false;
			}

		return builder.ToString ();
		}

	/// <summary>
	/// Decodes an app-list token (as produced by <see cref="EncodeApps"/>) back into bundle
	/// id/display name pairs. Returns an empty list for a null or empty token.
	/// </summary>
	internal static List<(string BundleId, string Name)> DecodeApps (string encoded)
		{
		var result = new List<(string BundleId, string Name)> ();
		if (string.IsNullOrEmpty (encoded))
			{
			return result;
			}

		string[] records = encoded.Split (RECORD_SEPARATOR);
		foreach (string record in records)
			{
			if (record.Length == 0)
				{
				continue;
				}

			int separatorIndex = record.IndexOf (UNIT_SEPARATOR);
			if (separatorIndex < 0)
				{
				continue;
				}

			string bundleId = record.Substring (0, separatorIndex);
			string name = record.Substring (separatorIndex + 1);
			result.Add ((bundleId, name));
			}

		return result;
		}

	/// <summary>
	/// Encodes free-form keyboard text (which may contain ':', newlines, or other characters that
	/// would otherwise be confused with the line's own token framing) as Base64 so it is safe to
	/// place after the <c>CMD:SETTEXT:</c>/<c>EVT:TEXT:</c> prefix on one bridge protocol line.
	/// </summary>
	internal static string EncodeText (string text)
		{
		if (string.IsNullOrEmpty (text))
			{
			return string.Empty;
			}

		return Convert.ToBase64String (Encoding.UTF8.GetBytes (text));
		}

	/// <summary>
	/// Decodes a keyboard-text token (as produced by <see cref="EncodeText"/>) back into its
	/// original text. Returns an empty string for a null or empty token.
	/// </summary>
	internal static string DecodeText (string encoded)
		{
		if (string.IsNullOrEmpty (encoded))
			{
			return string.Empty;
			}

		try
			{
			return Encoding.UTF8.GetString (Convert.FromBase64String (encoded));
			}
		catch (FormatException)
			{
			return string.Empty;
			}
		}
}
