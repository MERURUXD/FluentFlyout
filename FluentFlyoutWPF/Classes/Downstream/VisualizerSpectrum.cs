// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using NAudio.Dsp;
using NAudio.Wave;

namespace FluentFlyoutWPF.Classes.Downstream;

internal enum VisualizerAudioSource
{
    Desktop,
    Microphone,
    Both
}

internal static class VisualizerSourcePolicy
{
    public static int Normalize(int value) => value is >= 0 and <= 2 ? value : 0;

    public static bool Includes(int mode, int source)
        => Normalize(mode) == (int)VisualizerAudioSource.Both || Normalize(mode) == source;
}

/// <summary>One endpoint's sample clock and FFT. No interleaved channels enter the FFT.</summary>
internal sealed class VisualizerSpectrum
{
    private const int FftLength = 4096;
    private static readonly Guid Pcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid Float = new("00000003-0000-0010-8000-00aa00389b71");
    private readonly WaveFormat _format;
    private readonly bool _float;
    private readonly int _sampleBytes;
    private readonly Complex[] _fft = new Complex[FftLength];
    private readonly byte[] _partialFrame;
    private int _partialCount;
    private int _position;

    public VisualizerSpectrum(WaveFormat format)
    {
        _format = format;
        var encoding = format.Encoding;
        if (format is WaveFormatExtensible extensible)
            encoding = extensible.SubFormat == Pcm ? WaveFormatEncoding.Pcm
                : extensible.SubFormat == Float ? WaveFormatEncoding.IeeeFloat : WaveFormatEncoding.Unknown;
        _float = encoding == WaveFormatEncoding.IeeeFloat;
        _sampleBytes = format.BitsPerSample / 8;
        if (format.Channels < 1 || format.SampleRate < 1
            || format.BlockAlign != _sampleBytes * format.Channels
            || !(_float && format.BitsPerSample == 32
                || encoding == WaveFormatEncoding.Pcm && format.BitsPerSample is 8 or 16 or 24 or 32))
            throw new NotSupportedException($"Unsupported visualizer audio format: {format}");
        _partialFrame = new byte[format.BlockAlign];
    }

    public void Reset()
    {
        _position = 0;
        _partialCount = 0;
    }

    public float[]? Add(byte[] buffer, int count, int barCount)
    {
        float[]? result = null;
        int offset = 0;
        while (offset < count)
        {
            int copied = Math.Min(_partialFrame.Length - _partialCount, count - offset);
            Buffer.BlockCopy(buffer, offset, _partialFrame, _partialCount, copied);
            offset += copied;
            _partialCount += copied;
            if (_partialCount != _partialFrame.Length)
                continue;
            _partialCount = 0;
            double sample = 0;
            for (int channel = 0; channel < _format.Channels; channel++)
                sample += Decode(_partialFrame, channel * _sampleBytes);
            _fft[_position].X = (float)(sample / _format.Channels * FastFourierTransform.HammingWindow(_position, FftLength));
            _fft[_position].Y = 0;
            if (++_position < FftLength)
                continue;
            _position = 0;
            FastFourierTransform.FFT(true, 12, _fft);
            result = GetBands(barCount);
        }
        return result;
    }

    private float Decode(byte[] buffer, int offset)
    {
        float value;
        if (_float)
            value = BitConverter.ToSingle(buffer, offset);
        else
            value = _sampleBytes switch
            {
                1 => (buffer[offset] - 128) / 128f,
                2 => BitConverter.ToInt16(buffer, offset) / 32768f,
                3 => ((buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16) << 8 >> 8) / 8388608f,
                4 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
                _ => 0
            };
        return float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0;
    }

    private float[] GetBands(int count)
    {
        float[] bands = new float[count];
        double frequencyPerBin = (double)_format.SampleRate / FftLength;
        for (int i = 0; i < count; i++)
        {
            int start = Math.Clamp((int)(40 * Math.Pow(200, (double)i / count) / frequencyPerBin), 0, FftLength / 2);
            int end = Math.Clamp((int)(40 * Math.Pow(200, (double)(i + 1) / count) / frequencyPerBin), start, FftLength / 2);
            if (end == start && start < FftLength / 2)
                end++;
            for (int bin = start; bin < end; bin++)
                bands[i] = Math.Max(bands[i], MathF.Sqrt(_fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y));
        }
        return bands;
    }

    public static float[] Merge(float[]? desktop, float[]? microphone, int count, int sensitivity, int peak)
    {
        float minDb = sensitivity * -10f - 30f;
        float maxDb = peak * 10f - 30f;
        var result = new float[count];
        for (int i = 0; i < count; i++)
        {
            float amplitude = Math.Max(desktop?.ElementAtOrDefault(i) ?? 0, microphone?.ElementAtOrDefault(i) ?? 0);
            amplitude *= 1 + (float)i / count * 75;
            result[i] = amplitude <= 0 ? 0 : Math.Clamp((20 * MathF.Log10(Math.Max(amplitude, 0.001f)) - minDb) / (maxDb - minDb), 0, 1);
        }
        return result;
    }
}