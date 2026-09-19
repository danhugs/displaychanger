# DisplayChanger

A tiny Windows tray app with three global hotkeys:

| Hotkey | Action |
|--------|--------|
| **Win + /** | Cycle the **primary display** (left to right, wrapping) |
| **Win + ]** | Cycle the default **audio output** (speakers / headphones) |
| **Win + '** | Cycle the default **audio input** (microphone) |
| **Win + [** | **Gather all windows** onto the display that holds the active window |

Display cycling keeps your physical arrangement intact: only the origin moves, so the taskbar and new
windows follow. Audio cycling sets the device for all three Windows roles (default, multimedia,
communications) so every app follows it.

## Features

- Tray icon with three submenus (Display, Output, Input) showing the current selection; click any device to pick it directly.
- **Include in cycle** sub-list under Output and Input: untick devices you never want the hotkey to land on (virtual cables, monitor speakers, and so on). They can still be chosen from the menu.
- **Gather windows** moves every ordinary app window onto the display containing the focused window. Each window keeps its offset within the screen, its size (shrunk if it would not fit), its minimised or maximised state and its place in the z-order. Windows already on that display are left alone.
- Double-click the tray icon to cycle the display.
- Registers itself to start at sign-in on first launch (per-user Run key, no admin). Untick **Start with Windows** to turn that off.
- A small dark pane in the bottom-right corner of the primary display lists the devices in the current category (Display, Output or Input) with the active one highlighted, then fades out. Pressing a hotkey repeatedly updates the same pane instead of queueing Windows notifications; switching to another category replaces the pane's contents. Untick **Show on-screen pane** to turn it off. Errors still use a tray balloon.
- Single instance: launching it again while it is running does nothing.
- Re-enumerates devices on every press and every menu open, so plugging things in or out just works.

## Download

Prebuilt exes are on the [Releases page](https://github.com/danhugs/displaychanger/releases/latest). Two flavours:

| File | Needs .NET installed? | Size |
|------|-----------------------|------|
| `DisplayChanger-<version>-win-x64.exe` | No (self-contained) | ~110 MB |
| `DisplayChanger-<version>-win-x64-framework-dependent.exe` | Yes, .NET 9 Desktop Runtime | ~0.2 MB |

Put the exe somewhere permanent (for example `%LocalAppData%\DisplayChanger\`) and run it. It registers itself
to start at sign-in. Windows SmartScreen may warn on first run because the exe is unsigned; choose
**More info > Run anyway**.

## Requirements

- Windows 10 or 11.
- .NET 9 Desktop Runtime for the framework-dependent build (already present if you have the .NET 9 SDK). The self-contained build has no requirements.

## Build and run

```powershell
dotnet build -c Release
.\src\DisplayChanger\bin\Release\net9.0-windows\DisplayChanger.exe
```

## Publish a single exe

Framework-dependent (small, needs the .NET 9 Desktop Runtime):

```powershell
dotnet publish src\DisplayChanger -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Self-contained (larger, runs on a machine without .NET installed):

```powershell
dotnet publish src\DisplayChanger -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Put the published `DisplayChanger.exe` somewhere permanent (for example `%LocalAppData%\DisplayChanger\`)
and run it from there. The start-with-Windows entry stores the absolute path of the exe; if you move it
later, launch it once from the new location and the entry is updated automatically.

## Where things live

| What | Where |
|------|-------|
| Settings (on-screen pane, cycle exclusions) | `%LocalAppData%\DisplayChanger\settings.json` |
| Startup entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DisplayChanger` |

To remove the startup entry manually:

```powershell
Remove-ItemProperty -Path HKCU:\Software\Microsoft\Windows\CurrentVersion\Run -Name DisplayChanger
```

## How it works

**Display.** Windows has no "make this the primary" call. The primary display is simply the one at
virtual-desktop origin (0,0). The app reads every attached display with `EnumDisplayDevices` /
`EnumDisplaySettings`, shifts all positions so the chosen display lands at (0,0), stages each change with
`ChangeDisplaySettingsEx(..., CDS_UPDATEREGISTRY | CDS_NORESET)` (plus `CDS_SET_PRIMARY` on the target),
and commits them in one go with a final `ChangeDisplaySettingsEx(null, ...)`. Friendly monitor names come
from `QueryDisplayConfig` / `DisplayConfigGetDeviceInfo`.

**Audio.** Endpoints are listed with the documented `IMMDeviceEnumerator` API and named via
`IPropertyStore` (`PKEY_Device_FriendlyName`). Setting the default uses `IPolicyConfig::SetDefaultEndpoint`,
which is undocumented but is what the Sound control panel and every audio-switcher utility use. All COM
interop is hand-written in `Native/CoreAudioInterop.cs`; there are no NuGet dependencies.

**Windows.** `EnumWindows` with the usual Alt-Tab filter (visible, titled, un-owned or `WS_EX_APPWINDOW`, not a tool
window, not DWM-cloaked, not a shell class). Each window is repositioned with `GetWindowPlacement` /
`SetWindowPlacement`, which lets maximised and minimised windows change display without being restored first.
Windows are processed back-to-front so re-showing a maximised window (which activates it) leaves the z-order intact
(`Windowing/WindowGatherer.cs`).

**Hotkeys.** `RegisterHotKey` with `MOD_WIN | MOD_NOREPEAT` on a hidden window (`HotkeyWindow.cs`).

## Caveats

- The hotkeys use US-layout virtual keys (`VK_OEM_2` for `/`, `VK_OEM_6` for `]`, `VK_OEM_7` for `'`, `VK_OEM_4` for `[`).
  On other layouts the physical key may differ. If Windows or another app already owns one of them the
  app shows a warning balloon and the tray menu still works. To change a key, edit the `Register` calls in
  `TrayAppContext.cs`.
- With a single display, Win+/ does nothing except show the pane with an "Only one display is connected" note.
- Some hybrid-GPU laptops reject position changes for the internal panel; the error is surfaced as a balloon and nothing is half-applied.
- Gather windows cannot move windows belonging to elevated (run-as-administrator) apps unless DisplayChanger itself is elevated; the pane reports how many were skipped. Windows on other virtual desktops are ignored.
