// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes.Downstream;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentFlyoutWPF.Classes
{
    public class Visualizer : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        public static int BarCount = 10;
        private const int Supersample = 2;
        private readonly int ImageWidth = 76 * Supersample;
        private readonly int ImageHeight = 32 * Supersample;
        private readonly int BarSpacing = 2 * Supersample;
        private static Visualizer? _currentInstance;
        private float[] _barValues = new float[10];
        private WriteableBitmap? _bitmap;
        private readonly VisualizerAudioEngine _audio = new(source => new VisualizerWasapiCapture(source));
        private bool _disposed;

        public WriteableBitmap? Bitmap => _bitmap;

        public Visualizer()
        {
            _currentInstance = this;
            Application.Current.Dispatcher.Invoke(() =>
                _bitmap = new WriteableBitmap(ImageWidth, ImageHeight, 96, 96, PixelFormats.Bgra32, null));
            _audio.FrameReady += OnFrameReady;
            AudioDeviceMonitor.Instance.DefaultDeviceChanged += OnDefaultDeviceChanged;
            AudioDeviceMonitor.Instance.DefaultCaptureDeviceChanged += OnDefaultCaptureDeviceChanged;
            try
            {
                SystemEvents.SessionSwitch += OnSessionSwitch;
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
            }
            catch (Exception ex) { Logger.Warn(ex, "Failed to register visualizer system events"); }
        }

        public void Start()
        {
            RefreshSpectrumSettings();
            _ = _audio.Configure(SettingsManager.Current.TaskbarVisualizerEnabled,
                SettingsManager.Current.TaskbarVisualizerAudioSource);
        }

        public void Stop() => _audio.Configure(false, SettingsManager.Current.TaskbarVisualizerAudioSource).GetAwaiter().GetResult();

        public static void ChangeAudioSource()
        {
            var instance = _currentInstance;
            if (instance != null)
                _ = instance._audio.Configure(SettingsManager.Current.TaskbarVisualizerEnabled,
                    SettingsManager.Current.TaskbarVisualizerAudioSource);
        }

        public static void ResizeBarList(int newBarCount) => RefreshSpectrumSettings();

        public static void RefreshSpectrumSettings()
        {
            var settings = SettingsManager.Current;
            _currentInstance?._audio.SetSpectrum(settings.TaskbarVisualizerBarCount,
                settings.TaskbarVisualizerAudioSensitivity, settings.TaskbarVisualizerAudioPeakLevel);
        }

        private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e) => _ = _audio.Restart(0);
        private void OnDefaultCaptureDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e) => _ = _audio.Restart(1);

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon)
                RestartSources();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
                RestartSources();
        }

        private void RestartSources()
        {
            _ = _audio.Restart(0);
            _ = _audio.Restart(1);
        }

        private void OnFrameReady(VisualizerAudioFrame frame)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_disposed || !ReferenceEquals(_currentInstance, this) || !_audio.IsCurrent(frame.Revision) || _bitmap == null)
                    return;
                var settings = SettingsManager.Current;
                settings.TaskbarVisualizerSourceStatus = string.Join(" · ", new[]
                {
                    SourceStatusText("VisualizerSourceDesktop", frame.Desktop),
                    SourceStatusText("VisualizerSourceMicrophone", frame.Microphone)
                }.Where(text => text.Length > 0));
                _barValues = frame.Bars;
                BarCount = frame.Bars.Length;
                settings.TaskbarVisualizerHasContent = frame.Bars.Any(value => value > 0.01f)
                    || settings.TaskbarVisualizerBaseline && !settings.TaskbarVisualizerBaselineAutoHide;
                _bitmap.Lock();
                try
                {
                    unsafe
                    {
                        int stride = _bitmap.BackBufferStride;
                        var buffer = new Span<byte>(_bitmap.BackBuffer.ToPointer(), stride * ImageHeight);
                        buffer.Clear();
                        DrawBars(stride, buffer);
                    }
                    _bitmap.AddDirtyRect(new Int32Rect(0, 0, ImageWidth, ImageHeight));
                }
                finally { _bitmap.Unlock(); }
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private static string SourceStatusText(string sourceKey, VisualizerSourceStatus status)
        {
            if (status == VisualizerSourceStatus.Off)
                return string.Empty;
            string key = status switch
            {
                VisualizerSourceStatus.Active => "VisualizerSourceActive",
                VisualizerSourceStatus.Starting => "VisualizerSourceStarting",
                _ => "VisualizerSourceUnavailable"
            };
            return $"{Application.Current.TryFindResource(sourceKey)}: {Application.Current.TryFindResource(key)}";
        }

        private unsafe void DrawBars(int stride, Span<byte> buffer)
        {
            // Resolve brush once 
            SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0
                ? BitmapHelper.SavedDominantColors.Last()
                : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");

            byte b = brush.Color.B;
            byte g = brush.Color.G;
            byte r = brush.Color.R;

            bool centeredBars = SettingsManager.Current.TaskbarVisualizerCenteredBars;
            int barBaseline = SettingsManager.Current.TaskbarVisualizerBaseline ? Supersample * 2 : 0;

            int centerY = ImageHeight / 2;

            // Horizontal layout 
            ComputeLayout(ImageWidth, BarCount, BarSpacing,
                out int barWidth,
                out int offsetX);

            // Radius 
            float baseRadius = GetCornerRadius();

            // AA constants 
            const float aa = 1.25f;
            float invAA = 1f / aa;

            for (int i = 0; i < BarCount; i++)
            {
                int barX = offsetX + i * (barWidth + BarSpacing);

                int barHeight = GetBarHeight(_barValues[i], barBaseline);

                if (barHeight <= 0)
                    continue;

                ComputeVertical(centeredBars, centerY, barHeight, out int barY, out int barEndY);

                // Clamp radius per bar
                float radius = ClampRadius(baseRadius, barWidth, barHeight);
                float radiusSq = radius * radius;

                RasterizeBar(
                    buffer, stride,
                    barX, barWidth,
                    barY, barEndY,
                    centeredBars,
                    radius, radiusSq, invAA,
                    b, g, r);
            }
        }

        private static void ComputeLayout(
            int imageWidth,
            int barCount,
            int spacing,
            out int barWidth,
            out int offsetX)
        {
            int totalSpacing = (barCount - 1) * spacing;

            int availableWidth = imageWidth - totalSpacing - 1;

            barWidth = availableWidth / barCount;

            int usedWidth = barWidth * barCount + totalSpacing;

            // Center safely
            offsetX = (imageWidth - usedWidth) >> 1;
        }

        private void ComputeVertical(bool centered, int centerY, int height, out int y, out int endY)
        {
            if (centered)
            {
                int half = height >> 1; // faster than /2
                y = centerY - half;
                endY = centerY + half;
            }
            else
            {
                y = ImageHeight - height;
                endY = ImageHeight;
            }
        }

        private int GetBarHeight(float value, int baseline)
        {
            return Math.Max((int)(Math.Clamp(value, 0f, 1f) * ImageHeight), baseline);
        }
        private static float GetCornerRadius()
        {
            return (2f * Supersample) / MathF.Max(1f, SettingsManager.Current.TaskbarVisualizerBarCount / 10f);
        }

        private static float ClampRadius(float r, int width, int height)
        {
            float max = MathF.Min(width, height) * 0.5f;
            return r > max ? max : r;
        }

        private unsafe void RasterizeBar(
            Span<byte> buffer,
            int stride,
            int barX,
            int barWidth,
            int barY,
            int barEndY,
            bool centeredBars,
            float radius,
            float radiusSq,
            float invAA,
            byte b, byte g, byte r)
        {
            float left = barX;
            float right = barX + barWidth;
            float top = barY;
            float bottom = barEndY;

            float innerLeft = left + radius;
            float innerRight = right - radius;
            float innerTop = top + radius;
            float innerBottom = bottom - radius;

            for (int y = barY; y < barEndY && y < ImageHeight && y >= 0; y++)
            {
                int row = y * stride;
                float py = y + 0.5f;

                for (int x = barX; x < barX + barWidth && x < ImageWidth; x++)
                {
                    int index = row + (x << 2); // x * 4 (bitshift faster)
                    if (index + 3 >= buffer.Length)
                        continue;

                    float px = x + 0.5f;

                    // CENTER
                    if (px >= innerLeft && px <= innerRight)
                    {
                        WritePixel(buffer, index, b, g, r, 255);
                        continue;
                    }

                    // SIDES
                    if (py >= innerTop && py <= innerBottom)
                    {
                        WritePixel(buffer, index, b, g, r, 255);
                        continue;
                    }

                    // FLAT BOTTOM
                    if (!centeredBars && py >= innerBottom)
                    {
                        WritePixel(buffer, index, b, g, r, 255);
                        continue;
                    }

                    // CORNERS
                    float cx = px < innerLeft ? innerLeft : (px > innerRight ? innerRight : px);
                    float cy = py < innerTop ? innerTop : (py > innerBottom ? innerBottom : py);

                    float dx = px - cx;
                    float dy = py - cy;

                    float distSq = dx * dx + dy * dy;
                    float sdf = (distSq - radiusSq) / (2f * radius);

                    float alpha = 0.5f - sdf * invAA;

                    if (alpha <= 0f)
                        continue;

                    if (alpha > 1f) alpha = 1f;

                    WritePixel(buffer, index, b, g, r, (byte)(255 * alpha));
                }
            }
        }

        private static void WritePixel(Span<byte> buffer, int index, byte b, byte g, byte r, byte a)
        {
            buffer[index] = b;
            buffer[index + 1] = g;
            buffer[index + 2] = r;
            buffer[index + 3] = a;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            AudioDeviceMonitor.Instance.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            AudioDeviceMonitor.Instance.DefaultCaptureDeviceChanged -= OnDefaultCaptureDeviceChanged;
            try
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch (Exception ex) { Logger.Warn(ex, "Failed to unregister visualizer system events"); }
            _audio.FrameReady -= OnFrameReady;
            _audio.Dispose();
            if (ReferenceEquals(_currentInstance, this))
            {
                _currentInstance = null;
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_currentInstance != null)
                        return;
                    SettingsManager.Current.TaskbarVisualizerSourceStatus = string.Empty;
                    SettingsManager.Current.TaskbarVisualizerHasContent = false;
                });
            }
            GC.SuppressFinalize(this);
        }
    }
}