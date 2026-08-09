// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using AppleTvControlLibrary.Auth;
using AppleTvControlLibrary.Protocol;

using Crestron.RAD.Common.Enums;
using Crestron.RAD.Common.Transports;
using Crestron.RAD.DeviceTypes.VideoServer;
using Crestron.SimplSharp;

namespace AppleTV.CrestronDriver;

internal sealed class AppleTvVideoServerProtocol : AVideoServerProtocol
	{
	// Confirmed against the Ultamation reference driver's IL
	// (CLinkClient.Navigate(HidCommand, HidAction), HidAction.Press/Release):
	// press-and-hold uses true HID key-down/key-up semantics, not a
	// driver-side repeat timer. The Companion/tvOS side is responsible for
	// repeating while the key is held down; PressArrowKey sends a single
	// "down" and ReleaseArrowKey sends a single "up".
	// Confirmed against the Ultamation reference driver's IL: it tracks pressed
	// state per-direction (a Dictionary<ArrowDirections, bool>), not a single
	// "last pressed" slot, and never force-releases a direction early. Each
	// direction's down/up state is independent, so an overlapping Press for a
	// new direction cannot clobber the release bookkeeping for a previous one.
	private AppleTvCompanionSession _session;
	private readonly Dictionary<ArrowDirections, bool> _pressedArrowDirections = new Dictionary<ArrowDirections, bool>
		{
		{ ArrowDirections.Up, false },
		{ ArrowDirections.Down, false },
		{ ArrowDirections.Left, false },
		{ ArrowDirections.Right, false },
		};
	private readonly object _pressedArrowLock = new object ();
	private readonly SemaphoreSlim _sendGate = new SemaphoreSlim (1, 1);

	// Crestron Home re-applies the entire current configuration form (every
	// attribute's last known value, not just the one the user changed) whenever
	// it re-initializes the driver - which happens, for example, right after a
	// pairing attempt fails and the driver restarts, or as a side effect of
	// PairNow/PairingPin being applied themselves. Since PairNow is a boolean
	// toggle rather than a momentary/pulse control, that replay resends
	// PairNow = True even though the user did not press it again. Tracking the
	// last observed value lets PairNow be treated as edge-triggered
	// (false -> true) instead of level-triggered, so a replayed True is ignored.
	// This must live in the static AppleTvPairingSessionState singleton, not on
	// this instance: this protocol object is itself recreated on every reinit,
	// so an instance field would reset to false and see the replayed True as a
	// fresh edge every time.
	internal event Action<string> AppleTvNameChanged;

	internal event Action<string> PairingPinChanged;

	internal event Action PairNowRequested;

	internal AppleTvVideoServerProtocol (ISerialTransport transportDriver, byte id)
		 : base (transportDriver, id)
		{
		}

	internal async Task ConnectCompanionAsync (string address, int port, HapCredentials credentials, string stableIdentifier, string appleTvName)
		{
		_session?.Dispose ();
		_session = await AppleTvCompanionSession.ConnectAsync (
			address,
			port,
			credentials,
			stableIdentifier,
			appleTvName,
			CancellationToken.None,
			message => LogDiagnostic (message)).ConfigureAwait (false);
		_session.ConnectionStateChanged += SetCompanionConnectionState;
		SetCompanionConnectionState (true);
		}

	internal string AppleTvName { get; private set; } = string.Empty;

	/// <summary>
	/// Receives boolean user attribute changes made in Crestron Home (e.g. the
	/// "Pair Now" trigger, which is modeled as a Custom/Boolean attribute).
	/// </summary>
	/// <param name="attributeId">The manifest parameter identifier.</param>
	/// <param name="attributeValue">The configured parameter value.</param>
	public override void SetUserAttribute (string attributeId, bool attributeValue)
		{
		LogDiagnostic ($"SetUserAttribute(bool): {attributeId} = {attributeValue}");

		if (string.Equals (attributeId, "PairNow", StringComparison.Ordinal))
			{
			// Edge-triggered: only fire on a false -> true transition so that
			// Crestron Home replaying the last-known form state (e.g. after the
			// driver reinitializes) does not silently restart pairing without
			// the user actually pressing Pair Now again. The last-observed value
			// is kept on the static AppleTvPairingSessionState singleton because
			// this protocol instance itself is recreated on every reinit.
			AppleTvPairingSessionState session = AppleTvPairingSessionState.Instance;
			if (attributeValue && !session.LastPairNowValue)
				{
				PairNowRequested?.Invoke ();
				}

			session.LastPairNowValue = attributeValue;
			return;
			}

		base.SetUserAttribute (attributeId, attributeValue);
		}

	/// <summary>
	/// Receives user attribute changes made in Crestron Home.
	/// </summary>
	/// <param name="attributeId">The manifest parameter identifier.</param>
	/// <param name="attributeValue">The configured parameter value.</param>
	public override void SetUserAttribute (string attributeId, string attributeValue)
		{
		LogDiagnostic ($"SetUserAttribute(string): {attributeId} = {attributeValue}");

		if (string.Equals (attributeId, "AppleTvName", StringComparison.Ordinal))
			{
			AppleTvName = attributeValue?.Trim () ?? string.Empty;
			AppleTvNameChanged?.Invoke (AppleTvName);
			return;
			}

		if (string.Equals (attributeId, "PairingPin", StringComparison.Ordinal))
			{
			PairingPin = attributeValue?.Trim () ?? string.Empty;
			PairingPinChanged?.Invoke (PairingPin);
			return;
			}
		}

	internal string PairingPin { get; private set; } = string.Empty;

	internal void SetCompanionConnectionState (bool connected)
		{
		if (Transport is AppleTvNoOpTransport transport)
			{
			transport.SetConnectionState (connected);
			}

		ConnectionChangedEvent (connected);
		}

	public override void Dispose ()
		{
		// Make sure any held arrow keys are never left "stuck down" on the device
		// if the driver is disposed/reloaded while a key is pressed.
		ReleaseAllPressedArrowKeys ();
		_session?.Dispose ();
		_session = null;
		_sendGate.Dispose ();
		base.Dispose ();
		}

	protected override void ConnectionChangedEvent (bool connection)
		{
		base.ConnectionChangedEvent (connection);
		IsConnected = connection;
		}

	public override void PowerOff () => SendHid (HidCommand.Sleep);

	public override void PowerOn () => SendHid (HidCommand.Wake);

	public override void Play () => SendMedia (MediaControlCommand.Play);

	public override void Pause () => SendMedia (MediaControlCommand.Pause);

	public override void PlayPause () => SendHid (HidCommand.PlayPause);

	public override void ForwardScan () => SendMedia (MediaControlCommand.FastForwardBegin);

	public override void ReverseScan () => SendMedia (MediaControlCommand.RewindBegin);

	public override void ForwardSkip () => SendMedia (MediaControlCommand.NextTrack);

	public override void ReverseSkip () => SendMedia (MediaControlCommand.PreviousTrack);

	// Confirmed via live-tail diagnostics: RAD calls PressArrowKey exactly once
	// when the arrow key goes down and ReleaseArrowKey exactly once when it comes
	// up. A quick tap arrives separately as a Pulse routed to ArrowKey(direction).
	public override void ArrowKey (ArrowDirections direction)
		{
		LogDiagnostic ($"ArrowKey({direction}) called");
		SendArrowHid (MapArrowDirection (direction));
		}

	public override void PressArrowKey (ArrowDirections direction)
		{
		LogDiagnostic ($"PressArrowKey({direction}) called");

		// Matches the Ultamation reference driver: mark this direction pressed
		// without disturbing any other direction's pressed state, then send a
		// single HID down for it.
		lock (_pressedArrowLock)
			{
			_pressedArrowDirections[direction] = true;
			}

		SendArrowDown (MapArrowDirection (direction));
		}

	public override void ReleaseArrowKey ()
		{
		LogDiagnostic ("ReleaseArrowKey() called");
		ReleaseAllPressedArrowKeys ();
		}

	// Ensures down=false is always sent for every arrow direction currently
	// tracked as pressed, no matter what else happens (exceptions sending the
	// down event, repeated release calls, disposal, etc.). Matches the
	// Ultamation reference driver's ReleaseArrowKey, which releases whatever
	// direction(s) are actually flagged pressed rather than a single "last
	// pressed" value. Safe to call even when nothing is pressed.
	private void ReleaseAllPressedArrowKeys ()
		{
		List<ArrowDirections> pressedDirections = null;
		lock (_pressedArrowLock)
			{
			foreach (ArrowDirections direction in new[] { ArrowDirections.Up, ArrowDirections.Down, ArrowDirections.Left, ArrowDirections.Right })
				{
				if (_pressedArrowDirections[direction])
					{
					_pressedArrowDirections[direction] = false;
					(pressedDirections ??= new List<ArrowDirections> ()).Add (direction);
					}
				}
			}

		if (pressedDirections is not null)
			{
			foreach (ArrowDirections direction in pressedDirections)
				{
				SendArrowUp (MapArrowDirection (direction));
				}
			}
		}

	private static HidCommand MapArrowDirection (ArrowDirections direction)
		{
		switch (direction)
			{
			case ArrowDirections.Up:
				return HidCommand.Up;
			case ArrowDirections.Down:
				return HidCommand.Down;
			case ArrowDirections.Left:
				return HidCommand.Left;
			case ArrowDirections.Right:
				return HidCommand.Right;
			default:
				return HidCommand.Up;
			}
		}

	[Conditional ("DEBUG")]
	private void LogDiagnostic (string message)
		{
		// Write straight to the processor console/error log instead of going
		// through the RAD Log() hook, which is routed through Crestron Home
		// and can be filtered/delayed/interleaved with its own logging.
		// EnableLogging is not set until after the driver is constructed and
		// Initialize() runs, so gating on it here would silently drop every
		// diagnostic emitted during construction/load and the initial
		// SetUserAttribute calls that follow.
		string diagnostic = $"[AppleTV] {message}";
		ErrorLog.Notice (diagnostic);
		}

	public override void Select () => SendHid (HidCommand.Select);

	public override void Menu () => SendHid (HidCommand.Menu);

	public override void Home () => SendHid (HidCommand.Home);

	public override void Back () => SendHid (HidCommand.Menu);

	private void SendArrowHid (HidCommand command)
		{
		if (_session is null)
			{
			return;
			}

		// No in-flight gating here: the underlying session/API already sequences
		// frames correctly, so each command is simply queued through _sendGate in
		// arrival order.
		_ = SendAndLogAsync (() => _session.SendHidAsync (command));
		}

	private void SendArrowDown (HidCommand command)
		{
		if (_session is null)
			{
			return;
			}

		_ = SendAndLogAsync (() => _session.SendHidDownAsync (command));
		}

	private void SendArrowUp (HidCommand command)
		{
		// Always attempt the release even if the session has gone away by the
		// time we get here; SendAndLogAsync/session-null are the only things
		// that could stop it from going out, and both are handled defensively.
		if (_session is null)
			{
			return;
			}

		_ = SendAndLogAsync (() => _session.SendHidUpAsync (command));
		}

	private void SendHid (HidCommand command)
		{
		if (_session is not null)
			{
			_ = SendAndLogAsync (() => _session.SendHidAsync (command));
			}
		}

	private void SendMedia (MediaControlCommand command)
		{
		if (_session is not null)
			{
			_ = SendAndLogAsync (() => _session.SendMediaCommandAsync (command));
			}
		}

	private async Task SendAndLogAsync (Func<Task> operation)
		{
		try
			{
			// Serialize all outbound commands so overlapping calls (e.g. a held arrow
			// key firing faster than the round trip to the Apple TV) cannot interleave
			// their down/up HID pairs on the wire.
			await _sendGate.WaitAsync ().ConfigureAwait (false);
			try
				{
				await operation ().ConfigureAwait (false);
				}
			finally
				{
				_sendGate.Release ();
				}
			}
		catch (Exception exception)
			{
			if (EnableLogging)
				{
				Log (exception.Message);
				}
			}
		}
	}