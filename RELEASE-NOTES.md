# Apple TV drivers 1.4.5

The Video Server uses AppleTVControlLibrary and Discovery 2.2.7 and validates the accessory's SRP proof, the pairing response states and the final Pair Verify acknowledgement. Existing pairings are reused. A working endpoint recovered through mDNS is saved in the driver's settings while preserving the pairing file.

Credential writes commit complete records through a temporary file and preserve unknown data-contract fields. Connection disposal cancels pending initialization, stale sessions cannot publish current feedback, and reconnects retain a single bridge event subscription. Concurrent bridge writes are serialized.

The companion extension rejects superseded bridge callbacks, waits for confirmed Apple TV connection status and refreshes keyboard focus after reconnecting. Unavailable state clears stale keyboard text and volume controls. Routine informational logs are limited to Debug builds, and bridge diagnostics omit keyboard payloads.

Update both drivers together. The V1 Video Server requires a full processor reboot. The Entity V2 companion supports an in-place driver update. Retain the existing devices, configuration and saved pairing.

Workstation validation passed 130 net472 offline tests and 13 real-SDK extension lifecycle tests. All 143 cases also passed on the processor with no failures or skips. The temporary test instance and stored package were removed and its reservation released. Processor pairing-file hashes remained unchanged. Both existing drivers were then updated to 1.4.005.0000 on the lab processor, with one full reboot for V1. Loaded versions, device identities, rooms, online/ready state, app enumeration and extracted payload hashes were verified. Home navigation, confirmed sleep/wake feedback with a five-second settling pause, and an in-place V2 reload passed. All three pairing files remained byte-for-byte unchanged. A wake sent immediately after reported sleep was ignored; the five-second test interval is not a proven minimum delay.

See [test dependencies and instructions](AppleTVCrestronDriver.Tests/README.md).
