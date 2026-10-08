namespace OpenSuperWhisper.Core.Tests;

public class ResamplerTests
{
    [Fact]
    public void Leaves16kAudioUnchanged()
    {
        float[] input = [0.1f, -0.2f, 0.3f];
        Assert.Equal(input, Resampler.To16k(input, 16000));
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [InlineData(8000)]
    public void KeepsDurationAndPitch(int inputRate)
    {
        const double frequency = 440;
        var input = Sine(frequency, inputRate, seconds: 1);

        var output = Resampler.To16k(input, inputRate);

        Assert.InRange(output.Length, 15990, 16000);
        Assert.InRange(ZeroCrossings(output) / 2.0, frequency - 3, frequency + 3);
    }

    [Fact]
    public void FiltersToneAboveNyquistWhenDownsampling()
    {
        // 15 kHz can't exist at 16 kHz; without filtering it would alias to an audible 1 kHz tone.
        var output = Resampler.To16k(Sine(15000, 48000, seconds: 1), 48000);

        var rms = Math.Sqrt(output.Average(s => s * s));
        Assert.True(rms < 0.15, $"Aliased energy too high: RMS {rms:F3}");
    }

    private static float[] Sine(double frequency, int rate, double seconds) =>
        Enumerable.Range(0, (int)(rate * seconds))
            .Select(i => (float)Math.Sin(2 * Math.PI * frequency * i / rate))
            .ToArray();

    private static int ZeroCrossings(float[] samples)
    {
        var count = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] < 0 != samples[i] < 0)
            {
                count++;
            }
        }
        return count;
    }
}
