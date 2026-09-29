# ReelWalk

A lightweight fullscreen slideshow for Windows and Linux. It plays a mix of photos and videos from the folders you choose.

Point it at a library, including a large one on a network share, and it starts showing files while the rest of the scan continues in the background. The next launch can show the last file immediately, then refresh the library.

## Features

- **Photos and videos in one playlist** — images and videos are scanned together
- **Wide format support** — JPEG, PNG, GIF, BMP, TIFF, and WebP. HEIC, HEIF, AVIF, and HD Photo are scanned and skipped when they cannot be decoded. Videos: MP4, M4V, WMV, AVI, MOV, MKV, and WebM
- **Fullscreen** — the window fills the screen. Escape quits.
- **Transitions** — Crossfade, MorphZoom, SoftWipe, ParallaxReveal, and ScaleDissolve, one chosen at random from the ones you leave enabled
- **Ken Burns** — slow pan and zoom on still images
- **Playback order** — Random, Newest-first, Oldest-first, or Sequential (by file path)
- **Folder play** — F opens a folder explorer on the folders around the current file. Click or Enter plays one. Right looks inside. Type to jump to a name. Random by default, then the previous playlist resumes
- **Large and slow libraries** — recursive scan, folders you can ignore, UNC paths, a saved file list (`ReelWalk.library`), and the last file restored on the next start
- **Video controls** — volume, a progress bar you can drag, and jumps of 5 or 30 seconds
- **No extra install on Windows** — `ReelWalk.exe` includes the .NET runtime and LibVLC

## Requirements

- Windows 10 or 11, or Linux x64
- Each publish is one file and includes the .NET 10 runtime. On Linux, install LibVLC from the distro (`libvlc`) so videos can play.

Only one copy of ReelWalk runs at a time.

## Installation

1. Copy `ReelWalk.exe` on Windows, or `ReelWalk` on Linux, to any location.
2. Run it. The first launch writes `ReelWalk.toml` next to the program and then exits.
3. Edit `ReelWalk.toml` and add your folder paths.
4. Run it again.

`ReelWalk.toml` and `ReelWalk.library` stay in that same folder.

## Configuration (`ReelWalk.toml`)

```toml
paths = [
    "C:\\Users\\Public\\Pictures",
    "\\\\server\\photos",
]

ignore = [
    "G:\\other\\Indexes",
]

[display]
duration = 8.0                 # seconds each photo stays on screen
transition_percent = 20.0
fit = "Contain"                # Contain = whole picture, Cover = fill and crop
video_volume = 1.00            # 0.00 mute, 1.00 full

[playback]
mode = "Random"                # Random | NewestFirst | OldestFirst | Sequential
show = "Both"                  # Both | Images | Videos
folder_mode = "Random"         # Random | Sequential — starting choice in the folder menu
folder_subfolders = true       # true also plays folders inside the chosen folder
history = 10                   # files Back remembers in Random; Forward returns along them
last_index = 0                 # written on exit
last_path = ""

[transitions]
enabled = [
    "Crossfade",
    "MorphZoom",
    "SoftWipe",
    "ParallaxReveal",
    "ScaleDissolve",
]
ken_burns = true
# ken_burns_duration = 10.0
# ken_burns_max_zoom = 1.3

[controls]
# Key names match Avalonia: Left, Right, PageUp, Space, Delete, F1.
# Prefix with Ctrl+, Shift+, or Alt+.
skip = 10
seek_seconds = 5
seek_fast_seconds = 30
next = "Right"
previous = "Left"
pause = "Space"
first = "Home"
last = "End"
skip_forward = "PageUp"
skip_back = "PageDown"
duration_up = "Up"
duration_down = "Down"
volume_up = "Ctrl+Up"
volume_down = "Ctrl+Down"
mute = "M"
seek_forward = "Ctrl+Right"
seek_back = "Ctrl+Left"
seek_forward_fast = "Ctrl+Shift+Right"
seek_back_fast = "Ctrl+Shift+Left"
random = "R"
newest = "N"
sequential = "S"
show = "V"
folder = "F"
ken_burns = "K"
fit = "W"
copy = "C"
info = "I"
help = "H"
help_alt = "F1"
open = "O"
delete = "Delete"
delete_now = "Ctrl+Delete"
close = "Escape"
explorer_up = "Up"
explorer_down = "Down"
explorer_home = "Home"
explorer_end = "End"
explorer_back = "Left"
explorer_open = "Right"
explorer_play = "Enter"
explorer_backspace = "Back"
```

Duration, video volume, fit, Ken Burns, playback mode, and folder-play settings are also updated when you change them from the keyboard. ReelWalk rewrites `ReelWalk.toml` when it exits.

## Controls

Click the slideshow so it has keyboard focus. Press **H** or **F1** for the same list on screen. **Escape** closes that panel, then the location menu, then the app. The keys below are the defaults. Each one is a setting under `[controls]` in `ReelWalk.toml`, so you can change it.

In Random, **Left** walks back through the last `history` files (10 unless you change it), and **Right** returns forward along that same trail before it picks a new file. Ordered playback still moves through the list.

### Navigation

| Key | Action |
|---|---|
| Right / Left, or mouse wheel | Next / previous file |
| Ctrl+wheel | Video volume up / down |
| Ctrl+Right / Ctrl+Left | Jump 5 seconds in the current video |
| Ctrl+Shift+Right / Ctrl+Shift+Left | Jump 30 seconds |
| Home / End | First / last file |
| Page Up / Page Down | Skip 10 files forward / back |

A burst of short video jumps lands once, on the time you stop at. Drag the bar along the bottom of a video and it plays from where you release.

### Playback

| Key | Action |
|---|---|
| Space, or middle-click | Pause / resume |
| Up / Down | Image display time +1s / −1s |
| Ctrl+Up / Ctrl+Down, or M | Video volume up / down, or mute (saved to `ReelWalk.toml`) |

### Modes

| Key | Action |
|---|---|
| R | Random |
| N | Newest first, using the date the photo was taken when the file has one |
| S | Sequential (sorted by path) |
| V | Photos and videos, photos only, or videos only. Saved as `show` in the toml. |
| F, or right-click | Folder explorer. It opens on the folders around the current file, with that folder marked Now. Click a folder or press Enter to play it. Right, or Open, looks inside a folder that has folders of its own. Type to jump to a name. Left goes back. Random is the starting choice. Subfolders plays nested folders too; This folder plays only the files in the chosen folder. Playback returns to the previous list when that folder finishes. The key is `folder` in the toml. |
| K | Ken Burns on / off |

### Display and files

| Key | Action |
|---|---|
| W | Whole picture, or fill the screen |
| C | Copy the current file path |
| I | File info overlay |
| H or F1 | Keyboard help |
| O | Show the current file in the file browser |
| Delete | Delete the current file after confirmation |
| Ctrl+Delete | Delete the current file without asking |
| Escape | Exit |

The path in the top-left corner opens the same folder menu. A photo that cannot be opened is skipped. New files in the configured folders are picked up on their own. Random playback avoids the files it just showed. Right-click the tray icon for pause, next, previous, folders, edit config, and exit.

## Video

Videos play to the end and ignore `duration`. Volume comes from `video_volume`. A thin progress bar and elapsed / remaining time appear while a video is playing. Left and Right always change files; seeking uses Ctrl, or a click and drag on the bar.

## Building

From the repository root, with the .NET 10 SDK installed (Git Bash, WSL, or Linux):

```
./build.sh
```

That publishes both self-contained binaries into `build/` (separate folders so Windows does not treat `ReelWalk` and `ReelWalk.exe` as the same file):

- `build/win-x64/ReelWalk.exe` — Windows x64
- `build/linux-x64/ReelWalk` — Linux x64

Build only one target:

```
./build.sh --windows
./build.sh --linux
```

`bin/` and `obj/` under `src/ReelWalk/` are removed after a successful publish. Copy the file for the system you are on. `ReelWalk.toml` and `ReelWalk.library` stay beside it. The Windows file unpacks LibVLC on first launch. The Linux file uses the LibVLC installed on that machine.

## License

MIT License

Copyright (c) 2026

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
