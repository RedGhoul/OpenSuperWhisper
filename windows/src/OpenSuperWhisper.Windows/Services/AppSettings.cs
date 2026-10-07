using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSuperWhisper.Services;

public enum GpuBackend
{
    /// <summary>Try CUDA, then Vulkan, then fall back to the CPU.</summary>
    Auto,
    Cuda,
    Vulkan,
    Cpu,
}

public sealed class AppSettings
{
    /// <summary>File name inside <see cref="AppPaths.ModelsDirectory"/>.</summary>
    public string ModelFileName { get; set; } = "ggml-large-v3-turbo-q5_0.bin";

    /// <summary>Whisper language code, or "auto" to detect.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>WinMM device number; -1 is the Windows default microphone.</summary>
    public int MicrophoneDevice { get; set; } = -1;

    public Hotkey RecordHotkey { get; set; } = Hotkey.Default;

    /// <summary>Hold the hotkey to record and release to stop; otherwise press once to start and again to stop.</summary>
    public bool HoldToRecord { get; set; } = true;

    public GpuBackend GpuBackend { get; set; } = GpuBackend.Auto;

    /// <summary>Paste the text into the focused app; otherwise only copy it to the clipboard.</summary>
    public bool AutoPaste { get; set; } = true;

    public bool AddTrailingSpace { get; set; } = true;

    public string InitialPrompt { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions)
                       ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings, using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}
