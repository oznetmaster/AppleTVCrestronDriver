// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;

using AppleTV.CrestronDriver;

namespace AppleTV.CrestronDriver.Extension;

/// <summary>
/// Pure, side-effect-free helper logic factored out of <see cref="AppleTvExtensionDriver"/> so it
/// can be exercised directly by unit tests without constructing the real Crestron
/// <c>ReflectedAttributeDriverEntity</c>/<c>DriverImplementationResources</c> pipeline (which
/// <see cref="AppleTvExtensionDriver"/>'s constructor requires).
/// </summary>
public static class AppleTvExtensionDriverLogic
	{
	/// <summary>
	/// The result of interpreting a single tokenized bridge line (see
	/// <see cref="AppleTvBridgeProtocol"/>) via <see cref="TryParseBridgeLine"/>.
	/// </summary>
	public enum BridgeLineKind
		{
		Unrecognized,
		Connected,
		Disconnected,
		Power,
		SystemStatus,
		VolumeSupported,
		Mute,
		Apps,
		KeyboardFocus,
		Text,
		}

	/// <summary>
	/// The decoded result of a single tokenized bridge line, as produced by
	/// <see cref="TryParseBridgeLine"/>. Only the field(s) relevant to <see cref="Kind"/> are set.
	/// </summary>
	public readonly struct BridgeLineResult
		{
		public BridgeLineResult (BridgeLineKind kind, bool boolValue = default, string stringValue = null, List<(string BundleId, string Name)> apps = null)
			{
			Kind = kind;
			BoolValue = boolValue;
			StringValue = stringValue;
			Apps = apps;
			}

		public BridgeLineKind Kind { get; }

		public bool BoolValue { get; }

		public string StringValue { get; }

		public List<(string BundleId, string Name)> Apps { get; }
		}

	/// <summary>
	/// Parses a single tokenized bridge event line (see <see cref="AppleTvBridgeProtocol"/>),
	/// mirroring the recognition logic in <see cref="AppleTvExtensionDriver.HandleBridgeLine"/>
	/// without any of that method's side effects (property assignment, logging, etc.).
	/// </summary>
	public static BridgeLineResult TryParseBridgeLine (string line)
		{
		if (line is null)
			{
			return new BridgeLineResult (BridgeLineKind.Unrecognized);
			}

		if (string.Equals (line, AppleTvBridgeProtocol.EVENT_CONNECTED, StringComparison.Ordinal))
			{
			return new BridgeLineResult (BridgeLineKind.Connected);
			}

		if (string.Equals (line, AppleTvBridgeProtocol.EVENT_DISCONNECTED, StringComparison.Ordinal))
			{
			return new BridgeLineResult (BridgeLineKind.Disconnected);
			}

		if (line.StartsWith (AppleTvBridgeProtocol.EVENT_POWER_PREFIX, StringComparison.Ordinal))
			{
			bool isOn = line.AsSpan (AppleTvBridgeProtocol.EVENT_POWER_PREFIX.Length).Equals ("On", StringComparison.OrdinalIgnoreCase);
			return new BridgeLineResult (BridgeLineKind.Power, boolValue: isOn);
			}

		if (line.StartsWith (AppleTvBridgeProtocol.EVENT_SYSTEM_STATUS_PREFIX, StringComparison.Ordinal))
			{
			string status = line[AppleTvBridgeProtocol.EVENT_SYSTEM_STATUS_PREFIX.Length..];
			return new BridgeLineResult (BridgeLineKind.SystemStatus, stringValue: status);
			}

		if (line.StartsWith (AppleTvBridgeProtocol.EVENT_VOLUME_SUPPORTED_PREFIX, StringComparison.Ordinal))
			{
			bool supported = line.AsSpan (AppleTvBridgeProtocol.EVENT_VOLUME_SUPPORTED_PREFIX.Length).Equals ("1", StringComparison.Ordinal);
			return new BridgeLineResult (BridgeLineKind.VolumeSupported, boolValue: supported);
			}

		if (line.StartsWith (AppleTvBridgeProtocol.EVENT_MUTE_PREFIX, StringComparison.Ordinal))
			{
			bool muted = line.AsSpan (AppleTvBridgeProtocol.EVENT_MUTE_PREFIX.Length).Equals ("1", StringComparison.Ordinal);
			return new BridgeLineResult (BridgeLineKind.Mute, boolValue: muted);
			}

		if (line.StartsWith (AppleTvBridgeProtocol.EVENT_APPS_PREFIX, StringComparison.Ordinal))
			{
			string encoded = line[AppleTvBridgeProtocol.EVENT_APPS_PREFIX.Length..];
			List<(string BundleId, string Name)> apps = AppleTvBridgeProtocol.DecodeApps (encoded);
			return new BridgeLineResult (BridgeLineKind.Apps, apps: apps);
			}

		if (line.StartsWith (AppleTvBridgeProtocol.EVENT_KEYBOARD_FOCUS_PREFIX, StringComparison.Ordinal))
			{
			bool focused = line.AsSpan (AppleTvBridgeProtocol.EVENT_KEYBOARD_FOCUS_PREFIX.Length).Equals ("1", StringComparison.Ordinal);
			return new BridgeLineResult (BridgeLineKind.KeyboardFocus, boolValue: focused);
			}

		if (line.StartsWith (AppleTvBridgeProtocol.EVENT_TEXT_PREFIX, StringComparison.Ordinal))
			{
			string encoded = line[AppleTvBridgeProtocol.EVENT_TEXT_PREFIX.Length..];
			return new BridgeLineResult (BridgeLineKind.Text, stringValue: AppleTvBridgeProtocol.DecodeText (encoded));
			}

		return new BridgeLineResult (BridgeLineKind.Unrecognized);
		}

	/// <summary>
	/// Sorts an app list the same way <see cref="AppleTvExtensionDriver.ApplyAppList"/> does
	/// (by name, case-insensitively, using the current culture).
	/// </summary>
	public static List<(string BundleId, string Name)> SortApps (IReadOnlyList<(string BundleId, string Name)> apps) =>
		[.. apps.OrderBy (app => app.Name, StringComparer.CurrentCultureIgnoreCase)];

	/// <summary>
	/// Determines the selected-app bundle ID and display name that
	/// <see cref="AppleTvExtensionDriver.ApplyAppList"/> would apply for a given (already sorted)
	/// app list and the currently selected bundle ID, without mutating any state. Returns
	/// <see langword="null"/> selection values when the current selection should be left as-is.
	/// </summary>
	public static (bool ShouldChange, string BundleId, string Name) DetermineSelection (
		IReadOnlyList<(string BundleId, string Name)> apps,
		string currentSelectedBundleId)
		{
		if (apps.Count > 0 && !apps.Any (app => string.Equals (app.BundleId, currentSelectedBundleId, StringComparison.Ordinal)))
			{
			return (true, apps[0].BundleId, apps[0].Name);
			}

		if (apps.Count == 0)
			{
			return (true, string.Empty, string.Empty);
			}

		return (false, null, null);
		}
	}
