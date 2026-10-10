namespace OpenSuperWhisper.Core.Tests;

/// <summary>Runs the real whisper.cpp on the tiny model that ships in the repository.</summary>
public class TranscriptionTests
{
    [Fact]
    public async Task TranscribesJfkSample()
    {
        var model = ModelCatalog.Find("ggml-tiny.en.bin")!;
        TestEnvironment.Install(model, Path.Combine(TestEnvironment.RepoRoot, "ggml-tiny.en.bin"));
        var (samples, rate) = TestEnvironment.ReadWav(Path.Combine(TestEnvironment.RepoRoot, "jfk.wav"));
        Assert.Equal(Resampler.WhisperSampleRate, rate);

        using var transcriber = new WhisperTranscriber();
        // Language "de" must be ignored for an English-only model.
        var text = await transcriber.TranscribeAsync(samples, new AppSettings { Language = "de" }, model,
            CancellationToken.None);

        Assert.Contains("ask not what your country can do for you", text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(transcriber.ActiveBackend);
    }

    [Fact]
    public async Task ResampledAudioStillTranscribes()
    {
        // Simulates a 48 kHz microphone: upsample the clip, then let the app's resampler bring it back.
        var model = ModelCatalog.Find("ggml-tiny.en.bin")!;
        TestEnvironment.Install(model, Path.Combine(TestEnvironment.RepoRoot, "ggml-tiny.en.bin"));
        var (samples, _) = TestEnvironment.ReadWav(Path.Combine(TestEnvironment.RepoRoot, "jfk.wav"));
        var at48k = new float[samples.Length * 3];
        for (var i = 0; i < at48k.Length; i++)
        {
            at48k[i] = samples[Math.Min(i / 3, samples.Length - 1)];
        }

        using var transcriber = new WhisperTranscriber();
        var text = await transcriber.TranscribeAsync(Resampler.To16k(at48k, 48000), new AppSettings(), model,
            CancellationToken.None);

        Assert.Contains("fellow americans", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(" [BLANK_AUDIO] ", "")]
    [InlineData(" Hello   world. ", "Hello world.")]
    [InlineData("(silence) Okay [MUSIC]", "Okay")]
    public void CleanRemovesNonSpeechTags(string raw, string expected) =>
        Assert.Equal(expected, WhisperTranscriber.Clean(raw));

    [Fact]
    public void ShortClipsArePaddedToOverASecond()
    {
        Assert.Equal(17600, WhisperTranscriber.PadToMinimum(new float[100]).Length);
        Assert.Equal(20000, WhisperTranscriber.PadToMinimum(new float[20000]).Length);
    }
}
