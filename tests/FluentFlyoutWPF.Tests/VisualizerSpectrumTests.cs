// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes.Downstream;
using FluentFlyoutWPF.ViewModels;
using NAudio.Wave;
using System.Xml.Serialization;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class VisualizerSpectrumTests
{
    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, false, true)]
    [InlineData(2, true, true)]
    [InlineData(-1, true, false)]
    [InlineData(99, true, false)]
    public void SourcePolicySelectsOnlyRequestedEndpoints(int mode, bool desktop, bool microphone)
    {
        Assert.Equal(desktop, VisualizerSourcePolicy.Includes(mode, 0));
        Assert.Equal(microphone, VisualizerSourcePolicy.Includes(mode, 1));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("<TaskbarVisualizerAudioSource>1</TaskbarVisualizerAudioSource>", 1)]
    [InlineData("<TaskbarVisualizerAudioSource>2</TaskbarVisualizerAudioSource>", 2)]
    [InlineData("<TaskbarVisualizerAudioSource>999</TaskbarVisualizerAudioSource>", 0)]
    [InlineData("<TaskbarVisualizerAudioSource>-1</TaskbarVisualizerAudioSource>", 0)]
    public void SettingsRoundTripAndDefaultWithoutPersistingRuntimeStatus(string element, int expected)
    {
        var serializer = new XmlSerializer(typeof(UserSettings));
        using var reader = new StringReader($"<UserSettings>{element}</UserSettings>");
        var settings = Assert.IsType<UserSettings>(serializer.Deserialize(reader));
        Assert.Equal(expected, settings.TaskbarVisualizerAudioSource);
        settings.TaskbarVisualizerSourceStatus = "Transient failure";
        using var writer = new StringWriter();
        serializer.Serialize(writer, settings);
        Assert.Contains($"<TaskbarVisualizerAudioSource>{expected}</TaskbarVisualizerAudioSource>", writer.ToString());
        Assert.DoesNotContain("TaskbarVisualizerSourceStatus", writer.ToString());
    }

    [Theory]
    [InlineData(16000, 8, 1, false)]
    [InlineData(16000, 16, 1, false)]
    [InlineData(44100, 24, 2, false)]
    [InlineData(48000, 32, 2, false)]
    [InlineData(44100, 32, 1, true)]
    [InlineData(48000, 32, 2, true)]
    public void FormatsAndSampleRatesMapToneToTheSameFrequencyBand(int rate, int bits, int channels, bool floating)
    {
        var format = floating ? WaveFormat.CreateIeeeFloatWaveFormat(rate, channels) : new WaveFormat(rate, bits, channels);
        var data = Tone(format, 1000);
        var bands = Assert.IsType<float[]>(new VisualizerSpectrum(format).Add(data, data.Length, 10));
        int expectedBand = (int)(Math.Log(1000d / 40) / Math.Log(200) * 10);
        Assert.Equal(expectedBand, Array.IndexOf(bands, bands.Max()));
        Assert.InRange(bands.Max(), 0.09f, 0.15f);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void ExtensibleSubformatControlsSampleDecoding(int bits)
    {
        var format = new WaveFormatExtensible(48000, bits, 2);
        var input = format.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71")
            ? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2) : new WaveFormat(48000, bits, 2);
        var data = Tone(input, 1000);
        var bands = Assert.IsType<float[]>(new VisualizerSpectrum(format).Add(data, data.Length, 10));
        Assert.InRange(bands.Max(), 0.09f, 0.15f);
    }

    [Fact]
    public void SplitFramesAndOppositeStereoChannelsAreHandledPerFrame()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var data = Tone(format, 1000);
        var split = new VisualizerSpectrum(format);
        Assert.Null(split.Add(data[..7], 7, 10));
        var actual = split.Add(data[7..], data.Length - 7, 10);
        Assert.Equal(new VisualizerSpectrum(format).Add(data, data.Length, 10), actual);
        for (int offset = 0; offset < data.Length; offset += 8)
            BitConverter.GetBytes(-BitConverter.ToSingle(data, offset)).CopyTo(data, offset + 4);
        Assert.All(new VisualizerSpectrum(format).Add(data, data.Length, 10)!, value => Assert.Equal(0, value));
    }

    [Fact]
    public void LowSampleRateAndNonfiniteFloatInputStayFiniteAndSilent()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(8000, 1);
        var data = Enumerable.Repeat(BitConverter.GetBytes(float.NaN), 4096).SelectMany(bytes => bytes).ToArray();
        var bands = new VisualizerSpectrum(format).Add(data, data.Length, 20)!;
        Assert.All(bands, value => Assert.Equal(0, value));
        Assert.All(VisualizerSpectrum.Merge(bands, null, 20, 3, 1), value => Assert.Equal(0, value));
    }

    [Fact]
    public void MergeTakesTheStrongerSourceInEachBandWithoutBoostingDuplicates()
    {
        float[] desktop = [0.01f, 0.001f];
        float[] microphone = [0.001f, 0.01f];
        var left = VisualizerSpectrum.Merge(desktop, null, 2, 2, 3);
        var right = VisualizerSpectrum.Merge(null, microphone, 2, 2, 3);
        var both = VisualizerSpectrum.Merge(desktop, microphone, 2, 2, 3);
        Assert.Equal(Math.Max(left[0], right[0]), both[0]);
        Assert.Equal(Math.Max(left[1], right[1]), both[1]);
        Assert.Equal(left, VisualizerSpectrum.Merge(desktop, desktop, 2, 2, 3));
    }

    [Fact]
    public void UnsupportedEncodingFailsBeforeCaptureStarts()
    {
        Assert.Throws<NotSupportedException>(() => new VisualizerSpectrum(WaveFormat.CreateALawFormat(8000, 1)));
    }

    internal static byte[] Tone(WaveFormat format, double frequency, double amplitude = 0.5)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        for (int frame = 0; frame < 4096; frame++)
        {
            double sample = amplitude * Math.Sin(2 * Math.PI * frequency * frame / format.SampleRate);
            for (int channel = 0; channel < format.Channels; channel++)
            {
                if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                    writer.Write((float)sample);
                else if (format.BitsPerSample == 8)
                    writer.Write((byte)(sample * 127 + 128));
                else if (format.BitsPerSample == 16)
                    writer.Write((short)(sample * short.MaxValue));
                else if (format.BitsPerSample == 24)
                {
                    int value = (int)(sample * 8388607);
                    writer.Write((byte)value);
                    writer.Write((byte)(value >> 8));
                    writer.Write((byte)(value >> 16));
                }
                else
                    writer.Write((int)(sample * int.MaxValue));
            }
        }
        return stream.ToArray();
    }
}
