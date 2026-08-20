# CornerCalendar

[![.NET 8](https://img.shields.io/badge/.NET-8-blue)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-purple)](https://learn.microsoft.com/zh-cn/dotnet/desktop/wpf/)
[![Windows](https://img.shields.io/badge/Platform-Windows-0078d4)](https://www.microsoft.com/windows)
[![GitHub Release](https://img.shields.io/github/v/release/Chendaqian/CornerCalendar?label=Release)](https://github.com/Chendaqian/CornerCalendar/releases/latest)
[![Build Status](https://img.shields.io/github/actions/workflow/status/Chendaqian/CornerCalendar/release.yml?label=Build)](https://github.com/Chendaqian/CornerCalendar/actions/workflows/release.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE.txt)
[![GitHub Stars](https://img.shields.io/github/stars/Chendaqian/CornerCalendar?style=flat)](https://github.com/Chendaqian/CornerCalendar/stargazers)
[![GitHub Downloads](https://img.shields.io/github/downloads/Chendaqian/CornerCalendar/total?style=flat)](https://github.com/Chendaqian/CornerCalendar/releases/latest)
[![GitHub Last Commit](https://img.shields.io/github/last-commit/Chendaqian/CornerCalendar?style=flat)](https://github.com/Chendaqian/CornerCalendar/commits/master)

**[English](README.md) | 简体中文**

CornerCalendar 是一款 Windows 日历小工具，用紧凑的月历面板替换任务栏日历弹窗。程序常驻系统托盘，点击任务栏时间即可打开，不需要启动完整的桌面日历应用。

## 功能

- 月历视图，显示农历、传统节日、二十四节气、中国大陆法定节假日和调休补班信息。
- 中国日历数据来自远程 [YangH9/ChinaCalendar](https://github.com/YangH9/ChinaCalendar) ICS 数据源，并支持缓存。
- 每日宜忌使用 MIT 许可的开源库 [6tail/lunar-csharp](https://github.com/6tail/lunar-csharp) 按日期计算；ICS 订阅源提供宜忌时优先使用订阅源数据。
- 使用 [Ical.Net](https://github.com/ical-org/ical.net) 解析 ICS，支持多个订阅、订阅别名、刷新频率和事件颜色圆点。
- 点击日期打开独立详情窗口，展示农历、节日、节气、休班状态和当天日程。
- 主窗口顶部显示天气摘要，支持公网 IP 自动定位或手动设置城市；设置多个城市后可以在主窗口切换。
- 支持使用 `DateTime.ToString` 格式自定义任务栏时间，输入字面量 `\n` 可换行。
- 支持系统托盘图标、托盘右键菜单、开机自启动、浅色/深色/跟随系统主题和字号设置。
- 拦截任务栏日历并在程序退出时恢复系统窗口，避免原生 Windows 日历无法再次打开。

## 运行要求

- Windows 10 或 Windows 11。
- 从源码构建需要 .NET 8 SDK。
- 自包含版本已包含 .NET 运行时，不需要额外安装运行时。
- 获取最新中国日历、ICS、地理编码和天气数据需要网络；支持缓存的数据源在断网时可以继续使用缓存。

## 下载

从 [GitHub Releases](https://github.com/Chendaqian/CornerCalendar/releases/latest) 下载最新版本。

每次发布会提供两个 Windows x64 制品：

- `self-contained`：包含 .NET 运行时，推荐大多数用户使用。
- `framework`：体积更小，但需要安装 .NET 8 Desktop Runtime。

## 从源码构建

```powershell
dotnet build src\CornerCalendar.sln
dotnet test src\CornerCalendar.sln
```

运行调试版本：

```powershell
.\src\CornerCalendar\bin\Debug\net8.0-windows\win-x64\CornerCalendar.exe
```

生成两个发布版本：

```powershell
pwsh scripts\Publish-Artifacts.ps1
```

制品输出到 `release/`，该目录不会提交到仓库。

## 版本与发布

[`CornerCalendar.csproj`](src/CornerCalendar/CornerCalendar.csproj) 中的 `<Version>` 会生成程序集版本。发布工作流会先构建 `CornerCalendar.dll`，再从实际程序集的 `AssemblyName.Version` 读取版本，并用这个版本命名制品和 GitHub Release。tag 只用于触发工作流，必须与读取到的程序集版本一致，可以带或不带 `v` 前缀。

发布流程：

1. 修改 `src/CornerCalendar/CornerCalendar.csproj` 中的 `<Version>`。
2. 提交并推送修改。
3. 执行 `pwsh scripts/Publish-Release.ps1`，脚本会构建程序集、读取当前程序集版本，创建匹配的 `v<Version>` tag 并推送到 GitHub。

[`.github/workflows/release.yml`](.github/workflows/release.yml) 会校验 tag、构建两个制品、上传文件，并自动生成 GitHub Release 描述。可先使用 `-WhatIf` 预览版本和 tag，不会执行推送。

## 数据与隐私

CornerCalendar 将设置和支持缓存的数据保存到 `%LOCALAPPDATA%\CornerCalendar\`。天气自动定位会请求公网 IP 定位服务，手动设置城市会使用 Open-Meteo 地理编码；用户配置的日历 URL 由应用直接请求。

## 项目结构

```text
src/
├── CornerCalendar.sln
├── CornerCalendar.Tests/
└── CornerCalendar/
    ├── App.xaml(.cs)                 # 组合根、托盘、任务栏时钟和生命周期
    ├── Core/Models/                   # 日历、中国日历和天气模型
    ├── Core/Services/                 # 设置、ICS、中国日历和天气服务
    ├── Core/Helpers/                  # Win32、主题、农历、自启动和图标帮助类
    ├── ViewModels/                    # 月历和事件展示逻辑
    └── Views/                         # 弹出面板、设置、详情、任务栏和控件
```

## 当前限制

Windows 日历 API 实现仍保留在仓库中，但由于需要 Windows SDK 和 CsWinRT 配置，目前被排除在编译之外。未启用该集成时，系统日历数据源会回退为空服务，不会显示伪造的日程。

## 许可证

CornerCalendar 使用 [MIT License](LICENSE.txt) 开源。

## 共建者

<a href="https://github.com/Chendaqian/MagicCenterHub/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=Chendaqian/MagicCenterHub" />
</a>

## 星标历史

<a href="https://www.star-history.com/?repos=Chendaqian%2FCornerCalendar&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=Chendaqian/CornerCalendar&type=date&theme=dark&legend=top-left&sealed_token=GSQCZEqbIKA2ooC2Ro_m5B-BQOYdGGq1wjfaNu0yuioAu4cB8U4I4SZEkKp8fwhPgbCyRHmroRbKl3rs7RpAYNDVB-HHxiRzhy9KSm61wEvrJtelCgGK1U7DOeMQ5vP9q1Rg57rbJ1Ms6V_GKDx0zdoEw7_ru9hMswBpzHh_Bx6wLYeTSR4ReJW1C0fL" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=Chendaqian/CornerCalendar&type=date&legend=top-left&sealed_token=GSQCZEqbIKA2ooC2Ro_m5B-BQOYdGGq1wjfaNu0yuioAu4cB8U4I4SZEkKp8fwhPgbCyRHmroRbKl3rs7RpAYNDVB-HHxiRzhy9KSm61wEvrJtelCgGK1U7DOeMQ5vP9q1Rg57rbJ1Ms6V_GKDx0zdoEw7_ru9hMswBpzHh_Bx6wLYeTSR4ReJW1C0fL" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=Chendaqian/CornerCalendar&type=date&legend=top-left&sealed_token=GSQCZEqbIKA2ooC2Ro_m5B-BQOYdGGq1wjfaNu0yuioAu4cB8U4I4SZEkKp8fwhPgbCyRHmroRbKl3rs7RpAYNDVB-HHxiRzhy9KSm61wEvrJtelCgGK1U7DOeMQ5vP9q1Rg57rbJ1Ms6V_GKDx0zdoEw7_ru9hMswBpzHh_Bx6wLYeTSR4ReJW1C0fL" />
 </picture>
</a>
