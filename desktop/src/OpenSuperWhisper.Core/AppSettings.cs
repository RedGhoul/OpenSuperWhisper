using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSuperWhisper.Core;

public enum GpuBackend
{
    /// <summary>Try CUDA, then Vulkan, then the CPU build (which uses Metal on Apple Silicon).</summary>
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

    /// <summary>Microphone name as PortAudio reports it; null is the system default.</summary>
    public string? Microphone { get; set; }

    public Hotkey RecordHotkey { get; set; } = Hotkey.Default;

    /// <summary>Hold the shortcut to record and release to stop; otherwise press once to start and again to stop.</summary>
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

    public static AppSettings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings, using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        try
        {
            File.WriteAllText(path ?? AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}
