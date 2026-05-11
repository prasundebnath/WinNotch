using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinNotch.Models;

namespace WinNotch.Services;

/// <summary>
/// Wraps the Windows Global System Media Transport Controls (GSMTC) API.
/// 
/// This is the same API the Windows taskbar uses to show media info.
/// It works with Spotify, Edge, Chrome, Windows Media Player, VLC (when
/// configured), and any app that registers a media session.
/// 
/// Usage:
///   var svc = new MediaService();
///   await svc.InitAsync();
///   svc.MediaChanged += (sender, info) => { ... };
/// </summary>
public class MediaService : IDisposable
{
    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Fires on the calling thread when track info changes.</summary>
    public event EventHandler<MediaInfo>? MediaChanged;

    // ── Fields ────────────────────────────────────────────────────────────────
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private bool _disposed;

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Must be called once at startup (async because the WinRT API requires it).
    /// </summary>
    public async Task InitAsync()
    {
        _sessionManager = await GlobalSystemMediaTransportControlsSessionManager
            .RequestAsync();

        _sessionManager.CurrentSessionChanged += OnSessionChanged;

        // Hook up the current session immediately (if music is already playing)
        AttachSession(_sessionManager.GetCurrentSession());
    }

    // ── Session wiring ─────────────────────────────────────────────────────────

    private void OnSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        AttachSession(sender.GetCurrentSession());
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        // Detach previous session events
        if (_currentSession is not null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged    -= OnPlaybackInfoChanged;
        }

        _currentSession = session;

        if (_currentSession is null)
        {
            // No active media session → emit empty state
            RaiseMediaChanged(MediaInfo.Empty);
            return;
        }

        _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged    += OnPlaybackInfoChanged;

        // Fetch the current state right away
        _ = FetchAndRaiseAsync();
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
        => _ = FetchAndRaiseAsync();

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
        => _ = FetchAndRaiseAsync();

    // ── Data fetching ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the current session's media properties and playback state,
    /// converts the album art thumbnail to a WPF BitmapSource, then
    /// fires MediaChanged.
    /// </summary>
    private async Task FetchAndRaiseAsync()
    {
        if (_currentSession is null)
        {
            RaiseMediaChanged(MediaInfo.Empty);
            return;
        }

        try
        {
            // Get track metadata (title, artist, album, thumbnail)
            var props = await _currentSession.TryGetMediaPropertiesAsync();
            if (props is null)
            {
                RaiseMediaChanged(MediaInfo.Empty);
                return;
            }

            // Get play/pause state
            var playback = _currentSession.GetPlaybackInfo();
            bool isPlaying = playback?.PlaybackStatus ==
                             GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // Convert WinRT IRandomAccessStreamReference → WPF BitmapSource
            BitmapSource? albumArt = null;
            if (props.Thumbnail is not null)
            {
                albumArt = await ThumbnailToBitmapAsync(props.Thumbnail);
            }

            var info = new MediaInfo(
                Title:    props.Title     ?? string.Empty,
                Artist:   props.Artist    ?? string.Empty,
                Album:    props.AlbumTitle ?? string.Empty,
                AlbumArt: albumArt,
                IsPlaying: isPlaying
            );

            RaiseMediaChanged(info);
        }
        catch
        {
            // Session may have disappeared between checks — ignore gracefully
            RaiseMediaChanged(MediaInfo.Empty);
        }
    }

    /// <summary>
    /// Converts a WinRT thumbnail stream reference to a frozen WPF BitmapImage.
    /// "Frozen" means it's immutable and thread-safe for cross-thread UI use.
    /// </summary>
    private static async Task<BitmapSource?> ThumbnailToBitmapAsync(
        IRandomAccessStreamReference streamRef)
    {
        try
        {
            using var stream = await streamRef.OpenReadAsync();

            // Copy WinRT stream → byte array → MemoryStream (WPF can read this)
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);

            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption  = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze(); // Required for cross-thread access
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    // ── Media controls ────────────────────────────────────────────────────────

    /// <summary>Toggles play/pause on the current session.</summary>
    public async Task TogglePlayPauseAsync()
    {
        if (_currentSession is not null)
            await _currentSession.TryTogglePlayPauseAsync();
    }

    /// <summary>Skips to the next track.</summary>
    public async Task SkipNextAsync()
    {
        if (_currentSession is not null)
            await _currentSession.TrySkipNextAsync();
    }

    /// <summary>Skips to the previous track.</summary>
    public async Task SkipPreviousAsync()
    {
        if (_currentSession is not null)
            await _currentSession.TrySkipPreviousAsync();
    }

    // ── Helper ─────────────────────────────────────────────────────────────────

    private void RaiseMediaChanged(MediaInfo info)
        => MediaChanged?.Invoke(this, info);

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_currentSession is not null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged    -= OnPlaybackInfoChanged;
        }

        if (_sessionManager is not null)
            _sessionManager.CurrentSessionChanged -= OnSessionChanged;
    }
}
