// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

using AppleTV.CrestronDriver;

using NUnit.Framework;

namespace AppleTVCrestronDriver.Tests;

[TestFixture]
public sealed class CommandSessionIsolationTests
	{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	// The SDK base constructor requires native hardware. This fixture supplies only
	// the private managed queue state; no protocol, transport or pairing is opened.
	[TestCase (false)]
	[TestCase (true)]
	public async Task QueuedCommandCannotCrossReplacementOrTerminalBoundary (bool terminal)
		{
		var protocol = (AppleTvVideoServerProtocol)FormatterServices.GetUninitializedObject (typeof (AppleTvVideoServerProtocol));
		var original = (AppleTvCompanionSession)FormatterServices.GetUninitializedObject (typeof (AppleTvCompanionSession));
		var replacement = (AppleTvCompanionSession)FormatterServices.GetUninitializedObject (typeof (AppleTvCompanionSession));
		using var gate = new SemaphoreSlim (0, 1);
		using var lifetime = new CancellationTokenSource ();
		void Set (string name, object value) => typeof (AppleTvVideoServerProtocol).GetField (name, Private).SetValue (protocol, value);
		Set ("_sessionLock", new object ());
		Set ("_sendGate", gate);
		Set ("_lifetimeCancellation", lifetime);
		Set ("_session", original);
		bool sent = false;
		Task queued = protocol.SendAndLogAsync (_ => { sent = true; return Task.CompletedTask; });
		Assert.That (queued.IsCompleted, Is.False);
		if (terminal)
			Set ("_disposed", true);
		else
			{
			Set ("_session", replacement);
			Set ("_connectionVersion", 1);
			}
		gate.Release ();
		Assert.That (await Task.WhenAny (queued, Task.Delay (5000)), Is.SameAs (queued));
		await queued;
		Assert.That (sent, Is.False);
		if (!terminal)
			{
			await protocol.SendAndLogAsync (session => { Assert.That (session, Is.SameAs (replacement)); sent = true; return Task.CompletedTask; });
			Assert.That (sent, Is.True);
			}
		}
	}