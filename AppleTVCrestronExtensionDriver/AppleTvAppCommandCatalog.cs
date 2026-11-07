// Copyright (c) 2026 Neil Colvin. MIT with Commons Clause; see LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AppleTV.CrestronDriver.Extension;

// Identity is derived from the entire bundle ID, never the position or display name.
// Retired entries remain addressable for this session and cannot launch another app.
internal sealed class AppleTvAppCommandCatalog
{
    private readonly Dictionary<string, AppCommand> _known = new(StringComparer.Ordinal);

    internal IReadOnlyList<AppCommand> Reconcile(IReadOnlyList<(string BundleId, string Name)> apps)
    {
        var current = apps.Where(a => !string.IsNullOrWhiteSpace(a.BundleId))
            .GroupBy(a => a.BundleId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        var changed = new List<AppCommand>();
        foreach (var id in _known.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal).ToArray())
        {
            bool available = current.TryGetValue(id, out var name);
            _known.TryGetValue(id, out var previous);
            name = available ? (string.IsNullOrWhiteSpace(name) ? id : name) : previous.Name;
            if (previous != null && previous.Name == name && previous.Available == available) continue;
            var item = new AppCommand(id, name, available);
            _known[id] = item;
            changed.Add(item);
        }
        return changed;
    }

    internal static string CommandId(string bundleId)
    {
        using var hash = SHA256.Create();
        return "launchApp_" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(bundleId)))
            .Replace("-", string.Empty).ToLowerInvariant();
    }

    internal sealed class AppCommand
    {
        internal AppCommand(string bundleId, string name, bool available)
        { BundleId = bundleId; Name = name; Available = available; }
        internal string BundleId { get; }
        internal string Name { get; }
        internal bool Available { get; }
        internal string Id => CommandId(BundleId);
        internal string Label => "Launch " + Name + (Available ? string.Empty : " (not installed)");
    }
}
