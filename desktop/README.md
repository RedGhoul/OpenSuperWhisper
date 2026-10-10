# OpenSuperWhisper Desktop (Windows, Linux, macOS)

A cross-platform version of OpenSuperWhisper built with .NET 10 and [Avalonia UI](https://avaloniaui.net/).
Press a shortcut, speak, and the text is typed into whatever app you're in. Transcription runs locally with
[whisper.cpp](https://github.com/ggerganov/whisper.cpp) through [Whisper.net](https://github.com/sandrohanea/whisper.net),
on your GPU when you have one.

One codebase builds for all three platforms. The native macOS app in the repository root remains the most
complete option on a Mac.

This is an early version (0.2). It covers the core dictation loop; see [What's not ported yet](#whats-not-ported-yet).

## Features

- Lives in the system tray / menu bar
- Global shortcut, default **Alt + `** (Option + ` on macOS). Hold to record and release to transcribe; a quick tap
  starts a recording that the next press stops. **Esc** cancels.
- Pastes the text into the focused app (or only copies it), then restores your previous clipboard
- GPU acceleration, chosen automatically:

  | Platform | Backends tried in order |
  |---|---|
  | Windows | CUDA (NVIDIA) → Vulkan (NVIDIA, AMD, Intel) → CPU |
  | Linux | CUDA (NVIDIA) → Vulkan (NVIDIA, AMD, Intel) → CPU |
  | macOS | Metal on Apple Silicon (CPU on Intel Macs) |

- Model downloads from the settings window (Large v3 Turbo by default; same `ggml-*.bin` files as the macOS app)
- Language selection or auto-detect, microphone selection, initial prompt

## Requirements

- **Windows** 10 or 11 (x64), with the [Visual C++ Redistributable](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist)
- **Linux** x64, X11 session (see [Linux notes](#linux-notes))
- **macOS** on Apple Silicon; grant **Accessibility** permission when asked (needed for the global
  shortcut and for pasting)
- For CUDA: an NVIDIA driver plus the [CUDA Toolkit 13](https://developer.nvidia.com/cuda-downloads) (the CUDA build
  loads `cublas64_13` from it). Without it the app falls back to Vulkan, which needs only the normal GPU driver.

Published builds are self-contained, so .NET doesn't need to be installed.

## Building and running

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
cd desktop
dotnet run --project src/OpenSuperWhisper.Desktop
dotnet test
```

To build a self-contained folder for one platform:

```sh
dotnet publish src/OpenSuperWhisper.Desktop -c Release -r win-x64 --self-contained -o publish/win-x64
# or -r linux-x64, -r osx-arm64
```

Any platform can publish for any other. A publish for one platform keeps only that platform's whisper.cpp builds
(about 415 MB on Windows, most of it the CUDA build; 330 MB on Linux; 120 MB on macOS).

## Where things are stored

| Platform | Folder |
|---|---|
| Windows | `%LOCALAPPDATA%\OpenSuperWhisper` |
| macOS | `~/Library/Application Support/OpenSuperWhisper` |
| Linux | `~/.local/share/OpenSuperWhisper` |

It holds `models/`, `settings.json` and `log.txt` (model load times, the backend in use and transcription timings;
never the transcribed text).

## Linux notes

- The shortcut key also reaches the focused app on Linux: X11 doesn't let an app swallow a key it listens to
  globally. With the default Alt + `, some apps will type a `` ` ``. An F-key or a Ctrl combination avoids that.
- On Wayland, global shortcuts need libinput access (membership of the `input` group or equivalent). Under X11 they
  work without extra permissions.
- The recording indicator has square corners on desktops without a compositor.

## Code layout

| Path | Purpose |
|---|---|
| `src/OpenSuperWhisper.Core` | Platform-neutral logic, no UI. Unit-tested. |
| `  DictationController.cs` | The dictation flow: shortcut → record → transcribe → insert text |
| `  WhisperTranscriber.cs` | Whisper.net wrapper: backend selection, model loading, transcription |
| `  AudioRecorder.cs`, `Resampler.cs` | Microphone capture (PortAudio) and conversion to 16 kHz |
| `  HotkeyListener.cs`, `HotkeyMatcher.cs` | Global keyboard hook and key simulation (SharpHook), shortcut matching |
| `  ModelCatalog.cs`, `AppSettings.cs` | Model list and downloads, settings |
| `src/OpenSuperWhisper.Desktop` | Avalonia app: tray icon, settings window, recording indicator, clipboard |
| `tests/OpenSuperWhisper.Core.Tests` | Unit tests, including real transcription of `jfk.wav` with the tiny model |

## What's not ported yet

Compared with the macOS app, this version doesn't have:

- Recording history and search
- Drag-and-drop file transcription
- Parakeet engine (planned through ONNX Runtime)
- Asian-language autocorrect (the Rust `libautocorrect` builds on all three platforms; it still needs wiring up)
- Modifier-only and mouse-button triggers
- Installers and auto-update (MSIX/winget, `.app`/DMG, AppImage/Flatpak)
