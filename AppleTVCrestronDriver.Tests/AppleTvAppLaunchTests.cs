// Copyright (c) 2026 Neil Colvin. MIT with Commons Clause; see LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppleTV.CrestronDriver.Extension;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace AppleTVCrestronDriver.Tests;

[TestFixture]
public sealed class AppleTvAppLaunchTests
{
    private const string Home = "CMD:HID:Home", Launch = "CMD:LAUNCH:com.disney";

    private sealed class Harness
    {
        internal bool Awake = true, Online = true, Available = true;
        internal int Generation, WaitCount;
        internal readonly List<string> Sent = new(), Accepted = new();
        internal Func<CancellationToken, Task<bool>> Wait;
        internal Func<string, int, Task<bool>> Send;
        internal readonly AppleTvAppLaunchCoordinator Coordinator;

        internal Harness()
        {
            Wait = _ => { WaitCount++; Awake = true; return Task.FromResult(true); };
            Send = (s, g) => { Sent.Add(s); return Task.FromResult(g == Generation); };
            Coordinator = new AppleTvAppLaunchCoordinator(_ => Available, () => Online, () => Awake,
                (s, g) => Send(s, g), t => Wait(t), id => Accepted.Add(id), () => Generation);
        }
        internal Task<bool> LaunchAsync(CancellationToken token = default) => Coordinator.LaunchAsync("com.disney", token);
    }

    [Test]
    public async Task AwakeLaunch_DoesNotSendHomeOrWait()
    {
        var h = new Harness();
        Assert.IsTrue(await h.LaunchAsync());
        CollectionAssert.AreEqual(new[] { Launch }, h.Sent);
        Assert.AreEqual(0, h.WaitCount);
        CollectionAssert.AreEqual(new[] { "com.disney" }, h.Accepted);
    }

    [Test]
    public async Task AsleepLaunch_WaitsForConfirmationBeforeSendingLaunch()
    {
        var h = new Harness { Awake = false };
        var entered = new TaskCompletionSource<bool>();
        var release = new TaskCompletionSource<bool>();
        h.Wait = async _ => { entered.SetResult(true); return await release.Task; };
        var launch = h.LaunchAsync();
        await entered.Task;
        CollectionAssert.AreEqual(new[] { Home }, h.Sent);
        Assert.IsEmpty(h.Accepted);
        h.Awake = true; release.SetResult(true);
        Assert.IsTrue(await launch);
        CollectionAssert.AreEqual(new[] { Home, Launch }, h.Sent);
    }

    [Test]
    public async Task UnconfirmedWake_FailsWithoutLaunchOrSelection()
    {
        var h = new Harness { Awake = false, Wait = _ => Task.FromResult(false) };
        Assert.IsFalse(await h.LaunchAsync());
        CollectionAssert.AreEqual(new[] { Home }, h.Sent);
        Assert.IsEmpty(h.Accepted);
    }

    [Test]
    public async Task RejectedWake_DoesNotWaitOrLaunch()
    {
        var h = new Harness { Awake = false, Send = (_, _) => Task.FromResult(false) };
        Assert.IsFalse(await h.LaunchAsync());
        Assert.AreEqual(0, h.WaitCount);
        Assert.IsEmpty(h.Accepted);
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task UnavailableLaunch_SendsNothing(bool online, bool available)
    {
        var h = new Harness { Online = online, Available = available };
        Assert.IsFalse(await h.LaunchAsync());
        Assert.IsEmpty(h.Sent);
    }

    [TestCase("disconnect")]
    [TestCase("remove")]
    [TestCase("reconnect")]
    public async Task WakeContextChanges_DoNotLaunchOnAChangedConnection(string change)
    {
        var h = new Harness { Awake = false };
        h.Wait = _ => {
            h.Awake = true;
            if (change == "disconnect") h.Online = false;
            if (change == "remove") h.Available = false;
            if (change == "reconnect") h.Generation++;
            return Task.FromResult(true);
        };
        Assert.IsFalse(await h.LaunchAsync());
        CollectionAssert.AreEqual(new[] { Home }, h.Sent);
        Assert.IsEmpty(h.Accepted);
    }

    [Test]
    public async Task CancelledWake_DoesNotSendLaunch()
    {
        using var cancel = new CancellationTokenSource();
        var h = new Harness { Awake = false };
        h.Wait = _ => { h.Awake = true; cancel.Cancel(); return Task.FromResult(true); };
        try { await h.LaunchAsync(cancel.Token); Assert.Fail("Cancellation was not observed."); }
        catch (OperationCanceledException) { }
        CollectionAssert.AreEqual(new[] { Home }, h.Sent);
    }

    [Test]
    public async Task ConcurrentLaunches_KeepCommandAndSelectionOrder()
    {
        var h = new Harness { Awake = false };
        var entered = new TaskCompletionSource<bool>();
        var release = new TaskCompletionSource<bool>();
        h.Wait = async _ => { entered.SetResult(true); return await release.Task; };
        var first = h.LaunchAsync(); await entered.Task;
        var second = h.Coordinator.LaunchAsync("com.netflix", CancellationToken.None);
        CollectionAssert.AreEqual(new[] { Home }, h.Sent);
        h.Awake = true; release.SetResult(true);
        Assert.IsTrue((await Task.WhenAll(first, second)).All(value => value));
        CollectionAssert.AreEqual(new[] { Home, Launch, "CMD:LAUNCH:com.netflix" }, h.Sent);
        CollectionAssert.AreEqual(new[] { "com.disney", "com.netflix" }, h.Accepted);
    }

    [Test]
    public async Task ReconnectWhileQueued_DoesNotRetargetOldRequests()
    {
        var h = new Harness { Awake = false };
        var entered = new TaskCompletionSource<bool>();
        var release = new TaskCompletionSource<bool>();
        h.Wait = async _ => { entered.SetResult(true); return await release.Task; };
        var first = h.LaunchAsync(); await entered.Task;
        var second = h.Coordinator.LaunchAsync("com.netflix", CancellationToken.None);
        h.Generation++; h.Awake = true; release.SetResult(true);
        Assert.IsFalse((await Task.WhenAll(first, second)).Any(value => value));
        CollectionAssert.AreEqual(new[] { Home }, h.Sent);
        Assert.IsEmpty(h.Accepted);
    }

    [Test]
    public async Task RejectedLaunch_DoesNotPublishSelection()
    {
        var h = new Harness { Send = (_, _) => Task.FromResult(false) };
        Assert.IsFalse(await h.LaunchAsync());
        Assert.IsEmpty(h.Accepted);
    }

    [Test]
    public async Task Exception_ReleasesLaunchGateForNextRequest()
    {
        var h = new Harness { Send = (_, _) => throw new InvalidOperationException("test write failure") };
        try { await h.LaunchAsync(); Assert.Fail("Expected write failure."); }
        catch (InvalidOperationException) { }
        h.Send = (s, _) => { h.Sent.Add(s); return Task.FromResult(true); };
        Assert.IsTrue(await h.LaunchAsync());
        CollectionAssert.AreEqual(new[] { Launch }, h.Sent);
    }
}

[TestFixture]
public sealed class AppleTvAppCommandCatalogTests
{
    [Test]
    public void ReorderAndDuplicates_DoNotChangeDefinitions()
    {
        var c = new AppleTvAppCommandCatalog();
        var initial = c.Reconcile(new[] { ("com.disney", "Disney+"), ("com.netflix", "Netflix") });
        Assert.AreEqual(2, initial.Select(x => x.Id).Distinct().Count());
        Assert.IsEmpty(c.Reconcile(new[] { ("com.netflix", "Netflix"), ("com.disney", "Disney+"), ("com.disney", "Duplicate") }));
    }

    [Test]
    public void Rename_ChangesLabelWithoutChangingIdentity()
    {
        var c = new AppleTvAppCommandCatalog();
        var first = c.Reconcile(new[] { ("com.disney", "Disney+") }).Single();
        var renamed = c.Reconcile(new[] { ("com.disney", "Disney Plus") }).Single();
        Assert.AreEqual(first.Id, renamed.Id);
        Assert.AreEqual("Launch Disney Plus", renamed.Label);
    }

    [Test]
    public void RemoveAndReinstall_PreserveIdentityWithoutRetargeting()
    {
        var c = new AppleTvAppCommandCatalog();
        var first = c.Reconcile(new[] { ("com.disney", "Disney+") }).Single();
        var removed = c.Reconcile(Array.Empty<(string, string)>()).Single();
        Assert.AreEqual(first.Id, removed.Id); Assert.IsFalse(removed.Available);
        Assert.AreEqual("Launch Disney+ (not installed)", removed.Label);
        var replacement = c.Reconcile(new[] { ("com.other", "Disney+") }).Single();
        Assert.AreNotEqual(first.Id, replacement.Id);
        var restored = c.Reconcile(new[] { ("com.other", "Disney+"), ("com.disney", "Disney+") }).Single();
        Assert.AreEqual(first.Id, restored.Id); Assert.IsTrue(restored.Available);
    }

    [Test]
    public void EmptyBundleIsIgnoredAndMissingNameFallsBackToBundle()
    {
        var c = new AppleTvAppCommandCatalog();
        var command = c.Reconcile(new[] { ("", "Blank"), ("com.disney", (string)null) }).Single();
        Assert.AreEqual("Launch com.disney", command.Label);
    }

    [Test]
    public void NewCatalog_RecreatesTheSameStableCommandIdentity()
    {
        var first = new AppleTvAppCommandCatalog().Reconcile(new[] { ("com.disney", "Disney+") }).Single();
        var second = new AppleTvAppCommandCatalog().Reconcile(new[] { ("com.disney", "Renamed") }).Single();
        Assert.AreEqual(first.Id, second.Id);
    }
}
