# Apple TV drivers 1.5.0

The companion media extension now exposes a separate **Launch <app name>** command for each installed Apple TV app. Configure a Quick Action as **Route AppleTV → Launch app**. The extension wakes Apple TV when needed and waits for confirmed awake state before submitting the launch request. An already-on device launches without an extra Home command or mandatory startup wait.

App command identities remain stable across list reordering and app renaming. Pending launches stop when the connection/configuration changes or the app becomes unavailable. Requests are serialized, and selection feedback is updated after an accepted launch submission; it is not foreground-app or movie-title confirmation.

See the [Quick Action setup instructions](AppleTVCrestronExtensionDriver/README.md#quick-actions) for Crestron Home Setup and Configure Pro. Keep both existing devices: the V1 Video Server still owns pairing, the session and native routing/TSR-310 support. Neither tile visibility nor source selection is changed. No synthetic metadata or artwork probe is included.

Both packages use version 1.5.0 (driver version 1.5.000.0000). V1 runtime behavior is unchanged. Retain the existing paired devices and configuration; updating V1 requires a full processor reboot, while the V2 extension supports an in-place update.


Validation: 149 logic tests and 15 real-SDK lifecycle tests passed on the workstation; all 164 also passed in the packaged processor test host with no failures or skips. The temporary test instance and its package were removed, the workflow reservation was released and all three saved pairing files were unchanged. The two-step TV-off / BBC iPlayer live test passed on the experimental 1.4.005.0002 build; the prepared 1.5.0 production packages have not replaced the installed drivers.
