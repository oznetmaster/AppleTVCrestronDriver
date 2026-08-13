// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Text;

using AppleTV.CrestronDriver;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AppleTVCrestronDriver.Tests;

[TestClass]
public sealed class AppleTvStoredDeviceStorageTests
	{
	[TestMethod]
	public void Save_ThenLoadForName_RoundTripsAllFields ()
		{
		FakeCredentialFileStore store = new ();
		AppleTvStoredDevice device = new ()
			{
			Address = "10.0.0.5",
			Port = 4321,
			Name = "Living Room",
			UniqueId = "unique-id-1",
			StableIdentifier = "abc123",
			Ltpk = [1, 2, 3],
			Ltsk = [4, 5, 6],
			AtvId = [7, 8, 9],
			ClientId = [10, 11, 12],
			};

		AppleTvStoredDevice.Save (device, store);
		AppleTvStoredDevice loaded = AppleTvStoredDevice.LoadForName ("Living Room", store);

		Assert.IsNotNull (loaded);
		Assert.AreEqual (device.Address, loaded.Address);
		Assert.AreEqual (device.Port, loaded.Port);
		Assert.AreEqual (device.Name, loaded.Name);
		Assert.AreEqual (device.UniqueId, loaded.UniqueId);
		Assert.AreEqual (device.StableIdentifier, loaded.StableIdentifier);
		CollectionAssert.AreEqual (device.Ltpk, loaded.Ltpk);
		CollectionAssert.AreEqual (device.Ltsk, loaded.Ltsk);
		CollectionAssert.AreEqual (device.AtvId, loaded.AtvId);
		CollectionAssert.AreEqual (device.ClientId, loaded.ClientId);
		}

	[TestMethod]
	public void LoadForName_IsCaseInsensitive ()
		{
		FakeCredentialFileStore store = new ();
		AppleTvStoredDevice device = new ()
			{
			Name = "Living Room",
			UniqueId = "unique-id-1",
			};

		AppleTvStoredDevice.Save (device, store);
		AppleTvStoredDevice loaded = AppleTvStoredDevice.LoadForName ("LIVING room", store);

		Assert.IsNotNull (loaded);
		Assert.AreEqual ("Living Room", loaded.Name);
		}

	[TestMethod]
	public void LoadForName_NoMatchingEntry_ReturnsNull ()
		{
		FakeCredentialFileStore store = new ();
		AppleTvStoredDevice device = new ()
			{
			Name = "Living Room",
			UniqueId = "unique-id-1",
			};

		AppleTvStoredDevice.Save (device, store);
		AppleTvStoredDevice loaded = AppleTvStoredDevice.LoadForName ("Bedroom", store);

		Assert.IsNull (loaded);
		}

	[TestMethod]
	public void LoadForName_NullOrWhitespaceName_ReturnsNull ()
		{
		FakeCredentialFileStore store = new ();

		Assert.IsNull (AppleTvStoredDevice.LoadForName (null, store));
		Assert.IsNull (AppleTvStoredDevice.LoadForName ("   ", store));
		}

	[TestMethod]
	public void LoadForName_MalformedEntry_IsSkippedAndOtherEntriesStillFound ()
		{
		FakeCredentialFileStore store = new ();
		store.AddRawEntry ("malformed.json", Encoding.UTF8.GetBytes ("not valid json"));
		AppleTvStoredDevice device = new ()
			{
			Name = "Living Room",
			UniqueId = "unique-id-1",
			};
		AppleTvStoredDevice.Save (device, store);

		AppleTvStoredDevice loaded = AppleTvStoredDevice.LoadForName ("Living Room", store);

		Assert.IsNotNull (loaded);
		Assert.AreEqual ("Living Room", loaded.Name);
		}

	[TestMethod]
	public void Save_UniqueIdWithInvalidFileNameCharacters_SanitizesEntryId ()
		{
		FakeCredentialFileStore store = new ();
		AppleTvStoredDevice device = new ()
			{
			Name = "Living Room",
			UniqueId = "abc:def/ghi",
			};

		AppleTvStoredDevice.Save (device, store);

		Assert.IsTrue (store.ContainsEntry ("ABC_DEF_GHI.json"));
		}

	[TestMethod]
	public void Save_NullDevice_ThrowsArgumentNullException ()
		{
		FakeCredentialFileStore store = new ();

		_ = Assert.ThrowsExactly<System.ArgumentNullException> (() => AppleTvStoredDevice.Save (null, store));
		}

	[TestMethod]
	public void Save_MissingUniqueId_ThrowsArgumentException ()
		{
		FakeCredentialFileStore store = new ();
		AppleTvStoredDevice device = new () { Name = "Living Room" };

		_ = Assert.ThrowsExactly<System.ArgumentException> (() => AppleTvStoredDevice.Save (device, store));
		}
	}
