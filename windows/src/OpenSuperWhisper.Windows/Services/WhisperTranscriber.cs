using System.Text;
using System.Text.RegularExpressions;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace OpenSuperWhisper.Services;

/// <summary>Runs whisper.cpp through Whisper.net on the GPU when one is available.</summary>
public sealed partial class WhisperTranscriber : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private WhisperFactory? _factory;
    private string? _loadedModelPath;

    /// <summary>
    /// Chooses which native whisper.cpp build to load. Whisper.net loads the native library once per process,
    /// so this only has an effect before the first model is loaded; changing it later needs a restart.
    /// </summary>
    public static void ConfigureRuntime(GpuBackend backend)
    {
        RuntimeOptions.RuntimeLibraryOrder = backend switch
        {
            GpuBackend.Cuda => [RuntimeLibrary.Cuda, RuntimeLibrary.Cpu],
            GpuBackend.Vulkan => [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu],
            GpuBackend.Cpu => [RuntimeLibrary.Cpu],
            _ => [RuntimeLibrary.Cuda, RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu],
        };
    }

    /// <summary>"CUDA", "Vulkan" or "CPU" once a model is loaded, otherwise null.</summary>
    public string? ActiveBackend => RuntimeOptions.LoadedLibrary switch
    {
        null => null,
        RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12 => "CUDA (NVIDIA GPU)",
        RuntimeLibrary.Vulkan => "Vulkan (GPU)",
        RuntimeLibrary.Cpu or RuntimeLibrary.CpuNoAvx => "CPU",
        var other => other.ToString(),
    };

    public bool IsLoaded(string modelPath) => _factory is not null && _loadedModelPath == modelPath;

    /// <summary>Loads the model if it is not loaded yet. Safe to call repeatedly.</summary>
    public async Task LoadAsync(string modelPath)
    {
        await _lock.WaitAsync();
        try
        {
            await Task.Run(() => LoadLocked(modelPath));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> TranscribeAsync(float[] samples, AppSettings settings, WhisperModel model,
        CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await Task.Run(() => LoadLocked(model.LocalPath), ct);

            var language = model.IsEnglishOnly ? "en" : settings.Language;
            var builder = _factory!.CreateBuilder()
                .WithLanguage(language)
                .WithNoContext()
                .WithTemperature(0f)
                .WithNoSpeechThreshold(0.6f);
            if (!string.IsNullOrWhiteSpace(settings.InitialPrompt))
            {
                builder = builder.WithPrompt(settings.InitialPrompt);
            }

            await using var processor = builder.Build();

            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(PadToMinimum(samples), ct))
            {
                text.Append(segment.Text);
            }
            return Clean(text.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    private void LoadLocked(string modelPath)
    {
        if (_factory is not null && _loadedModelPath == modelPath)
        {
            return;
        }

        _factory?.Dispose();
        _factory = null;

        var started = DateTime.UtcNow;
        _factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = true });
        _loadedModelPath = modelPath;
        Log.Info($"Loaded {modelPath} on {ActiveBackend} in {(DateTime.UtcNow - started).TotalSeconds:F1}s");
    }

    /// <summary>whisper.cpp tends to hallucinate on clips under a second; pad with silence.</summary>
    private static float[] PadToMinimum(float[] samples)
    {
        var minimum = AudioRecorder.SampleRate + AudioRecorder.SampleRate / 10;
        if (samples.Length >= minimum)
        {
            return samples;
        }
        var padded = new float[minimum];
        samples.CopyTo(padded, 0);
        return padded;
    }

    private static string Clean(string text)
    {
        text = NonSpeechTag().Replace(text, " ");
        text = Whitespace().Replace(text, " ");
        return text.Trim();
    }

    // [BLANK_AUDIO], [MUSIC], (silence), ... that whisper emits for non-speech.
    [GeneratedRegex(@"\[[^\]]*\]|\((?:silence|music|applause|laughter|inaudible)[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex NonSpeechTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public void Dispose()
    {
        _factory?.Dispose();
        _lock.Dispose();
    }
}
