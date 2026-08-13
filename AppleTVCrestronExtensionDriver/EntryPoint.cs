// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;

using AppleTV.CrestronDriver.Extension;

[assembly: DriverAssemblyEntryPoint (typeof (EntryPoint))]

/// <summary>
/// Entry point used by the Crestron runtime to create the Apple TV extension driver controller.
/// </summary>
public sealed class EntryPoint : DriverAssemblyEntryPoint
	{
	/// <inheritdoc/>
	public override DriverController CreateDriverControllerInstance (DriverControllerCreationArgs args)
		{
		var resources = DriverImplementationResources.FromCreationArgs (args, typeof (EntryPoint));
		var driver = new AppleTvExtensionDriver (args, resources);
		var rootEntity = new ConfigurableDriverEntity (driver.ControllerId, driver, driver.ConfigurationController);
		return new DispatchingDeviceController (rootEntity, args, null);
		}
	}
