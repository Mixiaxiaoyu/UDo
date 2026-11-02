# UDo

[简体中文](README.md) · English

UDo is a Windows desktop Dock. Godot renders the interface, while Win32 and Windows Composition handle windows, the taskbar, and the glass background.

Its visual style takes inspiration from the glass material of macOS 27. UDo is an independent Windows implementation and is not affiliated with Apple or the authors of the Figma reference design. The repository does not include images from that design; application icons come from Windows.

> **Preview release:** UDo is still in development. Features and UI may change, and you may encounter bugs, compatibility issues, or unstable behavior. Feedback through Issues is welcome.

## Features

- Pin applications; show running windows and tray apps; switch, minimize, or launch programs.
- Window thumbnail previews, icon magnification, and light, dark, and glass Dock appearances.
- Optionally hide the Windows taskbar; press `Ctrl + Alt + F12` to restore it.
- Desktop storage boxes, desktop partitions, and draggable desktop widgets.
- Optional key animations and key count statistics. Individual keystrokes are not saved to disk.

## Run from source

UDo currently supports Windows only. You will need:

1. Godot 4.7.1 .NET edition.
2. Visual Studio C++ x64 build tools and a Windows 10/11 SDK.
3. The .NET Framework 4.x compiler included with Windows.

Run in PowerShell:

```powershell
cd <project-directory>
.\build.ps1 -GodotPath 'C:\path\to\Godot_v4.7.1-stable_mono_win64.exe'
.\UDo.exe
```

After the first build, the Godot path is saved locally in `godot-path.txt`. Later builds can use `.\build.ps1` without the parameter. You can also open `project.godot` in Godot and run `main.tscn`; run the build script first for native features.

Build outputs, user settings, session data, test screenshots, and original Figma images are not included in this source repository. There is no prebuilt installer here.

## Source layout

| Path | Purpose |
| --- | --- |
| `scripts/` | Godot UI, interactions, themes, and shaders |
| `native/DockBridge.cs` | Windows window, taskbar, icon, and tray integration |
| `native/ComposedGlass.cs`, `native/GlassEffects.cpp` | Live backdrop sampling, rounded corners, and glass refraction |
| `native/DesktopZones/`, `native/DesktopPartitions/` | Storage boxes and desktop partitions |
| `native/Launcher.cs`, `build.ps1` | Launcher and build process |

User settings are stored locally in `data/`. They may include application paths, window titles, and key count statistics. Git ignores this directory.

## Feedback and contributions

Contributions are welcome. Use [Issues](https://github.com/Mixiaxiaoyu/UDo/issues) to report bugs or suggest improvements, and open a [Pull Request](https://github.com/Mixiaxiaoyu/UDo/pulls) for fixes or features.

For bug reports, please include your Windows version, steps to reproduce, and expected and actual results. Discuss larger changes in an Issue before starting if coordination would help. In a PR, describe what changed and how you checked it. Remove personal paths, window titles, and other private information from public reports and logs.

## License

Copyright (C) 2026 Mixiaxiaoyu. The source code is released under the [GNU GPL v3.0](LICENSE). Distributions of modified versions must follow its source code requirements.
