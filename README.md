# NJU-
由于nju上网免费时长有限（虽然付费部分也不贵），但是对于很多同学来说免费时长就够用了，像我一样经常忘记出门前、睡觉前断网的同学应该也挺多。这个程序可以完美解决这个问题，简约好用，免费开源，欢迎进行升级和讨论

# NJU-WLAN 自动连断网工具

针对南京大学校园网（NJU-WLAN）的小工具：电脑空闲超过设定时间后自动断网，重新操作时自动联网，避免按时长计费的校园网被白白扣时长。

## 功能

- 图形界面，输入账号密码即可登录
- 一键联网、一键断网、查看当前状态
- 自动模式：空闲自动断网、恢复操作自动联网
- 可自定义空闲超时时间
- 贴边隐藏：窗口收起到屏幕右边缘，鼠标移上去自动弹出
- 账号密码本地保存，每个用户各自独立

## 快速开始

### Windows

双击 `NJU-WLAN网络助手.exe` 即可运行，无需安装。

1. 在“账号与超时设置”里输入校园网账号、密码，设置超时时间（默认 5 分钟）。
2. 点“保存并登录”：保存并立即用这个账号登录；点“保存设置”：只保存。
3. 之后用“立即联网”“立即断网”“启动自动模式”等按钮。

Windows 版的配置保存在系统目录 `%APPDATA%\NJU-WLAN-Helper\config.json`。

### macOS

进入 `macos` 文件夹，双击 `启动.command`。

首次使用前，在终端里进入 `macos` 文件夹执行一次：

```bash
chmod +x 启动.command
```

macOS 需要 Python 3 和 tkinter，详见 `macos/README_macOS.md`。

## 从源码编译（Windows）

需要 .NET Framework 4.0（Windows 自带）。双击 `build.bat`，或在命令行执行：

```bat
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe ^
  /nologo /optimize+ /codepage:65001 /target:winexe ^
  /out:NJU-WLAN网络助手.exe /win32icon:app.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll ^
  NJU_Network_Assistant.cs
```

## 目录结构

```
.
├── NJU-WLAN网络助手.exe       # Windows 版（已编译，可直接运行）
├── NJU_Network_Assistant.cs   # Windows 版源码
├── app.ico                    # 图标
├── build.bat                  # Windows 一键编译脚本
└── macos/                     # macOS 版（Python + Tk）
```

## 隐私与安全

- 账号密码只保存在本机：
  - Windows：`%APPDATA%\NJU-WLAN-Helper\config.json`
  - macOS：`macos/config.json`
- `config.json` 已被 `.gitignore` 忽略，请勿把含账号密码的 `config.json` 提交到公开仓库。

## 说明

本工具仅供学习交流，请遵守学校网络使用规定。

## 从源码编译（Windows）

需要 .NET Framework 4.0（Windows 自带）。双击 `build.bat`，或在命令行执行：

```bat
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe ^
  /nologo /optimize+ /codepage:65001 /target:winexe ^
  /out:NJU-WLAN网络助手.exe /win32icon:app.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll ^
  NJU_Network_Assistant.cs
```

## 目录结构

```
.
├── NJU-WLAN网络助手.exe       # Windows 版（已编译，可直接运行）
├── NJU_Network_Assistant.cs   # Windows 版源码
├── app.ico                    # 图标
├── build.bat                  # Windows 一键编译脚本
└── macos/                     # macOS 版（Python + Tk）
```

## 隐私与安全

- 账号密码只保存在本机：
  - Windows：`%APPDATA%\NJU-WLAN-Helper\config.json`
  - macOS：`macos/config.json`
- `config.json` 已被 `.gitignore` 忽略，请勿把含账号密码的 `config.json` 提交到公开仓库。

## 说明

本工具仅供学习交流，请遵守学校网络使用规定。
