# CornerCalendar

[![.NET 8](https://img.shields.io/badge/.NET-8-blue)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-purple)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![Windows](https://img.shields.io/badge/Platform-Windows-0078d4)](https://www.microsoft.com/windows)
[![GitHub Release](https://img.shields.io/github/v/release/Chendaqian/CornerCalendar?label=Release)](https://github.com/Chendaqian/CornerCalendar/releases/latest)
[![Build Status](https://img.shields.io/github/actions/workflow/status/Chendaqian/CornerCalendar/release.yml?label=Build)](https://github.com/Chendaqian/CornerCalendar/actions/workflows/release.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE.txt)
[![GitHub Stars](https://img.shields.io/github/stars/Chendaqian/CornerCalendar?style=flat)](https://github.com/Chendaqian/CornerCalendar/stargazers)
[![GitHub Downloads](https://img.shields.io/github/downloads/Chendaqian/CornerCalendar/total?style=flat)](https://github.com/Chendaqian/CornerCalendar/releases/latest)
[![GitHub Last Commit](https://img.shields.io/github/last-commit/Chendaqian/CornerCalendar?style=flat)](https://github.com/Chendaqian/CornerCalendar/commits/master)

**English | [简体中文](README_zh.md)**

<div align="center">
  ![icon](https://raw.githubusercontent.com/Chendaqian/CornerCalendar/refs/heads/master/src/CornerCalendar/Resources/icon.png)
</div>

CornerCalendar is a Windows calendar utility that replaces the taskbar calendar flyout with a compact monthly calendar. It stays in the system tray, opens from the taskbar clock, and keeps calendar information close without opening a full desktop calendar application.

## Features

- Monthly calendar with lunar dates, traditional festivals, solar terms, legal holidays, and Chinese mainland workday adjustments.
- Four built-in ICS calendars from [YangH9/ChinaCalendar](https://github.com/YangH9/ChinaCalendar): legal holidays and makeup workdays, festivals and observances, solar terms, and lunar dates; all use local caching.
- Daily auspicious and inauspicious activities are calculated with the MIT-licensed [6tail/lunar-csharp](https://github.com/6tail/lunar-csharp) library; ICS-provided values take priority when available.
- ICS subscriptions parsed by [Ical.Net](https://github.com/ical-org/ical.net), including multiple subscriptions, aliases, refresh intervals, and event-color dots.
- Click a date to open a separate detail window showing lunar information, holidays, and that day's schedules.
- Optional weather summary at the top of the calendar, with IP-based automatic location or manually configured cities. Multiple cities can be switched from the main panel. Weather is prefetched in the background, cached locally, and refreshed at a configurable 30/60/120/240-minute interval (120 minutes by default).
- Custom taskbar clock format using `DateTime.ToString` patterns, including the literal `\n` for a new line.
- System tray icon, tray context menu, startup option, light/dark/follow-system themes, configurable font size, and settings/about/update pages.
- The taskbar clock overlay is created only on the primary display. Other displays keep the native Windows taskbar clock and notification center.

## Requirements

- Windows 10 or Windows 11.
- .NET 8 SDK for development.
- The self-contained release does not require a separate .NET runtime.
- Network access is required for fresh ChinaCalendar, user ICS, geocoding, and weather data. Cached ICS and weather data can be used while offline.

## Download

Download the latest release from [GitHub Releases](https://github.com/Chendaqian/CornerCalendar/releases/latest).

Two Windows x64 packages are published:

- `self-contained`: includes the .NET runtime and is the recommended package for most users.
- `framework`: smaller package that requires the .NET 8 Desktop Runtime.

## Build From Source

```powershell
dotnet build src\CornerCalendar.sln
dotnet test src\CornerCalendar.sln
```

Run the debug build with:

```powershell
.\src\CornerCalendar\bin\Debug\net8.0-windows\win-x64\CornerCalendar.exe
```

Create both release variants with:

```powershell
pwsh scripts\Publish-Artifacts.ps1
```

The artifacts are written to `release/` and are not committed to the repository.

## Versioning And Releases

`<Version>` in [`CornerCalendar.csproj`](src/CornerCalendar/CornerCalendar.csproj) controls the generated assembly version. The release workflow builds `CornerCalendar.dll`, reads its actual `AssemblyName.Version`, and uses that value for the artifacts and GitHub Release. The tag only triggers the workflow and must match the detected assembly version, with or without a leading `v`.

Release procedure:

1. Update `<Version>` in `src/CornerCalendar/CornerCalendar.csproj`.
2. Commit and push the change.
3. Run `pwsh scripts/Publish-Release.ps1` to build the current assembly, create the matching `v<Version>` tag, and push it to GitHub.

The workflow in [`.github/workflows/release.yml`](.github/workflows/release.yml) validates the tag, builds both packages, uploads them, and uses GitHub's generated release notes for the Release description. Use `-WhatIf` to preview the version and tag without pushing anything.

## Data And Privacy

CornerCalendar stores settings, calendar caches, weather cache, and error logs under `%LOCALAPPDATA%\CornerCalendar\`:

- `settings.json`: application settings.
- `cache/`: ICS subscription cache files.
- `weather-cache.json`: locally cached weather results.
- `error.log`: application error log.

Weather automatic location uses a public IP geolocation service; manually configured cities use Open-Meteo geocoding. Calendar URLs configured by the user are requested directly by the application.

## Configuration

The Settings window can configure theme, font size, startup behavior, user ICS subscriptions, ICS refresh frequency, weather locations, weather API URL, weather refresh frequency, display options, and taskbar time format. The weather refresh frequency defaults to 120 minutes and can be changed to 30, 60, or 240 minutes.

## Project Layout

```text
src/
├── CornerCalendar.sln
├── CornerCalendar.Tests/
└── CornerCalendar/
    ├── App.xaml(.cs)                 # composition root, tray, taskbar clock, lifecycle
    ├── Core/Models/                   # calendar, ChinaCalendar, and weather models
    ├── Core/Services/                 # settings, ICS, ChinaCalendar, and weather services
    ├── Core/Helpers/                  # Win32, theme, lunar, startup, and icon helpers
    ├── ViewModels/                    # calendar and event presentation logic
    └── Views/                         # popup, settings, detail, taskbar, and controls
```

## Calendar Data Source

The application uses ICS subscriptions as its only calendar data source. The built-in ChinaCalendar subscriptions are:

- `中国日历-法定节假日`: legal holidays and makeup workdays — [`cal_holiday.ics`](https://yangh9.github.io/ChinaCalendar/cal_holiday.ics).
- `中国日历-节日纪念日`: festivals and observances — [`cal_festival.ics`](https://yangh9.github.io/ChinaCalendar/cal_festival.ics).
- `中国日历-二十四节气`: the 24 solar terms — [`cal_solarTerm.ics`](https://yangh9.github.io/ChinaCalendar/cal_solarTerm.ics).
- `中国日历-农历`: lunar dates and almanac information — [`cal_lunar.ics`](https://yangh9.github.io/ChinaCalendar/cal_lunar.ics).

User-added calendars are also loaded through ICS. The Windows system calendar is not used by the application.

## License

CornerCalendar is released under the [MIT License](LICENSE.txt).

## Contributors

<a href="https://github.com/Chendaqian/MagicCenterHub/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=Chendaqian/MagicCenterHub" />
</a>

## Star History

<a href="https://www.star-history.com/?repos=Chendaqian%2FCornerCalendar&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=Chendaqian/CornerCalendar&type=date&theme=dark&legend=top-left&sealed_token=GSQCZEqbIKA2ooC2Ro_m5B-BQOYdGGq1wjfaNu0yuioAu4cB8U4I4SZEkKp8fwhPgbCyRHmroRbKl3rs7RpAYNDVB-HHxiRzhy9KSm61wEvrJtelCgGK1U7DOeMQ5vP9q1Rg57rbJ1Ms6V_GKDx0zdoEw7_ru9hMswBpzHh_Bx6wLYeTSR4ReJW1C0fL" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=Chendaqian/CornerCalendar&type=date&legend=top-left&sealed_token=GSQCZEqbIKA2ooC2Ro_m5B-BQOYdGGq1wjfaNu0yuioAu4cB8U4I4SZEkKp8fwhPgbCyRHmroRbKl3rs7RpAYNDVB-HHxiRzhy9KSm61wEvrJtelCgGK1U7DOeMQ5vP9q1Rg57rbJ1Ms6V_GKDx0zdoEw7_ru9hMswBpzHh_Bx6wLYeTSR4ReJW1C0fL" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=Chendaqian/CornerCalendar&type=date&legend=top-left&sealed_token=GSQCZEqbIKA2ooC2Ro_m5B-BQOYdGGq1wjfaNu0yuioAu4cB8U4I4SZEkKp8fwhPgbCyRHmroRbKl3rs7RpAYNDVB-HHxiRzhy9KSm61wEvrJtelCgGK1U7DOeMQ5vP9q1Rg57rbJ1Ms6V_GKDx0zdoEw7_ru9hMswBpzHh_Bx6wLYeTSR4ReJW1C0fL" />
 </picture>
</a>
