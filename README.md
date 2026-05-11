<div align="center">

# 🪨 WinNotch

**A Dynamic Island–inspired media & clock overlay for Windows**

[![Release](https://img.shields.io/github/v/release/prasundebnath/WinNotch?style=flat-square&color=30D158)](https://github.com/prasundebnath/WinNotch/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4?style=flat-square)](https://github.com/prasundebnath/WinNotch)
[![License](https://img.shields.io/badge/license-MIT-blueviolet?style=flat-square)](LICENSE)

</div>

---

<img width="3276" height="1280" alt="winnotch v1 0" src="https://github.com/user-attachments/assets/194d7303-9c80-4f9a-bc7b-dfd80c58ff4b" />





## ✨ What it does


WinNotch sits silently on top of the screen and springs to life when you hover over it — just like Apple's Dynamic Island, but for Windows.

| State | Behaviour |
|---|---|
| **Idle** (no music) | Hover → clock + mini calendar |
| **Music playing/paused** | Hover → album art, track title, artist, playback controls |
| **Music + scroll** | Scroll wheel → toggle between media card and clock/calendar |

### Features
- 🎵 **Live media info** — integrates with the Windows media session API (Spotify, YouTube Music, browsers, etc.)
- ⏯ **Playback controls** — play/pause, previous, next without leaving your workflow
- 🕐 **Live clock** — always shows the current time in the collapsed pill
- 📅 **Mini calendar** — current month with today highlighted in green
- 🌊 **Spring physics animations** — smooth, bouncy expand/collapse just like Dynamic Island
- 🖤 **Anti-corner flanges** — flush with the top screen edge, zero-gap
- 👻 **Completely non-intrusive** — hidden from taskbar and Alt+Tab, never steals focus

---

## 🚀 Installation (No setup required)

1. Go to the [**Releases**](https://github.com/prasundebnath/WinNotch/releases/latest) page
2. Download **`WinNotch.exe`**
3. Run it — no installer, no .NET download needed
4. WinNotch starts minimised to the system tray area and is always on top

> **Requirements:** Windows 10 (19041+) or Windows 11, x64

---

## 🛠 Build from source

```bash
git clone https://github.com/prasundebnath/WinNotch.git
cd WinNotch
dotnet run
```

To build a self-contained release exe:

```bash
dotnet publish WinNotch.csproj -r win-x64 -c Release --self-contained true -p:PublishSingleFile=true -o ./dist/Release
```

**Requirements:** .NET 10 SDK

---

## 🏗 Project structure

```
WinNotch/
├── App.xaml / App.xaml.cs          # Application entry point & startup
├── Views/
│   ├── NotchWindow.xaml            # UI layout (collapsed pill + media card + clock panel)
│   └── NotchWindow.xaml.cs         # Spring physics, animations, mouse handling
├── ViewModels/
│   ├── NotchViewModel.cs           # Observable state: media info, clock, calendar
│   └── RelayCommand.cs             # ICommand helper
├── Models/
│   ├── MediaInfo.cs                # Media session data model
│   └── CalendarDay.cs              # Calendar cell model
├── Services/
│   └── MediaService.cs             # Windows.Media.Control integration
└── Converters/
    ├── BoolToVisibilityConverter.cs
    └── NullToVisibilityConverter.cs
```

---

<img width="370" height="139" alt="image" src="https://github.com/user-attachments/assets/291e5e82-bfd6-47ef-8778-b7e756bfda44" /><img width="476" height="187" alt="image" src="https://github.com/user-attachments/assets/71b7415b-e7a8-4831-981e-2ce08baf4fbf" />
<img width="471" height="244" alt="Screenshot 2026-05-11 210512" src="https://github.com/user-attachments/assets/56efa076-9a52-403b-97fd-8110cc790b36" />
<img width="696" height="272" alt="Screenshot 2026-05-11 210533" src="https://github.com/user-attachments/assets/7af74ea9-0df9-4d25-86d3-0302c8e7d4f1" />

## 📜 License

MIT © Prasun Debnath
