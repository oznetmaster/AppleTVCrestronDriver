// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;

namespace AppleTV.CrestronDriver.Extension;

/// <summary>
/// A single Apple TV app, exposed as an <see cref="ExtensionObject"/> so the Crestron Home
/// <c>listbutton</c> control can render a dynamic, runtime-populated list without any fixed row
/// count (see the <c>AppSelector</c> control in <c>UiDefinition.xml</c>). The inherited
/// <see cref="ExtensionObject.ExtensionObjectId"/> carries the app's bundle ID, which is what
/// <c>SetSelectedApp</c> receives back via the list button's <c>actionparameters</c> binding.
/// </summary>
public sealed class AppleTvAppItem : ExtensionObject
	{
	public AppleTvAppItem (string bundleId, string name)
		: base ()
		{
		ExtensionObjectId = bundleId;
		BundleId = bundleId;
		Name = name;
		}

	[EntityProperty (Id = "bundleId")]
	public string BundleId { get; }

	[EntityProperty (Id = "name")]
	public string Name { get; }
	}
