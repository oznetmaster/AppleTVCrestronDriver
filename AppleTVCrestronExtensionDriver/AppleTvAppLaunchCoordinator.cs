// Copyright (c) 2026 Neil Colvin. MIT with Commons Clause; see LICENSE.
using System;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

namespace AppleTV.CrestronDriver.Extension;

/// <summary>Serializes app launches and waits for confirmed power after a required wake.</summary>
internal sealed class AppleTvAppLaunchCoordinator
	{
	private readonly SemaphoreSlim _gate = new (1, 1);
	private readonly Func<string, bool> _available;
	private readonly Func<bool> _online;
	private readonly Func<bool> _awake;
	private readonly Func<string, int, Task<bool>> _send;
	private readonly Func<int> _generation;
	private readonly Func<CancellationToken, Task<bool>> _waitForAwake;
	private readonly Action<string> _accepted;

	internal AppleTvAppLaunchCoordinator (Func<string, bool> available, Func<bool> online,
		 Func<bool> awake, Func<string, int, Task<bool>> send, Func<CancellationToken, Task<bool>> waitForAwake, Action<string> accepted, Func<int> generation)
		{
		_available = available;
		_online = online;
		_awake = awake;
		_send = send;
		_waitForAwake = waitForAwake;
		_accepted = accepted;
		_generation = generation;
		}

	internal async Task<bool> LaunchAsync (string bundleId, CancellationToken cancellationToken)
		{
		int generation = _generation ();
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (generation != _generation () || !_online () || !_available (bundleId))
				return false;
			if (!_awake ())
				{
				if (!await _send ($"CMD:{AppleTvBridgeProtocol.COMMAND_HID}:Home", generation).ConfigureAwait (false))
					return false;
				if (!await _waitForAwake (cancellationToken).ConfigureAwait (false))
					return false;
				}
			cancellationToken.ThrowIfCancellationRequested ();
			if (generation != _generation () || !_online () || !_available (bundleId) || !_awake ())
				return false;
			bool queued = await _send ($"CMD:{AppleTvBridgeProtocol.COMMAND_LAUNCH}:{bundleId}", generation).ConfigureAwait (false);
			if (generation != _generation ())
				return false;
			if (queued)
				_accepted?.Invoke (bundleId);
			return queued;
			}
		finally { _gate.Release (); }
		}
	}