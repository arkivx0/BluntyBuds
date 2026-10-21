# Changelog

## 1.3.3
- Android: the app no longer draws under the status bar, camera cutout or navigation bar on Android 15 (edge-to-edge), so the header and Close button are reachable. Bar icons stay light on the dark background.

## 1.3.2
- Android: a Close button (top right) and the back button now disconnect the earbuds, stop scanning, cancel auto-reconnect and close the app. The Quick Settings tile still connects on demand.
- Windows: a Quit button in the main window. The "still running in the tray" hint shows once per session instead of only once ever.

## 1.3.1
- Auto-reconnect uses a single in-flight attempt with backoff (15s, 30s, then 60s) and a longer connect timeout.
- Device list updates in place and refreshes at most about twice a second.
- Starting a new scan can no longer be cut short by an earlier scan's timer.
- Bluetooth status results are checked and shown with readable reasons.
- Demo mode (`--demo`) on Windows for screenshots with placeholder data.
- New release signing key. Uninstall any earlier Android build before installing this one.
- Added MIT license, Gradle wrapper and privacy note.

## 1.3.0
- Recognizes about 60 earbud families using protocols A and J. Models other than A1 and NC9 are marked untested.
- Close to tray option on Windows.

## 1.2.0
- Dark theme and compact layout on Windows and Android.
- Custom noise-cancelling depth on supported models.
- Auto-reconnect, tray menu, Ctrl+Alt+N noise-mode hotkey, low-battery alerts, per-device EQ, preset import and export.
- Android Quick Settings tile for noise mode and optional status notification.
