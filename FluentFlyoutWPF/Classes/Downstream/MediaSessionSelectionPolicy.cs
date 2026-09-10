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
        ArgumentNullException.ThrowIfNull(allowedSessions);

        var sessions = allowedSessions.ToList();
        var spotifySessions = sessions.Where(IsSpotifySession).ToList();
        if (spotifySessions.Count == 0)
            return null;

        var playingSpotify = spotifySessions.Where(IsPlaying).ToList();
        if (playingSpotify.Count > 0)
            return FindFocusedOrFirst(playingSpotify, focusedSession);

        var playingNonSpotify = sessions
            .Where(session => !IsSpotifySession(session) && IsPlaying(session))
            .ToList();

        return playingNonSpotify.Count > 0
            ? FindFocusedOrFirst(playingNonSpotify, focusedSession)
            : null;
    }

    /// <summary>
    /// Identifies the native Spotify application without inspecting media metadata.
    /// Browser sessions that play Spotify Web Player therefore remain browser sessions.
    /// </summary>
    public static bool IsSpotifySession(MediaSession session) =>
        !string.IsNullOrWhiteSpace(session.Id)
        && session.Id.Contains("spotify", StringComparison.OrdinalIgnoreCase);

    private static MediaSession FindFocusedOrFirst(
        IReadOnlyList<MediaSession> candidates,
        MediaSession? focusedSession)
    {
        if (focusedSession != null)
        {
            var focused = candidates.FirstOrDefault(session => ReferenceEquals(session, focusedSession))
                ?? candidates.FirstOrDefault(session =>
                string.Equals(session.Id, focusedSession.Id, StringComparison.Ordinal));

            if (focused != null)
                return focused;
        }

        return candidates[0];
    }

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
