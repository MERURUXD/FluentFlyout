// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows.Media;

namespace FluentFlyoutWPF.Classes;

internal sealed class RibbonVisualizerRenderer : IVisualizerRenderer
{
    private const int RibbonCount = 3;
    private const int ControlPointCount = 5;
    private const byte BaseAlpha = 80;

    private static readonly Color[] RibbonColors =
    [
        Color.FromRgb(59, 130, 246),
        Color.FromRgb(34, 211, 238),
        Color.FromRgb(168, 85, 247)
    ];

    private static readonly float[] SourceOffsets = [-0.055f, 0f, 0.055f];
    private static readonly float[] PhaseOffsets = [0f, 1.7f, 3.4f];
    private static readonly float[] PhaseSpeeds = [0.48f, 0.38f, 0.56f];
    private static readonly float[] HeightScales = [0.31f, 0.28f, 0.30f];

    public void Render(
        Span<byte> buffer,
        int stride,
        int imageWidth,
        int imageHeight,
        ReadOnlySpan<float> amplitudes,
        in VisualizerRenderOptions options)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || amplitudes.Length == 0)
            return;

        Span<float> controlPoints = stackalloc float[ControlPointCount];

        for (int ribbon = 0; ribbon < RibbonCount; ribbon++)
        {
            BuildControlPoints(amplitudes, controlPoints, ribbon, options.ElapsedSeconds);
            DrawRibbon(
                buffer,
                stride,
                imageWidth,
                imageHeight,
                controlPoints,
                RibbonColors[ribbon],
                HeightScales[ribbon]);
        }
    }

    private static void BuildControlPoints(
        ReadOnlySpan<float> amplitudes,
        Span<float> controlPoints,
        int ribbon,
        double elapsedSeconds)
    {
        for (int point = 0; point < ControlPointCount; point++)
        {
            float normalizedPosition = point / (ControlPointCount - 1f) + SourceOffsets[ribbon];
            float audioAmplitude = SampleSpectrum(amplitudes, normalizedPosition);

            float phase = (float)(elapsedSeconds * PhaseSpeeds[ribbon])
                + PhaseOffsets[ribbon]
                + point * 0.85f;
            float proceduralMotion = 0.5f + 0.5f * MathF.Sin(phase);

            // Keep the procedural part subordinate to audio and unable to create
            // a visible waveform by itself during silence.
            controlPoints[point] = ClampUnit(
                (audioAmplitude * 0.80f) + (audioAmplitude * 0.20f * proceduralMotion));
        }
    }

    private static void DrawRibbon(
        Span<byte> buffer,
        int stride,
        int imageWidth,
        int imageHeight,
        ReadOnlySpan<float> controlPoints,
        Color color,
        float heightScale)
    {
        float centerY = imageHeight * 0.5f;

        for (int x = 0; x < imageWidth; x++)
        {
            float normalizedX = imageWidth == 1 ? 0f : x / (imageWidth - 1f);
            float envelope = MathF.Sin(MathF.PI * normalizedX);
            envelope *= envelope;
            float amplitude = ClampUnit(SampleSmoothCurve(controlPoints, normalizedX) * envelope);
            float halfHeight = amplitude * imageHeight * heightScale;

            if (halfHeight <= 0f)
                continue;

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