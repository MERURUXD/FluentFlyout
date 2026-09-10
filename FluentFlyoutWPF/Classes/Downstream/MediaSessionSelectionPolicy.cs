// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using System.Runtime.InteropServices;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF.Classes.Downstream;

/// <summary>
/// Provides the downstream-only Spotify Preferred override for media-session selection.
/// The caller supplies sessions after applying the existing app-filtering policy.
/// </summary>
public static class MediaSessionSelectionPolicy
{
    public const int Automatic = 0;
    public const int SpotifyPreferred = 1;

    public static bool IsSpotifyPreferred(int mode) => mode == SpotifyPreferred;

    /// <summary>
    /// Returns a preferred session when Spotify Preferred has an applicable override;
    /// otherwise returns null so the caller can retain its normal focused-then-first fallback.
    /// </summary>
    public static MediaSession? TrySelectSpotifyPreferred(
        IEnumerable<MediaSession> allowedSessions,
        MediaSession? focusedSession)
    {
        return TrySelectSpotifyPreferredCore(
            allowedSessions,
            focusedSession,
            session => session.Id,
            IsSpotifySession,
            IsPlaying);
    }

    internal static T? TrySelectSpotifyPreferredCore<T>(
        IEnumerable<T> allowedSessions,
        T? focusedSession,
        Func<T, string> getId,
        Func<T, bool> isSpotify,
        Func<T, bool> isPlaying)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(allowedSessions);
        ArgumentNullException.ThrowIfNull(getId);
        ArgumentNullException.ThrowIfNull(isSpotify);
        ArgumentNullException.ThrowIfNull(isPlaying);

        var sessions = allowedSessions.ToList();
        var spotifySessions = sessions.Where(isSpotify).ToList();
        if (spotifySessions.Count == 0)
            return null;

        var playingSpotify = spotifySessions.Where(isPlaying).ToList();
        if (playingSpotify.Count > 0)
            return SelectFocusedOrFirst(playingSpotify, focusedSession, getId);

        var playingNonSpotify = sessions
            .Where(session => !isSpotify(session) && isPlaying(session))
            .ToList();

        return playingNonSpotify.Count > 0
            ? SelectFocusedOrFirst(playingNonSpotify, focusedSession, getId)
            : null;
    }

    internal static T? SelectFocusedOrFirst<T>(
        IReadOnlyList<T> candidates,
        T? focusedSession,
        Func<T, string> getId)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(getId);

        if (focusedSession != null)
        {
            var focused = candidates.FirstOrDefault(session => ReferenceEquals(session, focusedSession))
                ?? candidates.FirstOrDefault(session =>
                    string.Equals(getId(session), getId(focusedSession), StringComparison.Ordinal));

            if (focused != null)
                return focused;
        }

        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// Identifies the native Spotify application without inspecting media metadata.
    /// Browser sessions that play Spotify Web Player therefore remain browser sessions.
    /// </summary>
    public static bool IsSpotifySession(MediaSession session) => IsSpotifyApplicationId(session.Id);

    internal static bool IsSpotifyApplicationId(string? applicationId) =>
        !string.IsNullOrWhiteSpace(applicationId)
        && applicationId.Contains("spotify", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlaying(MediaSession session)
    {
        try
        {
            return session.ControlSession?.GetPlaybackInfo()?.PlaybackStatus
                == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception ex) when (ex is COMException or ObjectDisposedException or InvalidOperationException)
        {
            // A closing or restarting session can become unreadable between enumeration and inspection.
            return false;
        }
    }
}