// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes.Downstream;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentFlyoutWPF.Classes
{
    public class Visualizer : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static int BarCount = 10;

        // Keep at 2: only an exact 2:1 minification turns WPF's default Linear
        // filter into a true box average. Other factors discard the supersampling.
        private const int Supersample = 2;
        private const int RibbonSpectrumPointCount = 7;
        private readonly int ImageWidth = 76 * Supersample;
        private readonly int ImageHeight = 32 * Supersample;
        private readonly int BarSpacing = 2 * Supersample;

        private static Visualizer? _currentInstance;
        private readonly IVisualizerRenderer _barsRenderer = new BarsVisualizerRenderer();
        private readonly IVisualizerRenderer _ribbonRenderer = new RibbonVisualizerRenderer();
        private readonly DateTime _renderStartedUtc = DateTime.UtcNow;
        private float[] _barValues = [];
        private float[] _currentBars = [];
        private readonly float[] _ribbonSpectrumValues = new float[RibbonSpectrumPointCount];
        private readonly float[] _currentRibbonSpectrum = new float[RibbonSpectrumPointCount];
        private WriteableBitmap? _bitmap;
        private readonly object _lock = new();
        private readonly object _captureCleanupLock = new();
        private readonly ResourceLifecycle<CaptureResources> _captureLifecycle = new();

        private readonly int _fftLength = 4096;
        private int _fftPos = 0;
        private readonly Complex[] _fftBuffer;

        private readonly int _targetFps = 30;
        private DateTime _lastUpdateTime = DateTime.MinValue;

        private DateTime _lastDataAvailableUtc = DateTime.MinValue;
        private int _restartInProgress; // 0=false, 1=true (Interlocked)
        private string? _deviceId; // track current device ID for restart logic

        private sealed class CaptureResources(
            WasapiLoopbackCapture capture,
            MMDevice renderDevice,
            System.Timers.Timer watchdog,
            int generation)
        {
            private readonly CallbackDrain _callbacks = new();

            public WasapiLoopbackCapture Capture { get; } = capture;
            public MMDevice RenderDevice { get; } = renderDevice;
            public System.Timers.Timer Watchdog { get; } = watchdog;
            public int Generation { get; } = generation;

            public bool TryEnterCallback() => _callbacks.TryEnter();

            public void ExitCallback() => _callbacks.Exit();

            public void StopAndWaitForCallbacks() => _callbacks.StopAndWait();
        }

        public WriteableBitmap? Bitmap
        {
            get
            {
                lock (_lock)
                {
                    return _bitmap;
                }
            }
        }

        public Visualizer()
        {
            _currentInstance = this;
            InitializeBitmap();

            _fftBuffer = new Complex[_fftLength];

            ResizeBarList(SettingsManager.Current.TaskbarVisualizerBarCount);
            AudioDeviceMonitor.Instance.DefaultDeviceChanged += OnDefaultDeviceChanged;
            TryRegisterSystemEvents();
        }

        private void TryRegisterSystemEvents()
        {
            try
            {
                SystemEvents.SessionSwitch += OnSessionSwitch;
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
            }
            catch (Exception ex)
            {
                // On some environments (e.g. non-interactive sessions), SystemEvents may not be available.
                Logger.Warn(ex, "Failed to register SystemEvents handlers for visualizer auto-restart");
            }
        }

        private void TryUnregisterSystemEvents()
        {
            try
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to unregister SystemEvents handlers for visualizer auto-restart");
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (!SettingsManager.Current.TaskbarVisualizerEnabled)
                return;

            // When unlocking after device disconnect (e.g. Bluetooth earbuds), WASAPI loopback can get stuck.
            // Restart capture on unlock / logon to recover without user action.
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon)
            {
                RequestRestart($"session switch: {e.Reason}");
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (!SettingsManager.Current.TaskbarVisualizerEnabled)
                return;

            if (e.Mode == PowerModes.Resume)
            {
                RequestRestart("power resume");
            }
        }

        private void InitializeBitmap()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    _bitmap = new WriteableBitmap(ImageWidth, ImageHeight, 96, 96, PixelFormats.Bgra32, null);
                }
            });
        }

        private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
        {
            _deviceId = e.DeviceId;

            // Even if capture isn't currently running (e.g. restart attempt failed while the device was reconfiguring),
            // we still want to try restarting as soon as Windows reports a usable default endpoint again.
            if (!SettingsManager.Current.TaskbarVisualizerEnabled)
                return;
            RequestRestart("default audio output device changed");
        }

        private void RequestRestart(string reason)
        {
            if (_captureLifecycle.IsDisposed || !SettingsManager.Current.TaskbarVisualizerEnabled)
                return;

            if (Interlocked.Exchange(ref _restartInProgress, 1) == 1)
                return;

            Logger.Info($"Restarting visualizer ({reason})");

            Task.Run(async () =>
            {
                try
                {
                    if (_captureLifecycle.IsDisposed || !SettingsManager.Current.TaskbarVisualizerEnabled)
                        return;

                    Stop();

                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        if (_captureLifecycle.IsDisposed || !SettingsManager.Current.TaskbarVisualizerEnabled)
                            return;

                        await Task.Delay(500);

                        if (_captureLifecycle.IsDisposed || !SettingsManager.Current.TaskbarVisualizerEnabled)
                            return;

                        Start();
                        if (_captureLifecycle.IsRunning)
                            return;
                        Logger.Warn($"Visualizer restart attempt {attempt + 1} failed, retrying...");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Visualizer restart failed");
                }
                finally
                {
                    Interlocked.Exchange(ref _restartInProgress, 0);
                }
            });
        }

        public static void ResizeBarList(int newBarCount)
        {
            BarCount = newBarCount;
            _currentInstance?.ResizeBarListCore(newBarCount);
        }

        internal static VisualizerRenderStyle ResolveVisualizerStyle(int persistedValue)
        {
            return persistedValue == (int)VisualizerRenderStyle.FluentRibbon
                ? VisualizerRenderStyle.FluentRibbon
                : VisualizerRenderStyle.ClassicBars;
        }

        internal static void OnRenderStyleChanged()
        {
            _currentInstance?.RefreshCurrentFrame();
        }

        private bool IsFluentRibbonStyle()
        {
            return ResolveVisualizerStyle(SettingsManager.Current.TaskbarVisualizerStyle)
                == VisualizerRenderStyle.FluentRibbon;
        }

        private bool HasCurrentStyleContent()
        {
            if (IsFluentRibbonStyle())
            {
                for (int i = 0; i < _ribbonSpectrumValues.Length; i++)
                {
                    if (_ribbonSpectrumValues[i] > 0.01f)
                        return true;
                }

                return false;
            }

            if (SettingsManager.Current.TaskbarVisualizerBaseline
                && !SettingsManager.Current.TaskbarVisualizerBaselineAutoHide)
                return true;

            for (int i = 0; i < _barValues.Length; i++)
            {
                if (_barValues[i] > 0.01f)
                    return true;
            }

            return false;
        }

        private void RefreshCurrentFrame()
        {
            if (!_captureLifecycle.IsRunning)
                return;

            SettingsManager.Current.TaskbarVisualizerHasContent = HasCurrentStyleContent();
            UpdateBitmap(_captureLifecycle.Generation);
        }

        private void ResizeBarListCore(int newBarCount)
        {
            _barValues = new float[newBarCount];
            _currentBars = new float[newBarCount];
        }

        public void Start()
        {
            if (!SettingsManager.Current.TaskbarVisualizerEnabled
                || !_captureLifecycle.TryBeginStart(out var startToken))
                return;

            ResizeBarListCore(BarCount >= 0 ? BarCount : 8);
            Array.Clear(_ribbonSpectrumValues);
            Array.Clear(_currentRibbonSpectrum);

            MMDevice? renderDevice = null;
            WasapiLoopbackCapture? capture = null;
            System.Timers.Timer? watchdog = null;
            CaptureResources? candidate = null;

            try
            {
                // Explicitly bind to the current default render endpoint.
                // Using the parameterless capture can throw transient COM errors when the default endpoint is
                // reconfiguring (e.g. Bluetooth earbuds disconnect/reconnect around lock/unlock).
                renderDevice = string.IsNullOrWhiteSpace(_deviceId)
                     ? AudioDeviceMonitor.Instance.GetDefaultRenderDevice()
                     : AudioDeviceMonitor.Instance.GetDeviceById(_deviceId) ?? AudioDeviceMonitor.Instance.GetDefaultRenderDevice();

                if (renderDevice == null)
                {
                    _captureLifecycle.CancelStart(startToken);
                    return;
                }

                capture = new WasapiLoopbackCapture(renderDevice);
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;
                capture.StartRecording();

                // automatic update timer in case audio data is not updated
                watchdog = new(500)
                {
                    AutoReset = false
                };
                int generation = startToken.Generation;
                watchdog.Elapsed += (sender, _) =>
                {
                    var resources = _captureLifecycle.Current;
                    if (resources == null
                        || resources.Generation != generation
                        || !ReferenceEquals(sender, resources.Watchdog)
                        || !resources.TryEnterCallback())
                        return;

                    try
                    {
                        if (!_captureLifecycle.IsRunning || _captureLifecycle.Generation != generation)
                            return;

                        for (int i = 0; i < _barValues.Length; i++)
                        {
                            _barValues[i] = 0;
                        }
                        Array.Clear(_ribbonSpectrumValues);
                        Array.Clear(_currentRibbonSpectrum);
                        UpdateBitmap(generation);

                        if (IsFluentRibbonStyle()
                            || !SettingsManager.Current.TaskbarVisualizerBaseline
                            || SettingsManager.Current.TaskbarVisualizerBaselineAutoHide)
                            SettingsManager.Current.TaskbarVisualizerHasContent = false;

                        // If we stop receiving loopback callbacks entirely (common after lock/unlock + device changes),
                        // the timer fires once and then never again. Use it as a recovery trigger.
                        var silenceFor = DateTime.UtcNow - _lastDataAvailableUtc;
                        if (silenceFor > TimeSpan.FromSeconds(2))
                        {
                            RequestRestart($"no audio callbacks for {silenceFor.TotalSeconds:0.0}s");
                        }
                    }
                    finally
                    {
                        resources.ExitCallback();
                    }
                };

                candidate = new CaptureResources(capture, renderDevice, watchdog, generation);
                capture = null;
                renderDevice = null;
                watchdog = null;

                if (!_captureLifecycle.TryPublish(startToken, candidate, out var rejected))
                {
                    candidate = null;
                    DisposeCaptureResources(rejected ?? throw new InvalidOperationException("Rejected visualizer resources were not returned."));
                    return;
                }

                candidate = null;
                _lastDataAvailableUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _captureLifecycle.CancelStart(startToken);
                if (candidate != null)
                    DisposeCaptureResources(candidate);
                else
                    DisposePartialCapture(capture, renderDevice, watchdog);
                Logger.Error(ex, "Failed to start visualizer");
            }
            finally
            {
                _captureLifecycle.CompleteStart(startToken);
            }
        }

        public void Stop()
        {
            lock (_captureCleanupLock)
            {
                try
                {
                    var resources = _captureLifecycle.Stop();
                    if (resources != null)
                        DisposeCaptureResources(resources);
                }
                finally
                {
                    _captureLifecycle.CompleteStop();
                }
            }
        }

        private void DisposeCaptureResources(CaptureResources resources)
        {
            try
            {
                resources.Watchdog.Stop();
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Visualizer watchdog was already stopped");
            }

            try
            {
                resources.StopAndWaitForCallbacks();
                resources.Capture.DataAvailable -= OnDataAvailable;
                resources.Capture.RecordingStopped -= OnRecordingStopped;
                try
                {
                    resources.Capture.StopRecording();
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Visualizer capture was already stopped");
                }
            }
            finally
            {
                DisposeSafely(resources.Capture, "visualizer capture");
                DisposeSafely(resources.Watchdog, "visualizer watchdog");
                DisposeSafely(resources.RenderDevice, "visualizer render device");
            }
        }

        private void DisposePartialCapture(
            WasapiLoopbackCapture? capture,
            MMDevice? renderDevice,
            System.Timers.Timer? watchdog)
        {
            if (watchdog != null)
            {
                try
                {
                    watchdog.Stop();
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Visualizer watchdog was already stopped");
                }
            }

            if (capture != null)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                try
                {
                    capture.StopRecording();
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Visualizer capture was already stopped");
                }

                DisposeSafely(capture, "partial visualizer capture");
            }

            DisposeSafely(renderDevice, "partial visualizer render device");
            DisposeSafely(watchdog, "partial visualizer watchdog");
        }

        private void DisposeSafely(IDisposable? resource, string name)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"Failed to dispose {name}");
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            var resources = _captureLifecycle.Current;
            if (resources == null
                || !ReferenceEquals(sender, resources.Capture)
                || e.BytesRecorded == 0
                || !resources.TryEnterCallback())
                return;

            try
            {
                var capture = resources.Capture;
                _lastDataAvailableUtc = DateTime.UtcNow;

                resources.Watchdog.Stop();
                resources.Watchdog.Start();

                int bytesPerSample = capture.WaveFormat.BitsPerSample / 8;
                int samplesRecorded = e.BytesRecorded / bytesPerSample;

                for (int i = 0; i < samplesRecorded; i++)
                {
                    float sampleValue = 0;
                    if (bytesPerSample == 4)
                    {
                        sampleValue = BitConverter.ToSingle(e.Buffer, i * 4);
                    }
                    else if (bytesPerSample == 2)
                    {
                        sampleValue = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                    }

                    _fftBuffer[_fftPos].X = (float)(sampleValue * FastFourierTransform.HammingWindow(_fftPos, _fftLength));
                    _fftBuffer[_fftPos].Y = 0;
                    _fftPos++;

                    // When buffer isn't full, skip processing and continue filling
                    if (_fftPos < _fftLength)
                        continue;

                    // perform FFT
                    _fftPos = 0;
                    ProcessFftData(capture);

                    // Update UI with frame rate limiting
                    DateTime now = DateTime.UtcNow;
                    double minFrameTime = 1000.0 / _targetFps;
                    double timeSinceLastUpdate = (now - _lastUpdateTime).TotalMilliseconds;

                    if (timeSinceLastUpdate < minFrameTime)
                        continue;

                    _lastUpdateTime = now;

                    if (IsFluentRibbonStyle())
                    {
                        bool hasRibbonContent = HasCurrentStyleContent();
                        SettingsManager.Current.TaskbarVisualizerHasContent = hasRibbonContent;

                        if (hasRibbonContent)
                            UpdateBitmap(resources.Generation);

                        continue;
                    }

                    SettingsManager.Current.TaskbarVisualizerHasContent = true;

                    if (SettingsManager.Current.TaskbarVisualizerBaseline && !SettingsManager.Current.TaskbarVisualizerBaselineAutoHide)
                    {
                        // if baseline is enabled and autohide is off, we want to keep showing the bars even when they are all zero
                        UpdateBitmap(resources.Generation);
                        break;
                    }

                    // check if bars are all zero, if so set has content to false to disable hover effect
                    bool allZero = true;
                    for (int j = 0; j < BarCount; j++)
                    {
                        if (_barValues[j] > 0.01f)
                        {
                            allZero = false;
                            break;
                        }
                    }

                    // update bars if they have content
                    if (!allZero)
                        UpdateBitmap(resources.Generation);
                    else
                        SettingsManager.Current.TaskbarVisualizerHasContent = false;
                }
            }
            finally
            {
                resources.ExitCallback();
            }
        }

        private void ProcessFftData(WasapiLoopbackCapture capture)
        {
            FastFourierTransform.FFT(true, (int)Math.Log(_fftLength, 2.0), _fftBuffer);

            int sampleRate = capture.WaveFormat.SampleRate;
            double frequencyPerBin = (double)sampleRate / _fftLength;

            double minFreq = 40;   // Hz
            double maxFreq = 8000; // Hz
            //double minFreq = 40;  // Hz // could be a setting to be bass only
            //double maxFreq = 120; // Hz
            float minDb = (SettingsManager.Current.TaskbarVisualizerAudioSensitivity * -10f) - 30f;
            float maxDb = (SettingsManager.Current.TaskbarVisualizerAudioPeakLevel * 10f) - 30f;

            for (int i = 0; i < BarCount; i++)
            {
                double startFreq = minFreq * Math.Pow(maxFreq / minFreq, (double)i / BarCount);
                double endFreq = minFreq * Math.Pow(maxFreq / minFreq, (double)(i + 1) / BarCount);

                int startBin = (int)(startFreq / frequencyPerBin);
                int endBin = (int)(endFreq / frequencyPerBin);

                if (endBin <= startBin) endBin = startBin + 1;
                if (endBin >= _fftBuffer.Length / 2) endBin = _fftBuffer.Length / 2 - 1;

                float maxAmplitude = 0;

                // Find max amplitude
                for (int j = startBin; j < endBin; j++)
                {
                    float amplitude = (float)Math.Sqrt(_fftBuffer[j].X * _fftBuffer[j].X + _fftBuffer[j].Y * _fftBuffer[j].Y);
                    if (amplitude > maxAmplitude)
                        maxAmplitude = amplitude;
                }

                float progress = (float)i / BarCount;
                float linearBoost = 1.0f + (progress * 75.0f);
                maxAmplitude *= linearBoost;

                if (maxAmplitude < 0.001f) maxAmplitude = 0.001f;

                float db = 20f * (float)Math.Log10(maxAmplitude);

                float intensity = (db - minDb) / (maxDb - minDb);
                intensity = Math.Clamp(intensity, 0f, 1f);

                _currentBars[i] = intensity;
            }

            for (int i = 0; i < BarCount; i++)
            {
                if (_currentBars[i] > _barValues[i])
                {
                    // Jump up quickly
                    _barValues[i] = _currentBars[i];
                }
                else
                {
                    // Fall down slowly
                    //_barValues[i] = (_barValues[i] * 0.9f) + (_currentBars[i] * 0.1f);
                    _barValues[i] = (_barValues[i] * 0.8f) + (_currentBars[i] * 0.2f);
                    //_barValues[i] = (_barValues[i] * 0.7f) + (_currentBars[i] * 0.3f); // could be options for smoothening
                    //_barValues[i] = (_barValues[i] * 0.6f) + (_currentBars[i] * 0.4f);
                }
            }

            ProcessRibbonSpectrumData(frequencyPerBin, minFreq, maxFreq, minDb, maxDb);
        }

        private void ProcessRibbonSpectrumData(
            double frequencyPerBin,
            double minFreq,
            double maxFreq,
            float minDb,
            float maxDb)
        {
            for (int i = 0; i < _ribbonSpectrumValues.Length; i++)
            {
                double startFreq = minFreq * Math.Pow(maxFreq / minFreq, (double)i / _ribbonSpectrumValues.Length);
                double endFreq = minFreq * Math.Pow(maxFreq / minFreq, (double)(i + 1) / _ribbonSpectrumValues.Length);

                int startBin = (int)(startFreq / frequencyPerBin);
                int endBin = (int)(endFreq / frequencyPerBin);

                if (endBin <= startBin) endBin = startBin + 1;
                if (endBin >= _fftBuffer.Length / 2) endBin = _fftBuffer.Length / 2 - 1;

                float maxAmplitude = 0;

                for (int j = startBin; j < endBin; j++)
                {
                    float amplitude = (float)Math.Sqrt(_fftBuffer[j].X * _fftBuffer[j].X + _fftBuffer[j].Y * _fftBuffer[j].Y);
                    if (amplitude > maxAmplitude)
                        maxAmplitude = amplitude;
                }

                float progress = (float)i / _ribbonSpectrumValues.Length;
                float linearBoost = 1.0f + (progress * 75.0f);
                maxAmplitude *= linearBoost;

                if (maxAmplitude < 0.001f) maxAmplitude = 0.001f;

                float db = 20f * (float)Math.Log10(maxAmplitude);
                float intensity = (db - minDb) / (maxDb - minDb);
                intensity = Math.Clamp(intensity, 0f, 1f);

                _currentRibbonSpectrum[i] = intensity;
            }

            for (int i = 0; i < _ribbonSpectrumValues.Length; i++)
            {
                if (_currentRibbonSpectrum[i] > _ribbonSpectrumValues[i])
                {
                    _ribbonSpectrumValues[i] = _currentRibbonSpectrum[i];
                }
                else
                {
                    _ribbonSpectrumValues[i] = (_ribbonSpectrumValues[i] * 0.8f) + (_currentRibbonSpectrum[i] * 0.2f);
                }
            }
        }

        private void UpdateBitmap(int generation)
        {
            if (_bitmap == null)
                return;

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    if (_bitmap == null
                        || !_captureLifecycle.IsRunning
                        || _captureLifecycle.Generation != generation)
                        return;

                    _bitmap.Lock();

                    try
                    {
                        unsafe
                        {
                            IntPtr pBackBuffer = _bitmap.BackBuffer;
                            int stride = _bitmap.BackBufferStride;
                            int bufferSize = stride * ImageHeight;

                            Span<byte> buffer = new Span<byte>(pBackBuffer.ToPointer(), bufferSize);

                            buffer.Clear();

                            DrawVisualizer(stride, buffer);
                        }

                        _bitmap.AddDirtyRect(new Int32Rect(0, 0, ImageWidth, ImageHeight));
                    }
                    finally
                    {
                        _bitmap.Unlock();
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void DrawVisualizer(int stride, Span<byte> buffer)
        {
            SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0
                ? BitmapHelper.SavedDominantColors.Last()
                : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");

            var options = new VisualizerRenderOptions(
                brush.Color,
                SettingsManager.Current.TaskbarVisualizerCenteredBars,
                SettingsManager.Current.TaskbarVisualizerBaseline,
                SettingsManager.Current.TaskbarVisualizerBarCount,
                BarSpacing,
                Supersample * 2,
                (DateTime.UtcNow - _renderStartedUtc).TotalSeconds);

            VisualizerRenderStyle style = ResolveVisualizerStyle(SettingsManager.Current.TaskbarVisualizerStyle);
            IVisualizerRenderer renderer = style == VisualizerRenderStyle.FluentRibbon
                ? _ribbonRenderer
                : _barsRenderer;
            ReadOnlySpan<float> amplitudes = style == VisualizerRenderStyle.FluentRibbon
                ? _ribbonSpectrumValues
                : _barValues;

            renderer.Render(buffer, stride, ImageWidth, ImageHeight, amplitudes, in options);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Logger.Error(e.Exception, "Visualizer recording stopped due to an error");
            }
        }

        public void Dispose()
        {
            lock (_captureCleanupLock)
            {
                var resources = _captureLifecycle.Dispose();
                if (resources != null)
                    DisposeCaptureResources(resources);
            }

            AudioDeviceMonitor.Instance.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            TryUnregisterSystemEvents();

            if (ReferenceEquals(_currentInstance, this))
                _currentInstance = null;

            GC.SuppressFinalize(this);
        }
    }
}