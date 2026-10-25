# Changelog

## 1.4.10

- Home stays one dark screen with no scrolling. The EQ is shorter, and L-R Balance and Low-Latency sit on the main screen. Gestures and Ping Buds stay as buttons.
- Sound Profiles (the dropdown and Edit) stay visible. The EQ shrinks so every row fits, with no overlap, at the default window size and at the minimum height.
- The top badge is a privacy control (a shield: Bluetooth only, no network, no analytics), not a connection status. The "Solo" word is gone; the L/R chips already show which earbud is in use, and those chips are at least 40 px / 40 dp.
- A missing earbud reads "In case". With no link, the pills read "Not connected". The case percent is no longer clipped while charging.
- Device names keep more of the top strip. Status text wraps, and the full message stays in a tooltip. An empty scan says to put the earbuds in pairing mode, then Scan.
- Lock EQ shows "Locked, tap to edit" and fades the sliders to 40%. The band labels stay readable. Gain labels are whole numbers, with the exact value in the tooltip.
- Built-in correction presets use generic names ("Tuned preset", "Tuned preset (approximate)", then numbered). They do not include an earbud model.
- Battery pills and EQ sliders have accessibility names.
- Android: the close button minimizes instead of quitting. Labels follow large system font sizes and shrink to fit instead of clipping.
- Windows: the window opens at 600×660. It can narrow to 560 px; the height stays 660. The EQ gives up height first, and the slider row stays at least about 90 px so the sliders do not disappear. Android keeps the same 90 dp floor when the screen is tall enough.

## 1.4.9

- Sound Profiles are a compact dropdown again: pick one and it applies at once, with Undo. Save, rename and delete sit behind the Edit button. Gestures is simpler: one dropdown per gesture and side, only for gestures your earbuds report, sent as soon as you change it. Android connects exactly like 1.4.4 again.

## 1.4.8

- Fix: EQ changes could be silently blocked. A Lock EQ setting left on by 1.4.5-1.4.7 stopped every slider and Sound Profile change. 1.4.8 starts unlocked, and a locked tap now says how to unlock. EQ frames are byte-for-byte the same as 1.4.4.

## 1.4.7

- Fix (Android): the Tune card showed only its header, with no EQ sliders and no Sound Profile chips. A spacer in the header grew to fill the whole card; it is now 1px tall and the band row has a minimum height.

## 1.4.6

- Fix: noise, Tune and other controls had no effect on some earbuds. 1.4.5 asked the charging case for its battery on connect, which can stall the earbuds' command handling. That request is gone; the case figure now shows only when the earbuds report it on their own.

## 1.4.5
- Home on one screen, no scrolling (both apps): top strip with the device name, a connection dot, L / Case / R battery pills (number plus bar) and an "Offline" badge; noise control; Tune with Sound Profile chips; a bottom row of icon buttons (Gestures, L-R Balance, Low-Latency, Ping Buds) that open compact flyouts (Windows) or sheets (Android). Unsupported controls are hidden. Colors: background #0E0F12, surfaces #17191E, one accent; 8 px grid, 32 px rows, 13–14 sp text.
- Solo Mode: when only one earbud is in use, L/R chips show which one, and the missing bud's pill fades to an outline instead of 0%.
- Battery: the Android notification shows only the earbud in use (one progress bar) with a small Case figure; the Windows tray tooltip reads like "L 82%". Low-battery alerts use only the earbud in use; the missing one is never flagged as low or disconnected. The case level is read where the earbuds report it.
- Lock EQ: a padlock next to Tune dims the sliders; touching one shows "EQ locked". Remembered between launches.
- Sound Profiles: chips under Tune apply on tap (no Apply step), with a 3-second Undo. Long-press (Windows: hold or right-click) to save the current Tune, rename or delete. Slider changes are sent as you go.
- Fix: the battery is read immediately on connect (also when the app opens while already connected), and the strip, the notification and the tray refresh right away.
- Fix (Windows): the EQ sliders were drawn upside down (negative gains near the top). They now match the values.
- Windows: --demo no longer touches the Bluetooth stack.
- Names: screens Home, Sound, Gestures, About and Privacy. Noise modes Quiet, Aware, Off, Custom (+ Depth). EQ is "Tune", presets are "Sound Profiles", AutoEQ file import is "Import Correction", find is "Ping Buds", game mode is "Low-Latency", balance is "L-R Balance".
- About and Privacy: "No internet. No account. No tracking.", the permissions actually granted, and "Unofficial companion".
- Android: edge-to-edge on every version, fixed dark theme (no dynamic color). Windows: dark title bar (caption color matches the background on Windows 11).
- Docs: README and screenshots updated for 1.4.5 (demo mode).

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
