namespace OpenSuperWhisper.Core;

public static class Resampler
{
    public const int WhisperSampleRate = 16000;

    /// <summary>
    /// Converts mono audio to 16 kHz. Each output sample averages the input samples it covers, which acts as the
    /// low-pass filter downsampling needs (48 kHz mics would otherwise alias); upsampling interpolates linearly.
    /// </summary>
    public static float[] To16k(ReadOnlySpan<float> input, double inputRate)
    {
        if (Math.Abs(inputRate - WhisperSampleRate) < 0.5)
        {
            return input.ToArray();
        }
        if (input.Length == 0)
        {
            return [];
        }

        var ratio = inputRate / WhisperSampleRate;
        var output = new float[(int)(input.Length / ratio)];

        for (var i = 0; i < output.Length; i++)
        {
            if (ratio > 1)
            {
                var start = (int)(i * ratio);
                var end = Math.Min(input.Length, (int)((i + 1) * ratio));
                var sum = 0f;
                for (var j = start; j < end; j++)
                {
                    sum += input[j];
                }
                output[i] = end > start ? sum / (end - start) : input[Math.Min(start, input.Length - 1)];
            }
            else
            {
                var position = i * ratio;
                var index = (int)position;
                var next = Math.Min(index + 1, input.Length - 1);
                var fraction = (float)(position - index);
                output[i] = input[index] + (input[next] - input[index]) * fraction;
            }
        }
        return output;
    }
}
