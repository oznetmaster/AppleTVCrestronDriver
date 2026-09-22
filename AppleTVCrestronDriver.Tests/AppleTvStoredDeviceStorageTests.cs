// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.IO;
using System.Text;

using AppleTV.CrestronDriver;

using NUnit.Framework;
using NUnit.Framework.Legacy;

using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace AppleTVCrestronDriver.Tests;

[TestFixture]
public sealed class AppleTvStoredDeviceStorageTests
	{
	[Test]
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

	[Test]
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

	[Test]
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

	[Test]
	public void LoadForName_NullOrWhitespaceName_ReturnsNull ()
		{
		FakeCredentialFileStore store = new ();

		Assert.IsNull (AppleTvStoredDevice.LoadForName (null, store));
		Assert.IsNull (AppleTvStoredDevice.LoadForName ("   ", store));
		}

	[Test]
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

	[Test]
	public void Save_UniqueIdWithInvalidFileNameCharacters_SanitizesEntryId ()
		{
		// Filename restrictions differ between Windows and the processor's Mono runtime.
		// Use a fresh store for each character so a previous entry cannot hide a failure.
		foreach (char invalidCharacter in Path.GetInvalidFileNameChars ())
			{
			FakeCredentialFileStore store = new ();
			AppleTvStoredDevice device = new ()
				{
				Name = "Living Room",
				UniqueId = "abc" + invalidCharacter + "def/ghi",
				};

			AppleTvStoredDevice.Save (device, store);

			Assert.IsTrue (store.ContainsEntry ("ABC_DEF_GHI.json"),
				$"Invalid filename character U+{(int)invalidCharacter:X4} was not replaced.");
			}
		}

	[Test]
	public void Save_NullDevice_ThrowsArgumentNullException ()
		{
		FakeCredentialFileStore store = new ();

		_ = NUnit.Framework.Assert.Throws<System.ArgumentNullException> (() => AppleTvStoredDevice.Save (null, store));
		}

	[Test]
	public void Save_MissingUniqueId_ThrowsArgumentException ()
		{
		FakeCredentialFileStore store = new ();
		AppleTvStoredDevice device = new ()
			{
			Name = "Living Room"
			};

		_ = NUnit.Framework.Assert.Throws<System.ArgumentException> (() => AppleTvStoredDevice.Save (device, store));
		}
    [Test]
    public void LoadForName_ExistingFilePreservesPairingAndIgnoresUnknownFields ()
        {
        FakeCredentialFileStore store = new ();
        store.AddRawEntry ("existing.json", Encoding.UTF8.GetBytes (
            "{\"Address\":\"192.0.2.10\",\"Port\":4321,\"Name\":\"Télévision\",\"UniqueId\":\"saved-id\",\"StableIdentifier\":\"stable\",\"Ltpk\":\"AQI=\",\"Ltsk\":\"AwQ=\",\"AtvId\":\"BQ==\",\"ClientId\":\"Bg==\",\"FutureField\":true}"));
        AppleTvStoredDevice device = AppleTvStoredDevice.LoadForName (" Télévision ", store);
        Assert.IsNotNull (device);
        Assert.IsTrue (device.IsPaired);
        Assert.AreEqual ("stable", device.StableIdentifier);
        Assert.AreEqual ("saved-id", device.UniqueId);
        Assert.AreEqual (4321, device.Port);
        CollectionAssert.AreEqual (new byte[] { 1, 2 }, device.Ltpk);
        CollectionAssert.AreEqual (new byte[] { 3, 4 }, device.Ltsk);
        CollectionAssert.AreEqual (new byte[] { 5 }, device.AtvId);
        CollectionAssert.AreEqual (new byte[] { 6 }, device.ClientId);
        }

    [Test]
    public void LoadForName_DiscoveryOnlyFileDoesNotInventPairing ()
        {
        FakeCredentialFileStore store = new ();
        store.AddRawEntry ("discovery.json", Encoding.UTF8.GetBytes (
            "{\"Name\":\"Room\",\"UniqueId\":\"saved-id\"}"));
        AppleTvStoredDevice device = AppleTvStoredDevice.LoadForName ("Room", store);
        Assert.IsNotNull (device);
        Assert.IsFalse (device.IsPaired);
        Assert.AreEqual (string.Empty, device.StableIdentifier);
        Assert.AreEqual (0, device.Ltpk.Length);
        }

    [Test]
    public void LoadForName_InvalidBase64DoesNotHideValidFile ()
        {
        FakeCredentialFileStore store = new ();
        store.AddRawEntry ("bad-key.json", Encoding.UTF8.GetBytes (
            "{\"Name\":\"Room\",\"Ltpk\":\"not-base64!\"}"));
        AppleTvStoredDevice.Save (new AppleTvStoredDevice { Name = "Room", UniqueId = "valid" }, store);
        AppleTvStoredDevice device = AppleTvStoredDevice.LoadForName ("Room", store);
        Assert.IsNotNull (device);
        Assert.AreEqual ("valid", device.UniqueId);
        }

	}