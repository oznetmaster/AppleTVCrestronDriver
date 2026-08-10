// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;

using AppleTV.CrestronDriver;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

[TestClass]
public sealed class AppleTvStoredDeviceTests
	{
	[TestMethod]
	public void IsPaired_AllCredentialsPresent_ReturnsTrue ()
		{
		AppleTvStoredDevice device = new ()
			{
			Ltpk = [1],
			Ltsk = [1],
			AtvId = [1],
			ClientId = [1],
			};

		Assert.IsTrue (device.IsPaired);
		}

	[TestMethod]
	public void IsPaired_MissingLtpk_ReturnsFalse ()
		{
		AppleTvStoredDevice device = new ()
			{
			Ltpk = [],
			Ltsk = [1],
			AtvId = [1],
			ClientId = [1],
			};

		Assert.IsFalse (device.IsPaired);
		}

	[TestMethod]
	public void IsPaired_MissingLtsk_ReturnsFalse ()
		{
		AppleTvStoredDevice device = new ()
			{
			Ltpk = [1],
			Ltsk = [],
			AtvId = [1],
			ClientId = [1],
			};

		Assert.IsFalse (device.IsPaired);
		}

	[TestMethod]
	public void IsPaired_MissingAtvId_ReturnsFalse ()
		{
		AppleTvStoredDevice device = new ()
			{
			Ltpk = [1],
			Ltsk = [1],
			AtvId = [],
			ClientId = [1],
			};

		Assert.IsFalse (device.IsPaired);
		}

	[TestMethod]
	public void IsPaired_MissingClientId_ReturnsFalse ()
		{
		AppleTvStoredDevice device = new ()
			{
			Ltpk = [1],
			Ltsk = [1],
			AtvId = [1],
			ClientId = [],
			};

		Assert.IsFalse (device.IsPaired);
		}

	[TestMethod]
	public void IsPaired_NoCredentials_ReturnsFalse ()
		{
		AppleTvStoredDevice device = new ();

		Assert.IsFalse (device.IsPaired);
		}

	[TestMethod]
	public void ToCredentials_ReturnsCredentialsFromStoredValues ()
		{
		byte[] ltpk = [1, 2, 3];
		byte[] ltsk = [4, 5, 6];
		byte[] atvId = [7, 8, 9];
		byte[] clientId = [10, 11, 12];

		AppleTvStoredDevice device = new ()
			{
			Ltpk = ltpk,
			Ltsk = ltsk,
			AtvId = atvId,
			ClientId = clientId,
			};

		AppleTvControlLibrary.Auth.HapCredentials credentials = device.ToCredentials ();

		CollectionAssert.AreEqual (ltpk, credentials.Ltpk);
		CollectionAssert.AreEqual (ltsk, credentials.Ltsk);
		CollectionAssert.AreEqual (atvId, credentials.AtvId);
		CollectionAssert.AreEqual (clientId, credentials.ClientId);
		}
	}
