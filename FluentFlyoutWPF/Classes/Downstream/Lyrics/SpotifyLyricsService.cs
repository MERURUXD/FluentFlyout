// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Net.Http;

namespace FluentFlyoutWPF.Classes.Downstream.Lyrics;

/// <summary>
/// An event-fed, single selected-session owner. No timers, WPF objects or implicit startup.
/// Call SelectAsync after app filtering and selection; null owner/track or enabled=false
/// invalidates previous work. Read Current on the Dispatcher rather than publishing task results.
/// </summary>
public sealed class SpotifyLyricsService : IDisposable
{
    private readonly object _gate = new();
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly TimeProvider _clock;
    private readonly Dictionary<LyricsTrack, CacheEntry> _cache = [];
    private CancellationTokenSource? _request;
    private object? _owner;
    private LyricsTrack? _track;
    private LyricsDocument? _current;
    private Task _pending = Task.CompletedTask;
    private long _generation;
    private bool _disposed;

    private sealed record CacheEntry(LyricsDocument? Document, DateTimeOffset Expires);

    // Provider order is explicit: production callers supply QQ, then NetEase.
    public SpotifyLyricsService(ILyricsProvider qq, ILyricsProvider netease, TimeProvider? clock = null)
    {
        _providers = [qq, netease];
        _clock = clock ?? TimeProvider.System;
    }

    public LyricsDocument? Current { get { lock (_gate) return _current; } }

    public Task SelectAsync(bool enabled, object? selectedSession, string? applicationId, LyricsTrack? track)
    {
        lock (_gate)
        {
            if (_disposed)
                return Task.CompletedTask;
            bool eligible = enabled && selectedSession != null && LyricsMatchPolicy.IsSpotify(applicationId)
                && track is { DurationMs: > 0 } && !string.IsNullOrWhiteSpace(track.Title)
                && !string.IsNullOrWhiteSpace(track.Artist);
            if (eligible && ReferenceEquals(_owner, selectedSession) && _track == track
                && (!_pending.IsCompleted || (_cache.TryGetValue(track!, out var existing)
                    && existing.Expires > _clock.GetUtcNow())))
                return _pending;

            _generation++;
            _request?.Cancel();
            _request = null;
            _current = null;
            _owner = eligible ? selectedSession : null;
            _track = eligible ? track : null;
            if (!eligible)
                return _pending = Task.CompletedTask;

            if (_cache.TryGetValue(track!, out var cached) && cached.Expires > _clock.GetUtcNow())
            {
                _current = cached.Document;
                return _pending = Task.CompletedTask;
            }

            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _request = cancellation;
            long generation = _generation;
            return _pending = Task.Run(() => LoadAsync(track!, generation, cancellation));
        }
    }

    private async Task LoadAsync(LyricsTrack track, long generation, CancellationTokenSource cancellation)
    {
        LyricsDocument? document = null;
        try
        {
            var token = cancellation.Token;
            var title = LyricsMatchPolicy.SearchTitle(track.Title);
            var queries = new[] { $"{title} {track.Artist}", title }.Distinct().ToArray();
            foreach (var provider in _providers)
            {
                var attempted = new HashSet<string>();
                foreach (var query in queries)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var candidates = await provider.SearchAsync(query, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        var match = LyricsMatchPolicy.Select(track, candidates);
                        if (match == null || !attempted.Add(match.Id))
                            continue;
                        document = await provider.FetchAsync(match.Id, token).ConfigureAwait(false);
                        if (document is { Lines.Count: > 0 })
                            break;
                        document = null;
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        // A per-request timeout can fall back; owner cancellation cannot.
                    }
                    catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException
                        or System.Text.Json.JsonException or FormatException or System.Xml.XmlException
                        or OverflowException or InvalidOperationException or ArgumentException
                        or ICSharpCode.SharpZipLib.SharpZipBaseException)
                    {
                        // A failed source can fall back; do not log lyrics or query URLs.
                    }
                }
                if (document != null)
                    break;
            }
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_gate)
            {
                if (!_disposed && generation == _generation)
                {
                    _current = cancellation.IsCancellationRequested ? null : document;
                    if (_cache.Count >= 64)
                        _cache.Remove(_cache.MinBy(pair => pair.Value.Expires).Key);
                    _cache[track] = new(_current, _clock.GetUtcNow().Add(
                        _current == null ? TimeSpan.FromMinutes(2) : TimeSpan.FromHours(12)));
                    _request = null;
                }
                cancellation.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _generation++;
            _request?.Cancel();
            _request = null;
            _owner = null;
            _track = null;
            _current = null;
            _cache.Clear();
        }
    }
}