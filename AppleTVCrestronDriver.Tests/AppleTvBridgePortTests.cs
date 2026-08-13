// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using AppleTV.CrestronDriver;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

[TestClass]
public sealed class AppleTvBridgePortTests
	{
	[TestMethod]
	public void GetPort_SameUniqueId_ReturnsSameValue ()
		{
		int first = AppleTvBridgePort.GetPort ("AABBCCDDEEFF");
		int second = AppleTvBridgePort.GetPort ("AABBCCDDEEFF");

		Assert.AreEqual (first, second);
		}

	[TestMethod]
	public void GetPort_DiffersByCaseOrWhitespaceOnly_ReturnsSameValue ()
		{
		int first = AppleTvBridgePort.GetPort ("AABBCCDDEEFF");
		int second = AppleTvBridgePort.GetPort ("  aabbccddeeff  ");

		Assert.AreEqual (first, second);
		}

	[TestMethod]
	public void GetPort_DifferentUniqueIds_ReturnDifferentValues ()
		{
		int first = AppleTvBridgePort.GetPort ("AABBCCDDEEFF");
		int second = AppleTvBridgePort.GetPort ("001122334455");

		Assert.AreNotEqual (first, second);
		}

	[TestMethod]
	public void GetPort_ReturnsValueWithinExpectedRange ()
		{
		int port = AppleTvBridgePort.GetPort ("AABBCCDDEEFF");

		Assert.IsTrue (port >= 20000 && port < 30000, $"Expected a port between 20000 and 29999, but got {port}.");
		}

	[TestMethod]
	public void GetPort_NullOrWhitespaceUniqueId_Throws ()
		{
		_ = Assert.ThrowsExactly<System.ArgumentException> (() => AppleTvBridgePort.GetPort (" "));
		}

	[TestMethod]
	public void GetPortCandidates_FirstValue_MatchesGetPort ()
		{
		int first = AppleTvBridgePort.GetPort ("AABBCCDDEEFF");
		int firstCandidate = System.Linq.Enumerable.First (AppleTvBridgePort.GetPortCandidates ("AABBCCDDEEFF"));

		Assert.AreEqual (first, firstCandidate);
		}

	[TestMethod]
	public void GetPortCandidates_SameUniqueId_ReturnsSameSequence ()
		{
		var first = new System.Collections.Generic.List<int> (AppleTvBridgePort.GetPortCandidates ("AABBCCDDEEFF"));
		var second = new System.Collections.Generic.List<int> (AppleTvBridgePort.GetPortCandidates ("AABBCCDDEEFF"));

		CollectionAssert.AreEqual (first, second);
		}

	[TestMethod]
	public void GetPortCandidates_ReturnsExpectedCountAllWithinRange ()
		{
		var candidates = new System.Collections.Generic.List<int> (AppleTvBridgePort.GetPortCandidates ("AABBCCDDEEFF"));

		Assert.AreEqual (AppleTvBridgePort.MAX_CANDIDATES, candidates.Count);
		foreach (int candidate in candidates)
			{
			Assert.IsTrue (candidate >= 20000 && candidate < 30000, $"Expected a port between 20000 and 29999, but got {candidate}.");
			}
		}

	[TestMethod]
	public void GetPortCandidates_NullOrWhitespaceUniqueId_Throws ()
		{
		_ = Assert.ThrowsExactly<System.ArgumentException> (() => System.Linq.Enumerable.First (AppleTvBridgePort.GetPortCandidates (" ")));
		}
	}
