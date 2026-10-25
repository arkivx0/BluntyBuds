# Changelog

## Unreleased

## 1.4.4
- One earbud: battery text, the tray, and alerts show only the bud that is in use. A missing bud is left out. It is not called disconnected and it does not keep the last percent. Low-battery alerts use only the bud you are wearing. A charging bud is marked "charging".
- EQ: Load, and an AutoEQ file import, sends the profile immediately on Windows and Android, so you do not click Apply afterwards.
- Android: the earbud-status notification is one notification. Turning it on no longer also posts the low-battery alert, and a burst of battery updates no longer posts a second card. It appears as soon as notification permission is granted.
- Android: the option is named "Earbud status in the notification shade".
- Names: the noise buttons say "Noise cancelling" and "Ambient". Touch actions say "Switch noise mode" and "Noise off". Ambient is the pass-through control.
- Windows: the preset box is wider so long AutoEQ labels ("AutoEQ · NC9 (may differ by hardware revision)") are no longer cut off; the EQ tool row wraps on narrow windows.
- Docs: screenshots recaptured for 1.4.4 (demo mode). The battery line is one bud, marked charging. The EQ curve is the built-in NC9 profile, which does not boost.

## 1.4.1
- AutoEQ headroom: profiles no longer boost. Every fit (built-in profiles and imported files) is shifted down so the highest band is at most 0 dB, then clamped to ±12 dB and quantized to 0.5 dB. Built-in profiles regenerated (they boosted up to +7.5 dB with no preamp before).
- Import AutoEQ file: the ParametricEQ.txt "Preamp" line is now honored; the 0 dB ceiling is still enforced.
- AutoEQ suggestions: one model name (e.g. NC9) covers several hardware revisions (BES, WuQi, Bluetrum...). A profile is only called "(this model)" and auto-selected when the earbuds' chip family matches the measured unit; otherwise it is listed first as "AutoEQ · NC9 (may differ by hardware revision)" and not selected. Every profile can still be picked manually.
- Windows: the last-used preset is remembered and selected again on start and on connect, as on Android.
- Android: "Import AutoEQ file" can pick any file type (some file managers report .txt as octet-stream); the content is checked instead.
- Windows installer: the uninstaller and upgrades only close BluntyBuds.exe running from the install folder; a portable copy elsewhere is left alone. Uninstall deletes only crash.log from %LOCALAPPDATA%\BluntyBuds (the folder only if empty); settings and presets are still kept unless you choose to delete them.

## 1.4.0
- AutoEQ: built-in correction profiles, pre-fitted to the 10-band EQ from AutoEq (MIT) measurements, for NC2, NC7, NC9, T6, T10 and T12. The connected model's profile is listed first in the presets ("AutoEQ · NC9 (this model)"); every profile can be picked manually and applied like a normal preset.
- Import AutoEQ file: load an AutoEQ ParametricEQ.txt (PK / LSC / HSC filters) or GraphicEQ.txt, fitted offline to the 10 bands (±12 dB, 0.5 dB steps). Save it as a preset if you like.
- About section with AutoEq attribution and license.
- Generator script (tools/autoeq) and parser/fitting unit tests on both platforms.
- Windows installer added (BluntyBuds-1.4.0-setup.exe): per-user install without admin, Start menu and optional desktop shortcut, uninstaller in Apps & features, in-place upgrades. The portable exe is still available.

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
