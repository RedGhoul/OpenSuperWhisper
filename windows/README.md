# OpenSuperWhisper for Windows

A Windows port of OpenSuperWhisper: press a shortcut, speak, and the text is typed into whatever app you're in.
Transcription runs locally with [whisper.cpp](https://github.com/ggerganov/whisper.cpp) through
[Whisper.net](https://github.com/sandrohanea/whisper.net), on your GPU when you have one.

This is an early version (0.1). It covers the core dictation loop; see [What's not ported yet](#whats-not-ported-yet).

## Features

- Lives in the system tray
- Global shortcut, default **Alt + `**. Hold to record and release to transcribe, or tap once to start and again to stop
- **Esc** cancels a recording or transcription
- Pastes the text into the focused app (or only copies it to the clipboard), then restores your previous clipboard
- GPU acceleration:
  - **CUDA** for NVIDIA GPUs
  - **Vulkan** for NVIDIA, AMD and Intel GPUs
  - falls back to the **CPU** automatically
- Model downloads from the settings window (Large v3 Turbo by default)
- Language selection or auto-detect, microphone selection, initial prompt

## Requirements

- Windows 10 or 11, x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Microsoft Visual C++ Redistributable](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) (x64)
- For the GPU, one of:
  - **NVIDIA with CUDA**: a recent NVIDIA driver plus the [CUDA Toolkit 13](https://developer.nvidia.com/cuda-downloads). The bundled CUDA build loads `cublas64_13.dll` from the toolkit, which the installer puts on the `PATH`.
  - **Vulkan (any vendor)**: only an up-to-date GPU driver. This is the zero-setup option; on NVIDIA it is usually a bit slower than CUDA.

With **Automatic** (the default), the app tries CUDA, then Vulkan, then the CPU, and uses the first one that loads.
Settings → GPU shows which one is active, and every transcription is timed in the log.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Visual Studio 2026 or Rider is optional.

```powershell
cd windows
dotnet build -c Release
dotnet run --project src/OpenSuperWhisper.Windows -c Release
```

To produce a folder you can copy to another machine:

```powershell
dotnet publish src/OpenSuperWhisper.Windows -c Release -o publish
```

The project also compiles on Linux and macOS (`EnableWindowsTargeting`), but it only runs on Windows.

## Where things are stored

`%LOCALAPPDATA%\OpenSuperWhisper\` holds:

- `models\` (the downloaded `ggml-*.bin` files, the same format the macOS app uses)
- `settings.json`
- `log.txt` (model load times, the backend in use, and transcription timings)

## Troubleshooting

- **Runs on the CPU although you have an NVIDIA GPU:** install the CUDA Toolkit, or switch Settings → GPU to Vulkan.
  Changing the backend takes effect after a restart.
- **Nothing gets pasted into one particular app:** if that app runs as administrator, Windows blocks input from
  non-elevated apps. The text is still on the clipboard.
- **The shortcut doesn't work:** another app may already own it. The tray shows a warning at startup; pick a
  different shortcut in Settings.

## Code layout

| Path | Purpose |
|---|---|
| `AppController.cs` | Tray icon and the dictation flow: shortcut → record → transcribe → paste |
| `Services/WhisperTranscriber.cs` | Whisper.net wrapper: runtime/GPU selection, model loading, transcription |
| `Services/AudioRecorder.cs` | Microphone capture at 16 kHz mono (NAudio / WinMM) |
| `Services/HotkeyManager.cs` | Global shortcuts (`RegisterHotKey`) and hold-to-record release detection |
| `Services/TextInserter.cs` | Clipboard and `SendInput` Ctrl+V |
| `Services/ModelCatalog.cs` | Model list and downloads from Hugging Face |
| `Views/IndicatorWindow` | Non-focus-stealing "Listening / Transcribing" pill |
| `Views/SettingsWindow` | Settings UI |

## What's not ported yet

Compared with the macOS app, this version doesn't have:

- Recording history and search
- Drag-and-drop file transcription
- Parakeet engine (planned through ONNX Runtime / Windows ML)
- Asian-language autocorrect (the Rust `libautocorrect` builds on Windows; it still needs wiring up)
- Modifier-only and mouse-button triggers
- An installer and auto-update (MSIX or winget)
