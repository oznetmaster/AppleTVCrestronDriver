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

	internal const string COMMAND_HID = "HID";
	internal const string COMMAND_MEDIA = "MEDIA";
	internal const string COMMAND_ARROW = "ARROW";
	internal const string COMMAND_ARROW_DOWN = "ARROWDOWN";
	internal const string COMMAND_ARROW_UP = "ARROWUP";
	internal const string COMMAND_POWER = "POWER";
	internal const string COMMAND_LAUNCH = "LAUNCH";
	internal const string COMMAND_REFRESH_APPS = "REFRESHAPPS";
	internal const string COMMAND_REFRESH_STATUS = "REFRESHSTATUS";
	internal const string COMMAND_MUTE = "MUTE";
	internal const string COMMAND_SET_TEXT = "SETTEXT";

	internal const string EVENT_CONNECTED = "EVT:CONNECTED";
	internal const string EVENT_DISCONNECTED = "EVT:DISCONNECTED";
	internal const string EVENT_POWER_PREFIX = "EVT:POWER:";
	internal const string EVENT_SYSTEM_STATUS_PREFIX = "EVT:SYSSTATUS:";
	internal const string EVENT_VOLUME_SUPPORTED_PREFIX = "EVT:VOLSUPPORTED:";
	internal const string EVENT_MUTE_PREFIX = "EVT:MUTE:";
	internal const string EVENT_APPS_PREFIX = "EVT:APPS:";
	internal const string EVENT_KEYBOARD_FOCUS_PREFIX = "EVT:KBFOCUS:";
	internal const string EVENT_TEXT_PREFIX = "EVT:TEXT:";

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

		// Enumerate records directly over spans of the original string rather than allocating an
		// intermediate string[] via Split (and a substring per record); only the final bundleId/name
		// values need to become actual strings.
		ReadOnlySpan<char> remaining = encoded;
		while (!remaining.IsEmpty)
			{
			int recordSeparatorIndex = remaining.IndexOf (RECORD_SEPARATOR);
			ReadOnlySpan<char> record = recordSeparatorIndex < 0 ? remaining : remaining[..recordSeparatorIndex];
			remaining = recordSeparatorIndex < 0 ? [] : remaining[(recordSeparatorIndex + 1)..];

			if (record.Length == 0)
				{
				continue;
				}

			int separatorIndex = record.IndexOf (UNIT_SEPARATOR);
			if (separatorIndex < 0)
				{
				continue;
				}

			string bundleId = record[..separatorIndex].ToString ();
			string name = record[(separatorIndex + 1)..].ToString ();
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
		return string.IsNullOrEmpty (text) ? string.Empty : Convert.ToBase64String (Encoding.UTF8.GetBytes (text));
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
