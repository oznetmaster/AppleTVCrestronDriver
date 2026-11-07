// Copyright (c) 2026 Neil Colvin. MIT with Commons Clause; see LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK.EntityModel;

namespace AppleTV.CrestronDriver.Extension;

// Parameterless programmable app commands keep their identity across app-list changes.
public sealed partial class AppleTvExtensionDriver
{
    private readonly AppleTvAppCommandCatalog _appCommandCatalog = new();
    private readonly object _appCommandLock = new();
    private readonly HashSet<string> _registeredAppCommands = new(StringComparer.Ordinal);
    private void RefreshAppLaunchCommands(IReadOnlyList<(string BundleId, string Name)> apps)
    {
        bool changed = false;
        lock (_appCommandLock)
        {
            if (_disposed) return;
            foreach (var app in _appCommandCatalog.Reconcile(apps))
            {
                if (_registeredAppCommands.Contains(app.Id)) RemoveCommand(app.Id);
                var definition = new DriverEntityCommandDefinition(
                    new Dictionary<string, DriverEntityParameterDefinition>(), null, null,
                    new DriverEntityLocalizedString(app.Label, null));
                var metadata = new DriverEntityCommandMetadata(true, false);
                AddCommand(this, app.Id, new DelegateCommandInstance(app.Id, definition, metadata,
                    (commandId, instance, args, types, complete) =>
                    {
                        bool available;
                        lock (_stateLock)
                            available = !_disposed && OnlineIndicatorIsOnline &&
                                _apps.Any(a => string.Equals(a.BundleId, app.BundleId, StringComparison.Ordinal));
                        if (available) _ = ExecuteAppLaunchCommandAsync(app.BundleId, complete);
                        // Completion follows wake confirmation and queuing the launch request.
                        if (!available) complete?.Invoke(DriverEntityCommandResult.Failure);
                    }, Array.Empty<string>()));
                _registeredAppCommands.Add(app.Id);
                changed = true;
            }
        }
        if (changed) RaiseDefinitionChangedEvent();
    }

}
