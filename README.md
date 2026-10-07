# Washline

**Screenshots, hung out to dry, on Windows.** Every screenshot you take hangs on a line just above the top of your screen. Rest the pointer against the top edge and it glides down. Move away and it tucks back up.

A Windows port of [Tendedero](https://github.com/alejandrobujan/tendedero) by Alejandro Buján, rewritten in C# and WPF. Not affiliated with or endorsed by the original author. The Tendedero name and icon are not used here, per its license.

## A gesture for everything

| Gesture | What it does |
|---|---|
| Click | Copy the image |
| Press and hold | Open it in Paint to mark up |
| Double click | Open it in your image viewer |
| Drag into an app | Send a copy. It stays on the line |
| Drag into a folder or the Recycle Bin | Moves it there. It leaves the line |
| Click the corner cross | Let it go (takes it off the line; the file stays) |
| Right click | Copy, Open, Mark up, Show in Explorer, Take down, Move to Recycle Bin |
| Rest the pointer at the top edge | Bring the line down on that screen |
| Click near the top of the screen | Put it away |
| `Ctrl` + `Alt` + `T` | Show or hide the line |

## What it picks up

- **Win + PrtScn** and the **Snipping Tool** (Win + Shift + S on Windows 11, which auto-saves): both land in your Screenshots folder, which Washline watches.
- **Plain PrtScn / Alt + PrtScn** only copy to the clipboard. Turn on *Also catch screenshots copied to the clipboard* in the tray menu and those hang too, saved to `%LocalAppData%\Washline\Captures`. Taking one of those down sends it to the Recycle Bin, so that folder never fills up.

Washline never takes screenshots itself. Keep your usual shortcuts.

## Private by design

No account. No network. No analytics. Settings live in `%AppData%\Washline\settings.json`.

## Install

Download `Washline.exe` from the latest release (or the latest **Build** run under Actions) and run it. It's a single self-contained file, no .NET install needed. It lives in the notification area; tick *Open at sign-in* in its menu to start it with Windows.

Windows SmartScreen may warn the first time because the exe isn't code-signed. Click *More info* → *Run anyway*.

## Build from source

Requires the .NET 8 SDK.

```
git clone <this repo>
cd washline
dotnet run --project src
```

Single-file release build:

```
dotnet publish src/Washline.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist
```

## Inside the app

| File | Role | macOS original |
|---|---|---|
| `Program.cs` | Startup, single instance, tray icon and menu | `main.swift`, `AppDelegate.swift` (menu bar) |
| `LineWindow.cs` | The transparent strip, revealing and tucking away, hotkey, clipboard, full screen | `AppDelegate.swift`, `LinePanel.swift`, `LineView.swift`, `HotKey.swift`, `FullScreen.swift` |
| `CardView.cs` | One photo: glass frame, clip, swing and breeze; click, hold, drag | `PeggedView.swift`, `GrabArea.swift` |
| `Line.cs` | What is hanging, and what you can do with it | `Line.swift` |
| `ScreenshotWatcher.cs` | Notices new screenshots | `ScreenshotWatcher.swift` |
| `Settings.cs` | Preferences and open at sign-in | `UserDefaults`, `SMAppService` |
| `Native.cs` | Win32 calls | |

## Differences from the macOS app

- No menu bar on Windows, so the line comes down when the pointer rests against the very top edge. A click near the top (a browser tab, a title bar) puts it away until the pointer moves off.
- Markup opens in Paint instead of the macOS Markup sheet; the photo refreshes after you save.
- The capture doesn't fly in from where it was taken; it drops onto the line.
- Washline doesn't change Windows' screenshot settings. The Snipping Tool keeps saving where it always does.

## License

MIT, see [LICENSE](LICENSE). Original code © Alejandro Buján.
