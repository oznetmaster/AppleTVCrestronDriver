// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;

using AppleTvControlLibrary.Protocol;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Relays the Apple TV's on-screen keyboard (RTI text input) focus state and text between a live
/// <see cref="AppleTvCompanionSession"/> and any connected bridge client, mirroring how
/// AppleTv.Remote.Wpf's MainViewModel reactively shows/hides its TextInputDialog on
/// TextFocusStateChanged. Extracted from <see cref="AppleTvVideoServerProtocol"/> so this
/// bridge-relay logic can be exercised in tests without constructing the real Crestron
/// AVideoServerProtocol base-driver chain.
/// </summary>
internal sealed class AppleTvKeyboardBridge
	{
	private readonly Func<AppleTvCompanionSession> _currentSessionProvider;
	private readonly Action<string> _log;

	internal AppleTvKeyboardBridge (Func<AppleTvCompanionSession> currentSessionProvider, Action<string> log)
		{
		_currentSessionProvider = currentSessionProvider;
		_log = log;
		}

	internal event Action<string> BridgeEventRaised;

	// Relays the Apple TV's on-screen keyboard (RTI text input) focus state, and its current
	// text when focused, to any connected bridge client, mirroring how AppleTv.Remote.Wpf's
	// MainViewModel reactively shows/hides its own TextInputDialog on TextFocusStateChanged.
	internal async Task HandleTextFocusStateChangedAsync (AppleTvCompanionSession session)
		{
		if (!ReferenceEquals (session, _currentSessionProvider ()) || session.Api is null)
			{
			return;
			}

		bool focused = session.Api.TextFocusState == KeyboardFocusState.Focused;
		BridgeEventRaised?.Invoke (AppleTvBridgeProtocol.EventKeyboardFocusPrefix + (focused ? "1" : "0"));

		if (!focused)
			{
			return;
			}

		string currentText = null;
		try
			{
			currentText = await session.Api.TextGetAsync ().ConfigureAwait (false);
			}
		catch (Exception exception)
			{
			_log?.Invoke ($"Failed to fetch the Apple TV's current keyboard text for the bridge: {exception.Message}");
			}

		if (ReferenceEquals (session, _currentSessionProvider ()))
			{
			BridgeEventRaised?.Invoke (AppleTvBridgeProtocol.EventTextPrefix + AppleTvBridgeProtocol.EncodeText (currentText ?? string.Empty));
			}
		}

	// Forwards a bridge client's (the extension driver's) keyboard text edit to the live
	// Companion Link session, replacing the device's virtual keyboard text, just as
	// AppleTv.Remote.Wpf's MainViewModel.SetTextAsync does via AppleTvDeviceManager.SetTextAsync.
	internal async Task SetTextAsync (string text)
		{
		AppleTvCompanionSession session = _currentSessionProvider ();
		if (session?.Api is null)
			{
			return;
			}

		try
			{
			await session.Api.TextSetAsync (text).ConfigureAwait (false);
			}
		catch (Exception exception)
			{
			_log?.Invoke ($"Failed to set the Apple TV's keyboard text for the bridge: {exception.Message}");
			}
		}
	}
