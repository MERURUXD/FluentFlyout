// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows.Media;

namespace FluentFlyoutWPF.Classes;

// The iOS 9 Siri waveform state and curve math in this renderer is adapted from:
// - https://github.com/halildurmus/siri_wave (BSD-3-Clause)
// - https://github.com/kopiro/siriwave (MIT)
// - https://github.com/mapo80/SiriWave (MIT)
// The FluentFlyout palette, bitmap rasterization and audio-amplitude adapter remain
// downstream-specific. The referenced projects retain their original notices.
internal sealed class RibbonVisualizerRenderer : IVisualizerRenderer
{
    private const int RibbonCount = 3;
    private const int MinimumCurveCount = 2;
    private const int MaximumCurveCount = 5;
    private const float GraphX = 25f;
    private const float AmplitudeFactor = 0.8f;
    private const float AttenuationFactor = 4f;
    private const float DeadPixel = 2f;
    private const float DespawnFactor = 0.02f;
    private const float TargetFrameRate = 30f;
    private const float PhaseFactor = 1f;
    private const float MinimumDespawnTimeoutSeconds = 0.5f;
    private const float MaximumDespawnTimeoutSeconds = 2f;
    private const byte BaseAlpha = 80;

    private static readonly Color[] RibbonColors =
    [
        Color.FromRgb(59, 130, 246),
        Color.FromRgb(34, 211, 238),
        Color.FromRgb(168, 85, 247)
    ];

    private readonly WaveLayerState[] _layers =
    [
        new(),
        new(),
        new()
    ];

    private double _lastElapsedSeconds = double.NaN;

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

        float audioAmplitude = ComputeGlobalAmplitude(amplitudes);
        double elapsedSeconds = SanitizeElapsedSeconds(options.ElapsedSeconds);
        float deltaSeconds = GetDeltaSeconds(elapsedSeconds);

        UpdateLayerStates(elapsedSeconds, deltaSeconds);

        // The reference renderer still owns all curve state while silent, but
        // global amplitude zero must never expose procedural pixels.
        if (audioAmplitude <= 0.001f)
            return;

        for (int ribbon = 0; ribbon < RibbonCount; ribbon++)
        {
            WaveLayerState layer = _layers[ribbon];
            float maxY = DrawLayer(
                buffer,
                stride,
                imageWidth,
                imageHeight,
                layer,
                audioAmplitude,
                RibbonColors[ribbon]);

            if (maxY < DeadPixel && layer.PreviousMaxY > maxY)
                SpawnLayer(layer, elapsedSeconds);

            layer.PreviousMaxY = maxY;
        }
    }

    internal static float ComputeGlobalAmplitude(ReadOnlySpan<float> amplitudes)
    {
        float maximum = 0f;

        for (int i = 0; i < amplitudes.Length; i++)
        {
            float amplitude = amplitudes[i];
            if (float.IsNaN(amplitude) || float.IsInfinity(amplitude))
                continue;

            maximum = MathF.Max(maximum, Math.Clamp(amplitude, 0f, 1f));
        }

        return maximum;
    }

    internal static float GlobalAttenuation(float x)
    {
        float denominator = AttenuationFactor + (x * x);
        return MathF.Pow(AttenuationFactor / denominator, AttenuationFactor);
    }

    private static void SpawnLayer(WaveLayerState layer, double elapsedSeconds)
    {
        layer.Spawned = true;
        layer.SpawnSeconds = elapsedSeconds;
        layer.PreviousMaxY = 0f;
        layer.CurveCount = Random.Shared.Next(MinimumCurveCount, MaximumCurveCount + 1);

        for (int curveIndex = 0; curveIndex < layer.CurveCount; curveIndex++)
        {
            layer.Curves[curveIndex] = new CurveState
            {
                Phase = 0f,
                Amplitude = 0f,
                DespawnTimeoutSeconds = RandomRange(MinimumDespawnTimeoutSeconds, MaximumDespawnTimeoutSeconds),
                Offset = RandomRange(-3f, 3f),
                Speed = RandomRange(0.5f, 1f),
                FinalAmplitude = RandomRange(0.3f, 1f),
                Width = RandomRange(1f, 3f),
                Verse = RandomRange(-1f, 1f)
            };
        }

        for (int curveIndex = layer.CurveCount; curveIndex < MaximumCurveCount; curveIndex++)
            layer.Curves[curveIndex] = default;
    }

    private void UpdateLayerStates(double elapsedSeconds, float deltaSeconds)
    {
        float frameStep = Math.Clamp(deltaSeconds * TargetFrameRate, 0f, 3f);

        for (int ribbon = 0; ribbon < RibbonCount; ribbon++)
        {
            WaveLayerState layer = _layers[ribbon];
            if (!layer.Spawned)
                SpawnLayer(layer, elapsedSeconds);

            for (int curveIndex = 0; curveIndex < layer.CurveCount; curveIndex++)
            {
                CurveState curve = layer.Curves[curveIndex];
                bool despawning = elapsedSeconds >= layer.SpawnSeconds + curve.DespawnTimeoutSeconds;
                float amplitudeDelta = DespawnFactor * frameStep;

                curve.Amplitude = Math.Clamp(
                    curve.Amplitude + (despawning ? -amplitudeDelta : amplitudeDelta),
                    0f,
                    curve.FinalAmplitude);
                curve.Phase = WrapPhase(curve.Phase + (curve.Speed * deltaSeconds * PhaseFactor));
                layer.Curves[curveIndex] = curve;
            }
        }
    }

    private static float DrawLayer(
        Span<byte> buffer,
        int stride,
        int imageWidth,
        int imageHeight,
        WaveLayerState layer,
        float audioAmplitude,
        Color color)
    {
        float centerY = imageHeight * 0.5f;
        float maxY = 0f;

        for (int x = 0; x < imageWidth; x++)
        {
            float normalizedX = imageWidth == 1 ? 0f : x / (imageWidth - 1f);
            float graphPosition = (normalizedX * 2f - 1f) * GraphX;
            float relativePosition = SampleRelativePosition(graphPosition, layer);
            float edgeAttenuation = GlobalAttenuation((graphPosition / GraphX) * 2f);
            float halfHeight = AmplitudeFactor
                * (imageHeight * 0.5f)
                * audioAmplitude
                * relativePosition
                * edgeAttenuation;

            maxY = MathF.Max(maxY, halfHeight);
            if (halfHeight <= 0f)
                continue;

            // The positive and negative reference paths are mirrored around a
            // fixed centerline and closed into one filled ribbon footprint.
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

                BlendPixelPlus(buffer, index, color, alpha);
            }
        }

        return maxY;
    }

    private static float SampleRelativePosition(float graphPosition, WaveLayerState layer)
    {
        if (layer.CurveCount <= 0)
            return 0f;

        float value = 0f;
        float denominator = Math.Max(1, layer.CurveCount - 1);

        for (int curveIndex = 0; curveIndex < layer.CurveCount; curveIndex++)
        {
            CurveState curve = layer.Curves[curveIndex];
            float staticOffset = 4f * (-1f + (curveIndex / denominator * 2f));
            float x = (graphPosition / curve.Width) - staticOffset - curve.Offset;
            float sine = MathF.Sin((curve.Verse * x) - curve.Phase);
            value += MathF.Abs(curve.Amplitude * sine * GlobalAttenuation(x));
        }

        return Math.Clamp(value / layer.CurveCount, 0f, 1f);
    }

    private float GetDeltaSeconds(double elapsedSeconds)
    {
        float deltaSeconds = double.IsNaN(_lastElapsedSeconds)
            ? 1f / TargetFrameRate
            : (float)Math.Clamp(elapsedSeconds - _lastElapsedSeconds, 0d, 0.1d);

        _lastElapsedSeconds = elapsedSeconds;
        return deltaSeconds;
    }

    private static double SanitizeElapsedSeconds(double elapsedSeconds)
    {
        return double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)
            ? 0d
            : Math.Max(0d, elapsedSeconds);
    }

    private static float WrapPhase(float phase)
    {
        phase %= 2f * MathF.PI;
        return phase < 0f ? phase + (2f * MathF.PI) : phase;
    }

    private static float RandomRange(float minimum, float maximum)
    {
        return minimum + ((float)Random.Shared.NextDouble() * (maximum - minimum));
    }

    private static void BlendPixelPlus(Span<byte> buffer, int index, Color color, byte alpha)
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

        int outputAlpha = Math.Min(255, destinationAlpha + sourceAlpha);
        if (outputAlpha == 0)
            return;

        buffer[index] = (byte)Math.Clamp(
            ((buffer[index] * destinationAlpha) + (color.B * sourceAlpha) + (outputAlpha / 2)) / outputAlpha,
            0,
            255);
        buffer[index + 1] = (byte)Math.Clamp(
            ((buffer[index + 1] * destinationAlpha) + (color.G * sourceAlpha) + (outputAlpha / 2)) / outputAlpha,
            0,
            255);
        buffer[index + 2] = (byte)Math.Clamp(
            ((buffer[index + 2] * destinationAlpha) + (color.R * sourceAlpha) + (outputAlpha / 2)) / outputAlpha,
            0,
            255);
        buffer[index + 3] = (byte)outputAlpha;
    }

    private sealed class WaveLayerState
    {
        public readonly CurveState[] Curves = new CurveState[MaximumCurveCount];
        public bool Spawned;
        public double SpawnSeconds;
        public float PreviousMaxY;
        public int CurveCount;
    }

    private struct CurveState
    {
        public float Phase;
        public float Amplitude;
        public float DespawnTimeoutSeconds;
        public float Offset;
        public float Speed;
        public float FinalAmplitude;
        public float Width;
        public float Verse;
    }
}