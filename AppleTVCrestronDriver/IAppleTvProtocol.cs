// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;

using AppleTvControlLibrary.Auth;

namespace AppleTV.CrestronDriver;

/// <summary>
/// The subset of <see cref="AppleTvVideoServerProtocol"/>'s surface that
/// <see cref="AppleTvVideoServer"/>'s orchestration logic depends on. Extracted so that logic can
/// eventually be unit-tested off-box against a fake protocol instead of the real RAD-derived
/// <c>AVideoServerProtocol</c> subclass, which cannot be constructed off-box. Every member already
/// exists on <see cref="AppleTvVideoServerProtocol"/>, so implementing this interface there is a
/// declaration change only.
/// </summary>
internal interface IAppleTvProtocol
	{
	/// <summary>
	/// The currently configured Apple TV name.
	/// </summary>
	string AppleTvName { get; }

	/// <summary>
	/// The pairing PIN most recently entered by the user, if any.
	/// </summary>
	string PairingPin { get; }

	/// <summary>
	/// Whether the Companion Link session is currently connected.
	/// </summary>
	bool IsConnected { get; }

	/// <summary>
	/// Connects the Companion Link session to the given endpoint using the given credentials.
	/// </summary>
	Task ConnectCompanionAsync (string address, int port, HapCredentials credentials, string stableIdentifier, string appleTvName);

	/// <summary>
	/// Directly sets the reported Companion Link connection state, without an underlying session
	/// change (e.g. to report offline when a stale paired record is discarded).
	/// </summary>
	void SetCompanionConnectionState (bool connected);
	}
