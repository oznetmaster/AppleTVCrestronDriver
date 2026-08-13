// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace AppleTV.CrestronDriver;

internal sealed class AppleTvStoredDevice
	{
	// The Crestron video server driver's own manifest BaseModel (AppleTVCrestronDriver.json's
	// GeneralInformation.BaseModel). The video server is the sole owner of Companion Link
	// pairing/credentials and therefore the sole owner of each Apple TV's UniqueId - the value
	// the bridge (see AppleTvBridgePort/AppleTvBridgeServer) derives its loopback port from. The
	// extension driver (a distinct manifest BaseModel, "Apple TV Companion Media", with its own
	// isolated credential storage - see SharedStorage/CrestronCredentialFileStore) uses this
	// constant purely to look up the video server's UniqueId for a given Apple TV name so it can
	// compute the same bridge port candidates, never to read or reuse its pairing credentials.
	internal const string VideoServerBaseModel = "Apple TV Companion";

	public string Address { get; set; } = string.Empty;

	public int Port { get; set; }

	public string Name { get; set; } = string.Empty;

	public string UniqueId { get; set; } = string.Empty;

	public string StableIdentifier { get; set; } = string.Empty;

	public byte[] Ltpk { get; set; } = [];

	public byte[] Ltsk { get; set; } = [];

	public byte[] AtvId { get; set; } = [];

	public byte[] ClientId { get; set; } = [];

	/// <summary>
	/// Gets a value indicating whether this record has full Companion Link pairing
	/// credentials, as opposed to being a discovery-only record (name/address/port/unique id
	/// persisted before pairing has ever completed).
	/// </summary>
	public bool IsPaired => Ltpk.Length > 0 && Ltsk.Length > 0 && AtvId.Length > 0 && ClientId.Length > 0;

	internal static AppleTvStoredDevice LoadForName (string name, string baseModel) => LoadForName (name, new CrestronCredentialFileStore (baseModel));

	internal static AppleTvStoredDevice LoadForName (string name, ICredentialFileStore store)
		{
		if (string.IsNullOrWhiteSpace (name))
			{
			return null;
			}

		foreach (string entryId in store.EnumerateEntries ())
			{
			try
				{
				StoredDeviceFile deviceFile;
				using (Stream stream = store.OpenRead (entryId))
					{
					deviceFile = (StoredDeviceFile)new DataContractJsonSerializer (typeof (StoredDeviceFile)).ReadObject (stream);
					}

				AppleTvStoredDevice device = deviceFile?.ToStoredDevice ();
				if (device is not null && string.Equals (device.Name, name.Trim (), StringComparison.OrdinalIgnoreCase))
					{
					return device;
					}
				}
			catch (Exception)
				{
				// A malformed or unrelated file must not prevent lookup of other paired Apple TVs.
				}
			}

		return null;
		}

	internal static void Save (AppleTvStoredDevice device, string baseModel) => Save (device, new CrestronCredentialFileStore (baseModel));

	internal static void Save (AppleTvStoredDevice device, ICredentialFileStore store)
		{
		if (device is null)
			{
			throw new ArgumentNullException (nameof (device));
			}

		if (string.IsNullOrWhiteSpace (device.UniqueId))
			{
			throw new ArgumentException ("An Apple TV stable identifier is required.", nameof (device));
			}

		string entryId = GetPathKey (device.UniqueId) + ".json";
		using Stream stream = store.CreateWrite (entryId);
		new DataContractJsonSerializer (typeof (StoredDeviceFile)).WriteObject (stream, StoredDeviceFile.FromStoredDevice (device));
		}

	[DataContract]
	private sealed class StoredDeviceFile
		{
		[DataMember] public string Address { get; set; }
		[DataMember] public int Port { get; set; }
		[DataMember] public string Name { get; set; }
		[DataMember] public string UniqueId { get; set; }
		[DataMember] public string StableIdentifier { get; set; }
		[DataMember] public string Ltpk { get; set; }
		[DataMember] public string Ltsk { get; set; }
		[DataMember] public string AtvId { get; set; }
		[DataMember] public string ClientId { get; set; }

		internal static StoredDeviceFile FromStoredDevice (AppleTvStoredDevice device) => new ()
			{
			Address = device.Address,
			Port = device.Port,
			Name = device.Name,
			UniqueId = device.UniqueId,
			StableIdentifier = device.StableIdentifier,
			Ltpk = Convert.ToBase64String (device.Ltpk),
			Ltsk = Convert.ToBase64String (device.Ltsk),
			AtvId = Convert.ToBase64String (device.AtvId),
			ClientId = Convert.ToBase64String (device.ClientId)
			};

		internal AppleTvStoredDevice ToStoredDevice () => new ()
			{
			Address = Address ?? string.Empty,
			Port = Port,
			Name = Name ?? string.Empty,
			UniqueId = UniqueId ?? string.Empty,
			StableIdentifier = StableIdentifier ?? string.Empty,
			Ltpk = Convert.FromBase64String (Ltpk ?? string.Empty),
			Ltsk = Convert.FromBase64String (Ltsk ?? string.Empty),
			AtvId = Convert.FromBase64String (AtvId ?? string.Empty),
			ClientId = Convert.FromBase64String (ClientId ?? string.Empty)
			};
		}

	private static string GetPathKey (string value)
		{
		char[] invalidCharacters = Path.GetInvalidFileNameChars ();
		char[] characters = value.Trim ().ToUpperInvariant ().ToCharArray ();
		for (int index = 0; index < characters.Length; index++)
			{
			if (Array.IndexOf (invalidCharacters, characters[index]) >= 0)
				{
				characters[index] = '_';
				}
			}

		return new string (characters);
		}
	}
