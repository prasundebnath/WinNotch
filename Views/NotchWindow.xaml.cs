using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfPoint = System.Windows.Point;
using WinNotch.ViewModels;

namespace WinNotch.Views;

/// <summary>
/// Code-behind for the main notch overlay window.
///
/// NotchPath is a single closed WPF Path rebuilt each SizeChanged frame.
/// It draws the entire notch silhouette in one shot:
///   outward left anti-corner → pill left side → rounded bottom → pill right side → outward right anti-corner
/// Closing along Y=0 is implicit (IsClosed=true). No separate elements needed.
/// </summary>
public partial class NotchWindow : Window
{
    // ── Win32 ────────────────────────────────────────────────────────────────
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const int  GWL_EXSTYLE       = -20;
    private const int  WS_EX_TOOLWINDOW  = 0x00000080; // hide from taskbar & Alt+Tab
    private const int  WS_EX_APPWINDOW   = 0x00040000; // forces taskbar presence — must remove
    private const int  WS_EX_NOACTIVATE  = 0x08000000; // clicks never steal focus

    [DllImport("shell32.dll")]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    private const uint ABM_NEW = 0x0000;
    private const uint ABM_REMOVE = 0x0001;
    private const uint ABM_SETPOS = 0x0003;
    private const uint ABE_TOP = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // Helpers that pick the right overload at runtime (32-bit vs 64-bit)
    private static int GetExStyle(IntPtr hwnd)
        => IntPtr.Size == 8
            ? (int)GetWindowLongPtr64(hwnd, GWL_EXSTYLE)
            : GetWindowLong32(hwnd, GWL_EXSTYLE);

    private static void SetExStyle(IntPtr hwnd, int style)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, (IntPtr)style);
        else
            SetWindowLong32(hwnd, GWL_EXSTYLE, style);
    }

    // ── Spring physics ─────────────────────────────────────────────────────────

    /// <summary>
    /// Damped spring simulator. Tick() integrates one time-step using semi-implicit
    /// Euler and returns true when the spring has settled within tolerance.
    /// ζ (damping ratio) = Damping / (2 * sqrt(Stiffness)).
    /// ζ &lt; 1 → underdamped (bounces), ζ = 1 → critically damped, ζ &gt; 1 → overdamped.
    /// </summary>
    private sealed class SpringValue
    {
        public double Position;
        public double Velocity;
        public double Target;
        public double Stiffness;
        public double Damping;

        public SpringValue(double initial, double stiffness, double damping)
        {
            Position  = Target = initial;
            Stiffness = stiffness;
            Damping   = damping;
        }

        /// <summary>Integrates one sub-step. Returns true when settled.</summary>
        public bool Tick(double dt)
        {
            double disp  = Position - Target;
            double accel = -Stiffness * disp - Damping * Velocity;
            Velocity += accel  * dt;
            Position += Velocity * dt;
            return Math.Abs(disp) < 0.08 && Math.Abs(Velocity) < 0.08;
        }
    }

    // One spring per dimension; constants are updated per animation direction.
    private readonly SpringValue _ws = new(200, 480, 28); // width
    private readonly SpringValue _hs = new(36,  480, 28); // height
    private bool _springing;
    private long _lastTick;

    // ── Fields ───────────────────────────────────────────────────────────────
    private readonly NotchViewModel _vm;
    private IntPtr _hwnd;
    private System.Windows.Threading.DispatcherTimer? _topmostTimer;

    // ── Constructor ───────────────────────────────────────────────────────────

    public NotchWindow()
    {
        InitializeComponent();

        _vm = new NotchViewModel();
        DataContext = _vm;

        _vm.PropertyChanged += Vm_PropertyChanged;
        Loaded += NotchWindow_Loaded;

        // Hook SizeChanged on the border so anti-corners always track its width
        NotchBorder.SizeChanged += NotchBorder_SizeChanged;
    }

    // ── Loaded ────────────────────────────────────────────────────────────────

    private void NotchWindow_Loaded(object sender, RoutedEventArgs e)
    {
        HideFromTaskbar();           // must run before anything else makes the window visible
        PositionFullWidth();
        RegisterAppBar();            // Reserve screen space for the notch
        SetupTopmostEnforcement();
        // Draw the initial notch shape
        RebuildNotchPath(NotchBorder.ActualWidth, NotchBorder.ActualHeight);
    }

    /// <summary>
    /// Applies WS_EX_TOOLWINDOW + WS_EX_NOACTIVATE to the extended window style.
    /// This hides the window from the taskbar, Alt+Tab, and prevents it from
    /// stealing keyboard focus when clicked.
    /// </summary>
    private void HideFromTaskbar()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetExStyle(_hwnd);
        exStyle |=  WS_EX_TOOLWINDOW;   // mark as tool window → off taskbar
        exStyle |=  WS_EX_NOACTIVATE;   // never steal focus
        exStyle &= ~WS_EX_APPWINDOW;    // remove forced-taskbar flag
        SetExStyle(_hwnd, exStyle);
    }

    private void RegisterAppBar()
    {
        var abd = new APPBARDATA();
        abd.cbSize = Marshal.SizeOf(abd);
        abd.hWnd = _hwnd;
        abd.uCallbackMessage = 0x0400 + 100; // WM_USER + 100

        SHAppBarMessage(ABM_NEW, ref abd);

        abd.uEdge = ABE_TOP;
        abd.rc.left = 0;
        abd.rc.right = (int)SystemParameters.PrimaryScreenWidth;
        abd.rc.top = 0;
        abd.rc.bottom = 36; // Reserve 36 logical pixels for the collapsed notch

        SHAppBarMessage(ABM_SETPOS, ref abd);
    }

    private void RemoveAppBar()
    {
        if (_hwnd != IntPtr.Zero)
        {
            var abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = _hwnd;
            SHAppBarMessage(ABM_REMOVE, ref abd);
        }
    }

    // ── Positioning ───────────────────────────────────────────────────────────

    private void PositionFullWidth()
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        Left   = 0;
        Top    = 0;
        Width  = screenWidth;
        Height = 240;
    }

    // ── Anti-corner tracking ──────────────────────────────────────────────────

    /// <summary>
    /// Called every time the NotchBorder resizes (each animation frame).
    /// Repositions the four anti-corner elements so they always sit flush.
    /// </summary>
    private void NotchBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RebuildNotchPath(e.NewSize.Width, e.NewSize.Height);
    }

    /// <summary>
    /// Draws the complete notch silhouette as a single closed WPF StreamGeometry path.
    ///
    /// Shape (reading the outline clockwise from top-left):
    ///   (notchLeft - ac, 0)  — left tip of left anti-corner flange
    ///   → concave Bezier inward → (notchLeft, ac)  — top of pill left edge
    ///   → straight down        → (notchLeft, h - r)
    ///   → convex Bezier        → (notchLeft + r, h) — bottom-left pill corner
    ///   → straight across      → (notchRight - r, h)
    ///   → convex Bezier        → (notchRight, h - r) — bottom-right pill corner
    ///   → straight up          → (notchRight, ac)
    ///   → concave Bezier outward→ (notchRight + ac, 0) — right tip of right anti-corner
    ///   → IsClosed closes along Y=0 back to start
    /// </summary>
    private void RebuildNotchPath(double w, double h)
    {
        // Pill bottom corner radius (r) and anti-corner radius (ac).
        // ac should be ≤ r for a natural look.
        const double r  = 18.0;  // pill corner radius
        const double ac = 14.0;  // anti-corner (outward flange) radius

        double kr  = 0.5523 * r;   // Bezier arc approximation constant
        double kac = 0.5523 * ac;

        double screenW    = ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
        double notchLeft  = (screenW - w) / 2.0;
        double notchRight = notchLeft + w;

        var sg = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = sg.Open())
        {
            // ── Start: left tip of left anti-corner (Y=0, just left of notch) ──
            ctx.BeginFigure(new WpfPoint(notchLeft - ac, 0), isFilled: true, isClosed: true);

            // Left anti-corner: concave curve sweeping from Y=0 inward to the pill left wall
            // Control points produce a tangent-matched quarter-circle arc
            ctx.BezierTo(
                new WpfPoint(notchLeft - ac + kac, 0),   // cp1: tangent at left tip (rightward)
                new WpfPoint(notchLeft, ac - kac),        // cp2: tangent at entry (upward)
                new WpfPoint(notchLeft, ac),              // end: where the pill wall starts
                isStroked: false, isSmoothJoin: true);

            // Left side of pill — straight down
            ctx.LineTo(new WpfPoint(notchLeft, h - r), isStroked: false, isSmoothJoin: false);

            // Bottom-left rounded corner of pill
            ctx.BezierTo(
                new WpfPoint(notchLeft, h - r + kr),
                new WpfPoint(notchLeft + r - kr, h),
                new WpfPoint(notchLeft + r, h),
                isStroked: false, isSmoothJoin: true);

            // Bottom of pill — straight across
            ctx.LineTo(new WpfPoint(notchRight - r, h), isStroked: false, isSmoothJoin: false);

            // Bottom-right rounded corner of pill
            ctx.BezierTo(
                new WpfPoint(notchRight - r + kr, h),
                new WpfPoint(notchRight, h - kr),
                new WpfPoint(notchRight, h - r),
                isStroked: false, isSmoothJoin: true);

            // Right side of pill — straight up
            ctx.LineTo(new WpfPoint(notchRight, ac), isStroked: false, isSmoothJoin: false);

            // Right anti-corner: concave curve sweeping from pill right wall outward to Y=0
            ctx.BezierTo(
                new WpfPoint(notchRight, ac - kac),        // cp1: tangent at entry (upward)
                new WpfPoint(notchRight + ac - kac, 0),   // cp2: tangent at right tip (rightward)
                new WpfPoint(notchRight + ac, 0),          // end: right tip
                isStroked: false, isSmoothJoin: true);

            // IsClosed=true closes back to start along Y=0 (invisible, above screen)
        }
        sg.Freeze();
        NotchPath.Data = sg;
    }

    // ── Always on top ─────────────────────────────────────────────────────────

    private void SetupTopmostEnforcement()
    {
        // _hwnd is already set by HideFromTaskbar() — no need to reassign.
        ForceTopmost();

        _topmostTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _topmostTimer.Tick += (_, _) =>
        {
            ForceTopmost();
            Top = 0;
        };
        _topmostTimer.Start();
    }

    private void ForceTopmost()
    {
        // Do NOT use SWP_SHOWWINDOW here — it can cause Windows to re-add the
        // window to the taskbar on each tick, defeating WS_EX_TOOLWINDOW.
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // ── Mouse handling ────────────────────────────────────────────────────────

    private void NotchBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _vm.IsExpanded = true;
        // In idle mode (no media) go straight to the clock/calendar view.
        if (!_vm.HasMedia)
            _vm.IsClockVisible = true;
    }

    private void NotchBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _vm.IsExpanded    = false;
        _vm.IsClockVisible = false;
    }

    private void NotchBorder_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        // Only allow toggling between media and clock views when music is active.
        // In idle state the calendar is the only view, so scroll does nothing.
        if (!_vm.IsExpanded || !_vm.HasMedia) return;
        _vm.IsClockVisible = !_vm.IsClockVisible;
        e.Handled = true;
    }

    // ── Animation triggers ────────────────────────────────────────────────────

    private void Vm_PropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotchViewModel.IsExpanded))
        {
            if (_vm.IsExpanded) PlayExpand();
            else                PlayCollapse();
        }
        else if (e.PropertyName == nameof(NotchViewModel.IsClockVisible) && _vm.IsExpanded)
        {
            if (_vm.IsClockVisible) PlayClockShow();
            else                    PlayClockHide();
        }
    }

    private void PlayExpand()
    {
        // Opacity handled by storyboard; shape driven by underdamped spring (ζ ≈ 0.65 → bouncy)
        ((Storyboard)Resources["ExpandStoryboard"]).Begin(this);
        LaunchSprings(targetW: 320, targetH: 162, stiffness: 400, damping: 26);
    }

    private void PlayCollapse()
    {
        // Stop the expand storyboard to release its opacity lock on MediaContent.
        ((Storyboard)Resources["ExpandStoryboard"]).Stop(this);

        // Resetting IsClockVisible to false collapses ClockPanel and reveals MediaCard
        // immediately via their Visibility bindings — no stale opacity state possible.
        _vm.IsClockVisible = false;

        ((Storyboard)Resources["CollapseStoryboard"]).Begin(this);
        LaunchSprings(targetW: 200, targetH: 36, stiffness: 750, damping: 46);
    }

    private void PlayClockShow()
    {
        // Visibility binding handles show/hide; spring expands to fit the calendar.
        LaunchSprings(targetW: 440, targetH: 192, stiffness: 420, damping: 28);
    }

    private void PlayClockHide()
    {
        // Spring shrinks back to media-card size.
        LaunchSprings(targetW: 320, targetH: 162, stiffness: 420, damping: 28);
    }

    /// <summary>
    /// Sets spring targets and starts the CompositionTarget.Rendering loop.
    /// If a spring animation is already running, velocity is partially preserved
    /// so direction reversals feel fluid rather than jarring.
    /// </summary>
    private void LaunchSprings(double targetW, double targetH, double stiffness, double damping)
    {
        double cw = NotchBorder.ActualWidth  > 1 ? NotchBorder.ActualWidth  : NotchBorder.Width;
        double ch = NotchBorder.ActualHeight > 1 ? NotchBorder.ActualHeight : NotchBorder.Height;

        // Preserve a fraction of velocity on reversal for natural momentum transfer.
        double velScale = _springing ? 0.25 : 0.0;

        _ws.Position  = cw;
        _ws.Velocity *= velScale;
        _ws.Target    = targetW;
        _ws.Stiffness = stiffness;
        _ws.Damping   = damping;

        _hs.Position  = ch;
        _hs.Velocity *= velScale;
        _hs.Target    = targetH;
        _hs.Stiffness = stiffness;
        _hs.Damping   = damping;

        if (!_springing)
        {
            _springing = true;
            _lastTick  = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnSpringRendering;
        }
    }

    /// <summary>
    /// Runs every display frame. Sub-steps the spring 4x per frame to keep
    /// physics stable at all refresh rates (60 / 120 / 144 Hz).
    /// </summary>
    private void OnSpringRendering(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = (now - _lastTick) / (double)Stopwatch.Frequency;
        _lastTick = now;

        // Clamp dt so a single slow frame can't destabilise the spring.
        const int substeps = 4;
        double sub = Math.Min(dt, 1.0 / 30.0) / substeps;

        bool wDone = false, hDone = false;
        for (int i = 0; i < substeps; i++)
        {
            wDone = _ws.Tick(sub);
            hDone = _hs.Tick(sub);
        }

        NotchBorder.Width  = _ws.Position;
        NotchBorder.Height = _hs.Position;

        if (wDone && hDone)
        {
            NotchBorder.Width  = _ws.Target;
            NotchBorder.Height = _hs.Target;
            _springing = false;
            CompositionTarget.Rendering -= OnSpringRendering;
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        RemoveAppBar(); // Restore screen space
        CompositionTarget.Rendering -= OnSpringRendering; // ensure cleanup
        _topmostTimer?.Stop();
        _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm.Dispose();
        base.OnClosed(e);
    }
}
