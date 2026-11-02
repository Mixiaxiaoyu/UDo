# 有兜 · UDo

有兜是一个 Windows 桌面 Dock，使用 Godot 绘制界面，并通过 Win32 与 Windows Composition 管理窗口、任务栏和玻璃背景。

视觉风格参考 macOS 27 的玻璃材质。本项目是独立的 Windows 实现，与 Apple 或 Figma 设计稿作者无关联。仓库不包含设计稿中的原始图片素材；应用图标由 Windows 提供。

## 功能

- 固定应用，显示运行窗口和托盘应用，切换、最小化或启动程序。
- 窗口缩略预览、图标悬停放大、浅色／深色／玻璃三种 Dock 外观。
- 可选隐藏 Windows 任务栏；按 `Ctrl + Alt + F12` 可恢复。
- 桌面收纳盒、无感桌面分区，以及可拖动的桌面组件。
- 可选按键动画与按键次数统计。逐次按键内容不写入磁盘。

## 从源码运行

目前仅支持 Windows。准备好：

1. Godot 4.7.1 .NET 版。
2. Visual Studio C++ x64 构建工具和 Windows 10/11 SDK。
3. Windows 自带的 .NET Framework 4.x 编译器。

在 PowerShell 中运行：

```powershell
cd <项目目录>
.\build.ps1 -GodotPath 'C:\path\to\Godot_v4.7.1-stable_mono_win64.exe'
.\UDo.exe
```

首次构建后，Godot 路径保存在本机的 `godot-path.txt`。再次构建可直接运行 `.\build.ps1`。也可以用 Godot 打开 `project.godot` 并运行 `main.tscn`；原生功能仍需先执行构建脚本。

构建产物、用户设置、会话数据、测试截图和 Figma 原图不包含在源码仓库中。此仓库不提供可直接运行的安装包。

## 代码结构

| 路径 | 用途 |
| --- | --- |
| `scripts/` | Godot 界面、交互、主题与着色器 |
| `native/DockBridge.cs` | Windows 窗口、任务栏、图标和托盘桥接 |
| `native/ComposedGlass.cs`、`native/GlassEffects.cpp` | 实时背景采样、圆角和玻璃折射 |
| `native/DesktopZones/`、`native/DesktopPartitions/` | 收纳盒与桌面分区 |
| `native/Launcher.cs`、`build.ps1` | 启动器与构建 |

用户设置保存在本机 `data/`，其中可能包含应用路径、窗口标题及按键统计。该目录已被 Git 忽略。

## 许可证

源代码按 [GNU GPL v3.0](LICENSE) 发布。重新分发修改版时，请遵守该许可证的源码提供要求。
