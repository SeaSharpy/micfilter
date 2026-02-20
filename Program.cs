using System;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

public static class Program
{
    static int TargetSampleRate = 16000;
    static int TargetBitDepth = 2;
    static float Gain = 50f;
    static bool EnableSystemOutput = false;

    public static void Main()
    {
        using var enumerator = new MMDeviceEnumerator();

        var input = FindCaptureDevice(enumerator, "Arctis Nova Pro Wireless");
        var cableOut = FindRenderDevice(enumerator, "CABLE Input");
        var systemOut = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        Console.WriteLine($"Input      : {input.FriendlyName}");
        Console.WriteLine($"VB-Cable   : {cableOut.FriendlyName}");
        Console.WriteLine($"System Out : {systemOut.FriendlyName}");
        Console.WriteLine("Press Enter to stop.");

        using var capture = new WasapiCapture(input);
        capture.ShareMode = AudioClientShareMode.Shared;

        var inputFormat = capture.WaveFormat;

        Console.WriteLine($"Input format: {inputFormat.SampleRate} Hz, {inputFormat.BitsPerSample} bit, {inputFormat.Channels} ch");

        var targetFormat = new WaveFormat(TargetSampleRate, 16, 1);

        var cableBuffer = new BufferedWaveProvider(targetFormat)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

        var systemBuffer = new BufferedWaveProvider(targetFormat)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

        using var cablePlayer = new WasapiOut(cableOut, AudioClientShareMode.Shared, false, 200);
        cablePlayer.Init(cableBuffer);

        WasapiOut? systemPlayer = null;
        if (EnableSystemOutput)
        {
            systemPlayer = new WasapiOut(systemOut, AudioClientShareMode.Shared, false, 200);
            systemPlayer.Init(systemBuffer);
        }

        var rand = new Random();

        double sampleRatio = (double)inputFormat.SampleRate / TargetSampleRate;
        double sampleAccumulator = 0;

        capture.DataAvailable += (_, e) =>
        {
            int bytesPerSample = inputFormat.BitsPerSample / 8;
            int frameSize = bytesPerSample * inputFormat.Channels;

            for (int i = 0; i < e.BytesRecorded; i += frameSize)
            {
                float sample = 0f;

                if (inputFormat.Encoding == WaveFormatEncoding.Pcm && inputFormat.BitsPerSample == 16)
                {
                    short s = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                    sample = s / 32768f;
                }
                else if (inputFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    sample = BitConverter.ToSingle(e.Buffer, i);
                }
                else
                {
                    continue;
                }

                sampleAccumulator += 1.0;

                if (sampleAccumulator >= sampleRatio)
                {
                    sampleAccumulator -= sampleRatio;

                    // Gain
                    sample *= Gain;

                    // Quantize to fake bit depth
                    sample = Quantize(sample, TargetBitDepth);

                    sample = Math.Clamp(sample, -1f, 1f);

                    short output = (short)(sample * 32767f);

                    byte lo = (byte)(output & 0xFF);
                    byte hi = (byte)((output >> 8) & 0xFF);

                    byte[] outBytes = { lo, hi };

                    cableBuffer.AddSamples(outBytes, 0, 2);

                    if (EnableSystemOutput)
                        systemBuffer.AddSamples(outBytes, 0, 2);
                }
            }
        };

        capture.StartRecording();
        cablePlayer.Play();
        systemPlayer?.Play();

        Console.ReadLine();

        capture.StopRecording();
        cablePlayer.Stop();
        systemPlayer?.Stop();
    }

    static float Quantize(float x, int bits)
    {
        bits = Math.Clamp(bits, 2, 16);
        int levels = 1 << bits;

        float scaled = (x + 1f) * 0.5f;
        scaled = MathF.Round(scaled * (levels - 1)) / (levels - 1);
        return scaled * 2f - 1f;
    }

    static MMDevice FindCaptureDevice(MMDeviceEnumerator enumerator, string nameContains)
    {
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
        return devices.First(d => d.FriendlyName.Contains(nameContains, StringComparison.OrdinalIgnoreCase));
    }

    static MMDevice FindRenderDevice(MMDeviceEnumerator enumerator, string nameContains)
    {
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        return devices.First(d => d.FriendlyName.Contains(nameContains, StringComparison.OrdinalIgnoreCase));
    }
}