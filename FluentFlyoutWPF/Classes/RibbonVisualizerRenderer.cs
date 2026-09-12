// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows.Media;

namespace FluentFlyoutWPF.Classes;

internal sealed class RibbonVisualizerRenderer : IVisualizerRenderer
{
    private const int RibbonCount = 3;
    private const int LobeCount = 3;
    private const int ControlPointCount = 7;
    private const byte BaseAlpha = 80;

    private static readonly Color[] RibbonColors =
    [
        Color.FromRgb(59, 130, 246),
        Color.FromRgb(34, 211, 238),
        Color.FromRgb(168, 85, 247)
    ];

    private static readonly float[] SpectrumOffsets = [-0.06f, 0f, 0.06f];
    private static readonly float[] LobeCenters = [0.18f, 0.50f, 0.82f];
    private static readonly float[] LobeWidths = [0.24f, 0.27f, 0.24f];
    private static readonly float[] LobeHeightScales = [0.105f, 0.13f, 0.105f];
    private static readonly float[] RibbonHeightScales = [1.00f, 0.92f, 0.84f];
    private static readonly float[] BaseThicknessScales = [0.024f, 0.022f, 0.020f];

    public void Render(
        Span<byte> buffer,
        int stride,
        int imageWidth,
        int imageHeight,
        ReadOnlySpan<float> amplitudes,
        in VisualizerRenderOptions options)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || amplitudes.Length == 0 || !HasAudio(amplitudes))
            return;

        Span<float> controlPoints = stackalloc float[ControlPointCount];
        Span<float> lobeHeights = stackalloc float[LobeCount];

        for (int ribbon = 0; ribbon < RibbonCount; ribbon++)
        {
            BuildAudioControlPoints(amplitudes, controlPoints, ribbon);
            BuildLobeHeights(controlPoints, lobeHeights, ribbon);
            DrawRibbon(
                buffer,
                stride,
                imageWidth,
                imageHeight,
                lobeHeights,
                RibbonColors[ribbon],
                ribbon);
        }
    }

    private static void BuildAudioControlPoints(
        ReadOnlySpan<float> amplitudes,
        Span<float> controlPoints,
        int ribbon)
    {
        for (int point = 0; point < ControlPointCount; point++)
        {
            float normalizedPosition = point / (ControlPointCount - 1f) + SpectrumOffsets[ribbon];
            controlPoints[point] = SampleSpectrum(amplitudes, normalizedPosition);
        }
    }

    private static void DrawRibbon(
        Span<byte> buffer,
        int stride,
        int imageWidth,
        int imageHeight,
        ReadOnlySpan<float> lobeHeights,
        Color color,
        int ribbon)
    {
        float centerY = imageHeight * 0.5f;

        for (int x = 0; x < imageWidth; x++)
        {
            float normalizedX = imageWidth == 1 ? 0f : x / (imageWidth - 1f);
            float halfHeight = CalculateHalfHeight(lobeHeights, normalizedX, ribbon, imageHeight);
            float top = centerY - halfHeight;
            float bottom = centerY + halfHeight;
            int firstY = Math.Max(0, (int)MathF.Floor(top));
            int endY = Math.Min(imageHeight, (int)MathF.Ceiling(bottom));

            for (int y = firstY; y < endY; y++)
            {
                float coverage = MathF.Min(bottom, y + 1f) - MathF.Max(top, y);
                if (coverage <= 0f)
                    continue;

                byte alpha = (byte)Math.Clamp(BaseAlpha * coverage, 0f, 255f);
                int index = y * stride + (x << 2);
                if (index + 3 >= buffer.Length)
                    continue;

                BlendPixel(buffer, index, color, alpha);
            }
        }
    }

    private static void BuildLobeHeights(
        ReadOnlySpan<float> controlPoints,
        Span<float> lobeHeights,
        int ribbon)
    {
        int ribbonIndex = NormalizeRibbonIndex(ribbon);

        for (int lobe = 0; lobe < LobeCount; lobe++)
        {
            float audioAmplitude = ClampUnit(SampleSmoothCurve(controlPoints, LobeCenters[lobe]));
            lobeHeights[lobe] = audioAmplitude * LobeHeightScales[lobe] * RibbonHeightScales[ribbonIndex];
        }
    }

    internal static float SampleHalfHeight(
        ReadOnlySpan<float> controlPoints,
        float normalizedX,
        int ribbon,
        int imageHeight)
    {
        Span<float> lobeHeights = stackalloc float[LobeCount];
        BuildLobeHeights(controlPoints, lobeHeights, ribbon);
        return CalculateHalfHeight(lobeHeights, normalizedX, ribbon, imageHeight);
    }

    private static float CalculateHalfHeight(
        ReadOnlySpan<float> lobeHeights,
        float normalizedX,
        int ribbon,
        int imageHeight)
    {
        int ribbonIndex = NormalizeRibbonIndex(ribbon);
        float halfHeight = BaseThicknessScales[ribbonIndex];

        for (int lobe = 0; lobe < LobeCount; lobe++)
            halfHeight += lobeHeights[lobe] * SampleLobeFalloff(normalizedX, lobe);

        return imageHeight * halfHeight * SampleEdgeEnvelope(normalizedX);
    }

    internal static float SampleEdgeEnvelope(float normalizedX)
    {
        normalizedX = ClampUnit(normalizedX);
        float centerWeight = 4f * normalizedX * (1f - normalizedX);
        return 0.72f + (0.28f * centerWeight);
    }

    internal static float SampleLobeFalloff(float normalizedX, int lobe)
    {
        int lobeIndex = Math.Clamp(lobe, 0, LobeCount - 1);
        normalizedX = ClampUnit(normalizedX);
        float distance = MathF.Abs(normalizedX - LobeCenters[lobeIndex]) / LobeWidths[lobeIndex];
        if (distance >= 1f)
            return 0f;

        float value = 1f - distance;
        return value * value * (3f - (2f * value));
    }

    internal static float SampleSpectrum(ReadOnlySpan<float> amplitudes, float normalizedX)
    {
        if (amplitudes.Length == 0)
            return 0f;

        if (amplitudes.Length == 1)
            return ClampUnit(amplitudes[0]);

        normalizedX = ClampUnit(normalizedX);
        float position = normalizedX * (amplitudes.Length - 1);
        int lower = (int)MathF.Floor(position);
        int upper = Math.Min(lower + 1, amplitudes.Length - 1);
        float fraction = position - lower;

        return ClampUnit(amplitudes[lower] + ((amplitudes[upper] - amplitudes[lower]) * fraction));
    }

    internal static float SampleSmoothCurve(ReadOnlySpan<float> points, float normalizedX)
    {
        if (points.Length == 0)
            return 0f;

        if (points.Length == 1)
            return ClampUnit(points[0]);

        normalizedX = ClampUnit(normalizedX);
        float position = normalizedX * (points.Length - 1);
        int segment = Math.Min((int)MathF.Floor(position), points.Length - 2);
        float t = position - segment;

        float p0 = points[Math.Max(0, segment - 1)];
        float p1 = points[segment];
        float p2 = points[segment + 1];
        float p3 = points[Math.Min(points.Length - 1, segment + 2)];

        float t2 = t * t;
        float t3 = t2 * t;
        float value = 0.5f * ((2f * p1)
            + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);

        return ClampUnit(value);
    }

    private static float ClampUnit(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 0f;

        return Math.Clamp(value, 0f, 1f);
    }

    private static bool HasAudio(ReadOnlySpan<float> amplitudes)
    {
        for (int i = 0; i < amplitudes.Length; i++)
        {
            if (amplitudes[i] > 0.001f)
                return true;
        }

        return false;
    }

    private static int NormalizeRibbonIndex(int ribbon)
    {
        return Math.Clamp(ribbon, 0, RibbonCount - 1);
    }

    private static void BlendPixel(Span<byte> buffer, int index, Color color, byte alpha)
    {
        if (alpha == 0)
            return;

        int sourceAlpha = alpha;
        int destinationAlpha = buffer[index + 3];

        if (destinationAlpha == 0)
        {
            buffer[index] = color.B;
            buffer[index + 1] = color.G;
            buffer[index + 2] = color.R;
            buffer[index + 3] = alpha;
            return;
        }

        int inverseSourceAlpha = 255 - sourceAlpha;
        int retainedDestinationAlpha = (destinationAlpha * inverseSourceAlpha + 127) / 255;
        int outputAlpha = sourceAlpha + retainedDestinationAlpha;
        if (outputAlpha == 0)
            return;

        buffer[index] = (byte)((color.B * sourceAlpha + buffer[index] * retainedDestinationAlpha + outputAlpha / 2) / outputAlpha);
        buffer[index + 1] = (byte)((color.G * sourceAlpha + buffer[index + 1] * retainedDestinationAlpha + outputAlpha / 2) / outputAlpha);
        buffer[index + 2] = (byte)((color.R * sourceAlpha + buffer[index + 2] * retainedDestinationAlpha + outputAlpha / 2) / outputAlpha);
        buffer[index + 3] = (byte)outputAlpha;
    }
}