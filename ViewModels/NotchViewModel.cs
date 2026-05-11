using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WinNotch.Models;
using WinNotch.Services;

namespace WinNotch.ViewModels;

/// <summary>
/// ViewModel for the notch window.
///
/// Implements INotifyPropertyChanged so that WPF bindings automatically
/// update the UI whenever media changes.
///
/// Lifecycle:
///   1. Window creates NotchViewModel
///   2. ViewModel creates MediaService and calls InitAsync()
///   3. MediaService fires MediaChanged whenever the track changes
///   4. ViewModel updates its properties → UI auto-updates via bindings
/// </summary>
public class NotchViewModel : INotifyPropertyChanged, IDisposable
{
    // ── Backing fields ─────────────────────────────────────────────────────────
    private string _title          = string.Empty;
    private string _artist         = string.Empty;
    private BitmapSource? _albumArt;
    private bool   _isPlaying;
    private bool   _isExpanded;
    private bool   _hasMedia;
    private bool   _isClockVisible;

    // Collapsed-pill clock
    private string _currentTime    = DateTime.Now.ToString("h:mm tt");

    // Expanded clock-panel
    private string _clockTimeLarge = DateTime.Now.ToString("h:mm");
    private string _clockPeriod    = DateTime.Now.ToString("tt");
    private string _clockDateLine  = DateTime.Now.ToString("dddd, d MMMM");
    private string _calendarMonth  = DateTime.Now.ToString("MMMM yyyy");
    private IReadOnlyList<CalendarDay> _calendarDays = BuildCalendar(DateTime.Now);

    private readonly MediaService _mediaService;
    private readonly System.Windows.Threading.DispatcherTimer _clockTimer;
    private int _lastMinute = DateTime.Now.Minute;

    // ── Constructor ────────────────────────────────────────────────────────────

    public NotchViewModel()
    {
        _mediaService = new MediaService();
        _mediaService.MediaChanged += OnMediaChanged;

        // Media control commands
        PlayPauseCommand     = new RelayCommand(() => _ = _mediaService.TogglePlayPauseAsync());
        NextTrackCommand     = new RelayCommand(() => _ = _mediaService.SkipNextAsync());
        PreviousTrackCommand = new RelayCommand(() => _ = _mediaService.SkipPreviousAsync());

        // Clock timer – ticks every second to keep time displays fresh
        _clockTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();

        // Fire-and-forget init (WinRT requires async, but constructor is sync)
        _ = _mediaService.InitAsync();
    }

    // ── Clock tick ────────────────────────────────────────────────────────────

    private void OnClockTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        CurrentTime    = now.ToString("h:mm tt");
        ClockTimeLarge = now.ToString("h:mm");
        ClockPeriod    = now.ToString("tt");

        // Rebuild date / calendar only when the minute rolls over
        if (now.Minute != _lastMinute)
        {
            _lastMinute   = now.Minute;
            ClockDateLine = now.ToString("dddd, d MMMM");
            CalendarMonth = now.ToString("MMMM yyyy");
            CalendarDays  = BuildCalendar(now);
        }
    }

    // ── Calendar builder ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns a 42-cell (6 rows × 7 cols) flat list representing the
    /// calendar grid for the month containing <paramref name="reference"/>.
    /// Week starts on Monday. Cells outside the current month have
    /// IsCurrentMonth=false and show the neighbour-month day number.
    /// </summary>
    private static IReadOnlyList<CalendarDay> BuildCalendar(DateTime reference)
    {
        var today     = reference.Date;
        var firstDay  = new DateTime(today.Year, today.Month, 1);
        // Day-of-week offset so Monday = column 0
        int startCol  = ((int)firstDay.DayOfWeek + 6) % 7; // Mon=0 … Sun=6
        int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        var days = new List<CalendarDay>(42);

        // Leading cells from previous month
        var prevMonth = firstDay.AddMonths(-1);
        int prevDays  = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
        for (int i = startCol - 1; i >= 0; i--)
            days.Add(new CalendarDay((prevDays - i).ToString(), false, false));

        // Current-month cells
        for (int d = 1; d <= daysInMonth; d++)
            days.Add(new CalendarDay(d.ToString(), new DateTime(today.Year, today.Month, d) == today, true));

        // Trailing cells to fill up to 42
        int trailing = 42 - days.Count;
        for (int d = 1; d <= trailing; d++)
            days.Add(new CalendarDay(d.ToString(), false, false));

        return days;
    }

    // ── Observable Properties ──────────────────────────────────────────────────

    /// <summary>Track title, e.g. "Blinding Lights"</summary>
    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    /// <summary>Artist name, e.g. "The Weeknd"</summary>
    public string Artist
    {
        get => _artist;
        set => Set(ref _artist, value);
    }

    /// <summary>Album art bitmap (nullable; show placeholder when null)</summary>
    public BitmapSource? AlbumArt
    {
        get => _albumArt;
        set => Set(ref _albumArt, value);
    }

    /// <summary>True when a track is actively playing (not paused)</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set => Set(ref _isPlaying, value);
    }

    /// <summary>True when the notch is in expanded (media card) mode</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    /// <summary>
    /// True when there is valid media info to display.
    /// Used to auto-expand when a track starts.
    /// </summary>
    public bool HasMedia
    {
        get => _hasMedia;
        set => Set(ref _hasMedia, value);
    }

    /// <summary>
    /// True when the clock/calendar overlay is visible inside the expanded notch.
    /// Toggled by scrolling over the notch while expanded.
    /// </summary>
    public bool IsClockVisible
    {
        get => _isClockVisible;
        set => Set(ref _isClockVisible, value);
    }

    // ── Clock / Calendar Properties ───────────────────────────────────────────

    /// <summary>Current local time formatted as "h:mm tt" (e.g. "8:35 AM").
    /// Shown in the collapsed notch when no media is active.</summary>
    public string CurrentTime
    {
        get => _currentTime;
        set => Set(ref _currentTime, value);
    }

    /// <summary>Large clock hour:minute, e.g. "8:35" (no AM/PM — use ClockPeriod separately).</summary>
    public string ClockTimeLarge
    {
        get => _clockTimeLarge;
        set => Set(ref _clockTimeLarge, value);
    }

    /// <summary>AM or PM label.</summary>
    public string ClockPeriod
    {
        get => _clockPeriod;
        set => Set(ref _clockPeriod, value);
    }

    /// <summary>Full date line, e.g. "Sunday, 11 May".</summary>
    public string ClockDateLine
    {
        get => _clockDateLine;
        set => Set(ref _clockDateLine, value);
    }

    /// <summary>Month + year for the calendar header, e.g. "May 2026".</summary>
    public string CalendarMonth
    {
        get => _calendarMonth;
        set => Set(ref _calendarMonth, value);
    }

    /// <summary>42-item flat list of calendar cells for the current month.</summary>
    public IReadOnlyList<CalendarDay> CalendarDays
    {
        get => _calendarDays;
        set => Set(ref _calendarDays, value);
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    public ICommand PlayPauseCommand { get; }
    public ICommand NextTrackCommand { get; }
    public ICommand PreviousTrackCommand { get; }

    // ── Commands / Actions ─────────────────────────────────────────────────────

    /// <summary>
    /// Toggle expand/collapse when the notch is clicked.
    /// Only expands if there is active media.
    /// </summary>
    public void ToggleExpand()
    {
        if (!HasMedia && !IsExpanded) return;
        IsExpanded = !IsExpanded;
    }

    // ── Media event handler ────────────────────────────────────────────────────

    /// <summary>
    /// Called by MediaService on a background thread.
    /// We marshal to the UI thread before touching properties.
    /// </summary>
    private void OnMediaChanged(object? sender, MediaInfo info)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Title     = info.Title;
            Artist    = info.Artist;
            AlbumArt  = info.AlbumArt;
            IsPlaying = info.IsPlaying;
            HasMedia  = !string.IsNullOrWhiteSpace(info.Title);
        });
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _clockTimer.Stop();
        _mediaService.MediaChanged -= OnMediaChanged;
        _mediaService.Dispose();
    }
}
