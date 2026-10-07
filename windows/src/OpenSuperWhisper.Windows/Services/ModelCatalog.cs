using System.IO;
using System.Net.Http;

namespace OpenSuperWhisper.Services;

public sealed record WhisperModel(string FileName, string DisplayName, string Size, string Description)
{
    public string Url => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}?download=true";

    public string LocalPath => Path.Combine(AppPaths.ModelsDirectory, FileName);

    public bool IsDownloaded => File.Exists(LocalPath);

    /// <summary>".en" models only understand English.</summary>
    public bool IsEnglishOnly => FileName.Contains(".en.", StringComparison.Ordinal);
}

public static class ModelCatalog
{
    public static IReadOnlyList<WhisperModel> All { get; } =
    [
        new("ggml-large-v3-turbo-q5_0.bin", "Large v3 Turbo (Q5)", "547 MB", "Recommended. Near large-v3 quality, fast on a GPU."),
        new("ggml-large-v3-turbo-q8_0.bin", "Large v3 Turbo (Q8)", "834 MB", "Slightly more accurate than Q5."),
        new("ggml-large-v3-turbo.bin", "Large v3 Turbo", "1.6 GB", "Full precision turbo model."),
        new("ggml-large-v3.bin", "Large v3", "3.1 GB", "Most accurate, slowest. Needs a GPU with 4 GB+ memory."),
        new("ggml-medium.bin", "Medium", "1.5 GB", "Multilingual, mid-size."),
        new("ggml-small.bin", "Small", "466 MB", "Multilingual, good on CPU."),
        new("ggml-base.bin", "Base", "142 MB", "Multilingual, fast on CPU."),
        new("ggml-tiny.en.bin", "Tiny (English)", "75 MB", "English only, fastest."),
    ];

    public static WhisperModel? Find(string fileName) =>
        All.FirstOrDefault(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Downloads to a .part file and renames it once complete, so a cancelled download never looks finished.</summary>
    public static async Task DownloadAsync(WhisperModel model, IProgress<double> progress, CancellationToken ct)
    {
        var partPath = model.LocalPath + ".part";
        try
        {
            using var response = await Http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = File.Create(partPath))
            {
                var buffer = new byte[1 << 20];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    received += read;
                    if (total > 0)
                    {
                        progress.Report((double)received / total.Value);
                    }
                }
            }

            File.Move(partPath, model.LocalPath, overwrite: true);
            Log.Info($"Downloaded {model.FileName}");
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }
}
