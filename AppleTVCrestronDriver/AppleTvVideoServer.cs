using System;
using System.Linq;
using System.Threading.Tasks;

using AppleTvControlLibrary.Discovery.Companion;

using Crestron.SimplSharp;
using Crestron.RAD.Common.Enums;
using Crestron.RAD.Common.Interfaces;
using Crestron.RAD.Common.Transports;
using Crestron.RAD.DeviceTypes.VideoServer;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Provides Crestron Home Video Server control for a paired Apple TV through Companion Link.
/// </summary>
public sealed class AppleTvVideoServer : ABasicVideoServer, ICloudConnected, ISerial
	{
	private const string STORED_DEVICE_SETTING_KEY = "AppleTvStoredDevice";
	private const string PAIRING_NOTICE_ATTRIBUTE_ID = "AppleTvPairingNotice";
	private const string PAIRING_PIN_ATTRIBUTE_ID = "PairingPin";

	private AppleTvNoOpTransport _transport;
	private AppleTvCompanionPairing _pairing;
	private AppleTvStoredDevice _storedDevice;
	private string _pairingAddress = string.Empty;
	private int _pairingPort;
	private string _pairingUniqueId = string.Empty;

	/// <summary>
	/// Initializes the Video Server driver and begins connection or pairing for the configured Apple TV.
	/// </summary>
	public void Initialize ()
		{
		LogDiagnostic ("Initializing Apple TV Companion Video Server.");
		_transport = new AppleTvNoOpTransport
			{
			EnableLogging = InternalEnableLogging,
			CustomLogger = InternalCustomLogger
			};
		ConnectionTransport = _transport;

		var protocol = new AppleTvVideoServerProtocol (ConnectionTransport, Id)
			{
			EnableLogging = InternalEnableLogging,
			CustomLogger = InternalCustomLogger
			};
		protocol.AppleTvNameChanged += appleTvName => _ = HandleAppleTvNameChangedAsync (protocol, appleTvName);
		protocol.PairingPinChanged += pairingPin => _ = HandlePairingPinChangedAsync (protocol, pairingPin);
		protocol.StateChange += StateChange;
		protocol.RxOut += SendRxOut;
		protocol.Initialize (VideoServerData);
		VideoServerProtocol = protocol;

		_ = ConfigureAppleTvAsync (protocol, protocol.AppleTvName);
		}

	/// <summary>
	/// Releases the active Companion Link session and any pending pairing session.
	/// </summary>
	public override void Dispose ()
		{
		_pairing?.Dispose ();
		base.Dispose ();
		}

	private async Task HandleAppleTvNameChangedAsync (AppleTvVideoServerProtocol protocol, string appleTvName)
		{
		LogDiagnostic ($"Apple TV name configured as '{appleTvName}'.");
		ClearPairing ();
		await ConfigureAppleTvAsync (protocol, appleTvName).ConfigureAwait (false);
		}

	private async Task HandlePairingPinChangedAsync (AppleTvVideoServerProtocol protocol, string pairingPin)
		{
		LogDiagnostic ($"Pairing PIN received ({pairingPin.Length} digits)." );
		if (_pairing is null)
			{
			await ConfigureAppleTvAsync (protocol, protocol.AppleTvName).ConfigureAwait (false);
			}

		await CompletePairingAsync (protocol, pairingPin).ConfigureAwait (false);
		}

	private async Task ConfigureAppleTvAsync (AppleTvVideoServerProtocol protocol, string appleTvName)
		{
		try
			{
			AppleTvStoredDevice device = LoadStoredDevice ();
			if (device is null)
				{
				if (string.IsNullOrWhiteSpace (appleTvName))
					{
					LogDiagnostic ("No Apple TV name is configured and device settings do not contain paired credentials.");
					return;
					}

				device = AppleTvStoredDevice.LoadForName (appleTvName);
				if (device is not null)
					{
					SaveStoredDevice (device);
					ClearPairing ();
					SetAppleTvNameStatus ("Existing paired credentials were found for this Apple TV name.");
					LogDiagnostic ($"Initialized device settings from the shared credentials for '{device.Name}'.");
					}
				}

			if (device is not null)
				{
				try
					{
					LogDiagnostic ($"Attempting saved endpoint for '{device.Name}' at {device.Address}:{device.Port}.");
					ClearPairing ();
					await ConnectCompanionAsync (protocol, device, device.Address, device.Port).ConfigureAwait (false);
					LogDiagnostic ($"Connected to '{device.Name}' using its saved endpoint.");
					return;
					}
				catch (Exception exception)
					{
					LogDiagnostic ($"Saved endpoint failed for '{device.Name}'; starting endpoint recovery.");
					LogException (exception);
					}

				appleTvName = device.Name;
				}

			if (string.IsNullOrWhiteSpace (appleTvName))
				{
				LogDiagnostic ("A paired Apple TV endpoint failed, but no Apple TV name is available for discovery recovery.");
				return;
				}

			CompanionDiscoveryResult discovered;
			if (device is null)
				{
				LogDiagnostic ($"Discovering '{appleTvName}' for up to five seconds.");
				discovered = await MulticastCompanionDiscovery.DiscoveryAsync (appleTvName, TimeSpan.FromSeconds (5)).ConfigureAwait (false);
				}
			else
				{
				LogDiagnostic ($"Discovering the paired Apple TV by its saved identity for up to five seconds.");
				discovered = (await new MulticastCompanionDiscovery ().ScanAsync (TimeSpan.FromSeconds (5)).ConfigureAwait (false))
					.FirstOrDefault (result => string.Equals (result.UniqueId, device.UniqueId, StringComparison.Ordinal));
				}

			if (discovered is null || discovered.Address is null)
				{
				LogDiagnostic (device is null
					? $"Discovery did not find '{appleTvName}'."
					: "Discovery did not find the paired Apple TV identity.");
				LogException (new InvalidOperationException ("The required Apple TV was not found by Companion Link discovery."));
				if (device is null)
					{
					SetAppleTvNameStatus ($"No Apple TV named '{appleTvName}' was found. Check the name and ensure the Apple TV is online on the local network.");
					}

					return;
				}

			LogDiagnostic ($"Discovery resolved '{discovered.Name}' at {discovered.Address}:{discovered.Port}.");

			if (device is not null)
				{
				if (string.IsNullOrWhiteSpace (device.UniqueId) || !string.Equals (discovered.UniqueId, device.UniqueId, StringComparison.Ordinal))
					{
					LogDiagnostic ($"Discovered identity for '{discovered.Name}' does not match the stored pairing.");
					LogException (new InvalidOperationException ("The discovered Apple TV does not match the paired device identity."));
					return;
					}

				device.Address = discovered.Address.ToString ();
				device.Port = discovered.Port;
				device.Name = discovered.Name;
				AppleTvStoredDevice.Save (device);
				SaveStoredDevice (device);
				LogDiagnostic ($"Saved endpoint refreshed for '{device.Name}'; reconnecting.");
				await ConnectCompanionAsync (protocol, device, device.Address, device.Port).ConfigureAwait (false);
				LogDiagnostic ($"Connected to '{device.Name}' after endpoint recovery.");
				return;
				}

			if (_pairing is null)
				{
				_pairingAddress = discovered.Address.ToString ();
				_pairingPort = discovered.Port;
				_pairingUniqueId = discovered.UniqueId ?? string.Empty;
				LogDiagnostic ($"Starting pairing for '{discovered.Name}'. Enter the PIN shown on the Apple TV.");
				_pairing = await AppleTvCompanionPairing.BeginAsync (_pairingAddress, _pairingPort, default).ConfigureAwait (false);
				ShowPairingAttributes (discovered.Name);
				SetAppleTvNameStatus ("Apple TV found. Pairing is active; enter the code shown on the Apple TV.");
				LogDiagnostic ($"Pairing setup is active for '{discovered.Name}'.");
				}
			}
		catch (Exception exception)
			{
			LogException (exception);
			if (_pairing is null)
				{
				SetAppleTvNameStatus ($"The Apple TV name could not be validated: {exception.Message}");
				}
			}
		}

	private async Task CompletePairingAsync (AppleTvVideoServerProtocol protocol, string pairingPin)
		{
		if (_pairing is null || pairingPin.Length != 4 || !int.TryParse (pairingPin, out int pin) || pin < 0 || pin > 9999 || string.IsNullOrWhiteSpace (protocol.AppleTvName))
			{
			LogDiagnostic ("Pairing PIN was ignored because pairing is not active, the PIN is invalid, or no Apple TV name is configured.");
			return;
			}

		try
			{
			LogDiagnostic ($"Completing pairing for '{protocol.AppleTvName}'.");
			AppleTvStoredDevice device = await _pairing.CompleteAsync (pin, protocol.AppleTvName, _pairingAddress, _pairingPort, default).ConfigureAwait (false);
			device.UniqueId = _pairingUniqueId;
			AppleTvStoredDevice.Save (device);
			SaveStoredDevice (device);
			LogDiagnostic ($"Credentials were saved for '{device.Name}'.");
			ClearPairing ();
			await ConnectCompanionAsync (protocol, device, device.Address, device.Port).ConfigureAwait (false);
			LogDiagnostic ($"Pairing completed and '{device.Name}' is connected.");
			}
		catch (Exception exception)
			{
			ClearPairing ();
			LogDiagnostic ($"Pairing failed for '{protocol.AppleTvName}'; restarting pairing setup for retry.");
			LogException (exception);
			_ = ConfigureAppleTvAsync (protocol, protocol.AppleTvName);
			}
		}

	private async Task ConnectCompanionAsync (AppleTvVideoServerProtocol protocol, AppleTvStoredDevice device, string address, int port)
		{
		#if DEBUG
		LogDiagnostic ($"Starting Companion TCP connection to {address}:{port}.");
		#endif
		await protocol.ConnectCompanionAsync (
			address,
			port,
			device.ToCredentials (),
			device.StableIdentifier,
			device.Name).ConfigureAwait (false);
		ClearPairing ();
		LogDiagnostic ($"Companion session initialized for '{device.Name}'.");
		}

	private void ClearPairing ()
		{
		if (_pairing is not null)
			{
			LogDiagnostic ("Clearing the active pairing session.");
			}

		_pairing?.Dispose ();
		_pairing = null;
		_pairingAddress = string.Empty;
		_pairingPort = 0;
		_pairingUniqueId = string.Empty;
		HidePairingAttributes ();
		}

	private void ShowPairingAttributes (string appleTvName)
		{
		RemoveUserAttribute (PAIRING_NOTICE_ATTRIBUTE_ID);
		RemoveUserAttribute (PAIRING_PIN_ATTRIBUTE_ID);
		AddUserAttribute (
			UserAttributeType.MessageBox,
			PAIRING_NOTICE_ATTRIBUTE_ID,
			"Pair Apple TV",
			$"A pairing code is now displayed on {appleTvName}. Enter it below to complete pairing.",
			false,
			UserAttributeRequiredForConnectionType.None);
		AddUserAttribute (
			UserAttributeType.OnScreenId,
			PAIRING_PIN_ATTRIBUTE_ID,
			"Pairing PIN",
			"Enter the four-digit pairing code currently displayed on the Apple TV.",
			false,
			UserAttributeRequiredForConnectionType.None,
			UserAttributeDataType.String,
			string.Empty);
		}

	private void HidePairingAttributes ()
		{
		RemoveUserAttribute (PAIRING_NOTICE_ATTRIBUTE_ID);
		RemoveUserAttribute (PAIRING_PIN_ATTRIBUTE_ID);
		}

	private void SetAppleTvNameStatus (string description)
		{
		LogDiagnostic (description);
		}

	private AppleTvStoredDevice LoadStoredDevice ()
		{
		try
			{
			var storedDevice = GetSetting (STORED_DEVICE_SETTING_KEY) as AppleTvStoredDevice;
			if (storedDevice is not null && !string.IsNullOrWhiteSpace (storedDevice.UniqueId) && !string.IsNullOrWhiteSpace (storedDevice.StableIdentifier))
				{
				_storedDevice = storedDevice;
				return _storedDevice;
				}
			}
		catch (Exception exception)
			{
			LogException (exception);
			}

		return null;
		}

	private void SaveStoredDevice (AppleTvStoredDevice device)
		{
		SaveSetting (STORED_DEVICE_SETTING_KEY, device);
		_storedDevice = device;
		}

	private void LogException (Exception exception)
		{
		LogDiagnostic ($"{exception.GetType ().FullName}: {exception.Message}");
		}

	private void LogDiagnostic (string message)
		{
		#if DEBUG
		CrestronConsole.PrintLine ($"[AppleTV] {message}");
		#endif
		if (InternalEnableLogging)
			{
			Log ($"[AppleTV] {message}");
			}
		}
	}