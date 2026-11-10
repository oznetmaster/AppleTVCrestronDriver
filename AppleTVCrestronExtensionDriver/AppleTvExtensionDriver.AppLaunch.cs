// Copyright (c) 2026 Neil Colvin. MIT with Commons Clause; see LICENSE.
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Crestron.DeviceDrivers.EntityModel;

namespace AppleTV.CrestronDriver.Extension;

public sealed partial class AppleTvExtensionDriver
	{
	private readonly CancellationTokenSource _launchCancellation = new ();
	private int _appLaunchGeneration;
	private AppleTvAppLaunchCoordinator _appLauncher;

	private void InitializeAppLauncher () => _appLauncher = new AppleTvAppLaunchCoordinator (
		 IsAppAvailable, () => OnlineIndicatorIsOnline && !_disposed, IsAppleTvAwake,
		 SendAppLaunchCommandAsync, WaitForAppleTvAwakeAsync, PublishAcceptedAppSelection,
		 () => Volatile.Read (ref _appLaunchGeneration));

	private async Task<bool> SendAppLaunchCommandAsync (string line, int generation)
		{
		AppleTvBridgeClient client;
		lock (_stateLock)
			{
			if (_disposed || generation != _appLaunchGeneration || !OnlineIndicatorIsOnline)
				return false;
			client = _bridgeClient;
			if (client is null || !client.IsConnected)
				return false;
			}
		// Use only the captured client: reconnecting must not redirect an old request.
		try
			{
			await client.SendCommandAsync (line).ConfigureAwait (false);
			return true;
			}
		catch (Exception ex) { LogError ("App command bridge write failed: " + ex.Message); return false; }
		}

	private void PublishAcceptedAppSelection (string bundleId)
		{
		string name;
		lock (_stateLock)
			{
			if (_disposed)
				return;
			name = _apps.FirstOrDefault (a => string.Equals (a.BundleId, bundleId, StringComparison.Ordinal)).Name ?? bundleId;
			}
		// Called inside the launch coordinator's gate, preserving selection order.
		SelectedApp = bundleId;
		SelectedAppName = name;
		}

	private bool IsAppAvailable (string bundleId)
		{
		lock (_stateLock)
			return !_disposed && _apps.Any (a => string.Equals (a.BundleId, bundleId, StringComparison.Ordinal));
		}

	private bool IsAppleTvAwake () => OnlineIndicatorIsOnline && ReadyIndicatorIsReady &&
		 PowerIsOn && PowerStatusLabel is "Awake" or "Idle" or "Screensaver";

	private async Task<bool> WaitForAppleTvAwakeAsync (CancellationToken cancellationToken)
		{
		var clock = Stopwatch.StartNew ();
		// This is a confirmation deadline, not a mandatory startup delay.
		while (clock.Elapsed < TimeSpan.FromSeconds (60))
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (!OnlineIndicatorIsOnline || _disposed)
				return false;
			if (IsAppleTvAwake ())
				return true;
			await Task.Delay (100, cancellationToken).ConfigureAwait (false);
			}
		return false;
		}

	private async Task<bool> LaunchSelectedAppAsync (string bundleId)
		{
		if (string.IsNullOrWhiteSpace (bundleId))
			return false;
		CancellationToken token;
		lock (_stateLock)
			{
			if (_disposed)
				return false;
			token = _launchCancellation.Token;
			}
		try
			{
			if (!await _appLauncher.LaunchAsync (bundleId, token).ConfigureAwait (false))
				{
				LogWarning ("App launch could not be queued after checking availability, connection and power.");
				return false;
				}
			return true;
			}
		catch (OperationCanceledException) { return false; }
		catch (Exception ex) { LogError ("App launch failed: " + ex.Message); return false; }
		}

	private async Task ExecuteAppLaunchCommandAsync (string bundleId, Action<DriverEntityCommandResult> complete)
		{
		bool accepted = await LaunchSelectedAppAsync (bundleId).ConfigureAwait (false);
		try
			{
			complete?.Invoke (accepted ? DriverEntityCommandResult.Completed : DriverEntityCommandResult.Failure);
			}
		catch (Exception ex) { LogWarning ("App launch completion callback failed: " + ex.Message); }
		}
	}