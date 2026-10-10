using System.Runtime.CompilerServices;

namespace OpenSuperWhisper.Core.Tests;

internal static class TestEnvironment
{
    /// <summary>Points AppPaths at a throwaway folder before anything touches it.</summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        var dir = Path.Combine(Path.GetTempPath(), "osw-tests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("OSW_DATA_DIR", dir);
    }

    /// <summary>The repository root, which holds jfk.wav and ggml-tiny.en.bin.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "jfk.wav")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("Could not find the repository root (jfk.wav).");
    }

    /// <summary>Makes <paramref name="model"/> count as downloaded, linking the real file when one is given.</summary>
    public static void Install(WhisperModel model, string? source = null)
    {
        if (File.Exists(model.LocalPath))
        {
            return;
        }
        if (source is null)
        {
            File.WriteAllBytes(model.LocalPath, []);
        }
        else
        {
            File.CreateSymbolicLink(model.LocalPath, source);
        }
    }

    /// <summary>Reads a 16-bit PCM WAV file into floats.</summary>
    public static (float[] Samples, int SampleRate) ReadWav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var sampleRate = BitConverter.ToInt32(bytes, 24);
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            var size = BitConverter.ToInt32(bytes, offset + 4);
            if (chunkId == "data")
            {
                var samples = new float[size / 2];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, offset + 8 + i * 2) / 32768f;
                }
                return (samples, sampleRate);
            }
            offset += 8 + size;
        }
        throw new InvalidDataException("No data chunk.");
    }
}
