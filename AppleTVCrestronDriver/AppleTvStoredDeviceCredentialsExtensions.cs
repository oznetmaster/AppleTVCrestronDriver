// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using AppleTvControlLibrary.Auth;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Converts an <see cref="AppleTvStoredDevice"/>'s raw pairing fields into a Companion Link
/// <see cref="HapCredentials"/> instance for resuming a paired session. Kept out of
/// <see cref="AppleTvStoredDevice"/> itself (and only compiled here in the video server driver
/// project, never linked into the Entity V2 extension driver project) so the extension driver -
/// which only needs <see cref="AppleTvStoredDevice"/> to look up a stored UniqueId and never
/// resumes a Companion Link session of its own - has no compile-time dependency on
/// AppleTvControlLibrary at all.
/// </summary>
internal static class AppleTvStoredDeviceCredentialsExtensions
	{
	internal static HapCredentials ToCredentials (this AppleTvStoredDevice device) => new (device.Ltpk, device.Ltsk, device.AtvId, device.ClientId);
	}
