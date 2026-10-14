# CatSaver 🐱💧

A cute Windows hydration reminder. CatSaver runs quietly in the system tray. At the top of every hour an animated cat pops up in the middle of the screen, holding a glass of water and waving, and says **「起來喝水了！」**. It stays for **16 minutes**, then fades away on its own. This repeats every hour.

![CatSaver reminder](docs/screenshot.png)

> Why not a real `.scr` screen saver? Windows only starts screen savers when the PC is idle, so one can't reliably pop up on the hour while you're working. CatSaver is a tray app that draws an always-on-top overlay instead.

## Features

- Background tray app; launching it shows no window.
- Reminders start at every exact hour (10:00, 11:00, ...), last 16 minutes, then hide automatically.
- Animated cat drawn in code (no image assets): it waves, blinks, swishes its tail, sips from a glass of water, and water drops float around it. The speech bubble shows the message and a countdown (「還剩 15:42」).
- Dims every monitor and shows the cat on each one (configurable to the primary monitor only).
- **Doesn't steal focus.** The overlay never takes keyboard focus, and by default mouse clicks pass through to the apps underneath.
- Starts with Windows (installer checkbox, or toggle in the tray menu).
- Tray menu (right-click the cat icon):
  - 現在顯示貓咪: show the cat now (also double-click the icon)
  - 關閉這次提醒: dismiss the current reminder
  - 暫停提醒: pause/resume reminders
  - 開機時自動啟動: start with Windows
  - 開啟設定檔…: open the config file
  - 重新啟動: restart (applies config changes)
  - 結束: quit
- If the PC wakes up or the app starts partway through a reminder window (say 10:05), the cat shows for the rest of that window (until 10:16).

## Install (end users)

Download from the GitHub Actions build artifacts or a GitHub Release. Either:

- **`CatSaver-Setup.exe`** (recommended): a per-user install with no admin rights needed. It offers "開機時自動啟動" (checked by default) and launches CatSaver when it finishes.
- **`CatSaver.exe`**: a portable, self-contained single exe that doesn't need the .NET runtime. Put it anywhere and run it. To start it with Windows, right-click the tray icon and choose 「開機時自動啟動」.

Windows SmartScreen may warn the first time because the exe isn't code-signed. Click *More info → Run anyway*.

To check the look without waiting for the next hour, double-click the tray icon, or run `CatSaver.exe --show`.

## Configuration

Settings live in `%APPDATA%\CatSaver\config.json`. The file is created with defaults on first run. Edit it (tray → 開啟設定檔…), save, then choose tray → 重新啟動.

```json
{
  "intervalMinutes": 60,          // minutes between reminders, aligned to midnight (60 = every exact hour)
  "durationMinutes": 16,          // how long the cat stays
  "message": "起來喝水了！",
  "subMessage": "離開電腦，伸展一下，喝杯水吧～",
  "showCountdown": true,
  "monitors": "all",              // "all" or "primary"
  "dimScreen": true,
  "dimOpacity": 0.35,             // 0.0 – 0.9
  "clickThrough": true,           // false = dimmed area blocks mouse clicks (keyboard is never blocked)
  "catScale": 1.0,                // 0.2 – 2.0
  "catColor": "#F6A55A",          // fur colour
  "animation": "wave",
  "animationFps": 30,
  "fontFamily": "Microsoft JhengHei UI"
}
```

Comments are allowed in the file. Out-of-range values are clamped to safe limits. If the file is broken, CatSaver falls back to the defaults and shows a warning balloon.

## Build from source

Requirements: Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). For the installer you also need [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

```powershell
# run from the repo root
.\build.ps1                 # -> artifacts\publish\CatSaver.exe (self-contained single file)
.\build.ps1 -Test -Installer  # also run unit tests and build artifacts\CatSaver-Setup.exe

# or, while developing
dotnet run --project src\CatSaver -- --show
dotnet test tests\CatSaver.Tests
```

CI (`.github/workflows/build.yml`) builds both files on `windows-latest` for every push and PR and uploads them as the **CatSaver** artifact. Pushing a `v*` tag (e.g. `v1.0.0`) also attaches them to a GitHub Release.

## Code layout

```
src/CatSaver/
  Program.cs              entry point, single-instance mutex, restart handling
  TrayApplication.cs      tray icon + menu, 1-second schedule check → show/hide overlay
  ReminderSchedule.cs     pure scheduling logic (interval / duration), unit-tested
  AppConfig.cs            JSON config with defaults + validation
  AutoStart.cs            HKCU\...\Run registry entry
  NativeMethods.cs        Win32 interop
  Overlay/
    OverlayManager.cs     creates the windows per monitor, animation loop, fade in/out, keeps topmost
    CatWindow.cs          per-pixel-alpha layered window (UpdateLayeredWindow), click-through, no-activate
    DimForm.cs            semi-transparent full-screen dimming layer
    CatRenderer.cs        draws the cat, glass, drops and speech bubble with GDI+ (change the look here)
  Assets/catsaver.ico     app/tray icon (regenerate with tools/make_icon.py)
tests/CatSaver.Tests      xUnit tests for the schedule (runs on any OS)
installer/CatSaver.iss    Inno Setup script
```

To change the animation, edit `Overlay/CatRenderer.cs`. Everything is drawn in an 800×880 logical canvas that is scaled to the screen, and the `Draw` method gets the elapsed time and remaining time for each frame.
