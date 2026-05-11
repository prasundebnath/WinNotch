using System.Windows;
using System.Windows.Forms; // For NotifyIcon (system tray)
using Microsoft.Win32;
using WinNotch.Views;

namespace WinNotch;

/// <summary>
/// Application entry point.
/// Hosts the notch window and a system tray icon for graceful exit.
/// </summary>
public partial class App : System.Windows.Application
{
    private NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterStartup();
        SetupTrayIcon();
    }

    /// <summary>
    /// Registers WinNotch in the Windows startup registry key so it
    /// launches automatically every time the user logs in.
    /// Uses HKCU so no administrator privileges are required.
    /// </summary>
    private static void RegisterStartup()
    {
        const string appName = "WinNotch";
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

        if (key is null) return;

        // Only write if not already registered (or if path changed after reinstall)
        string exePath = $"\"{Environment.ProcessPath}\"";
        string? current = key.GetValue(appName) as string;
        if (!string.Equals(current, exePath, StringComparison.OrdinalIgnoreCase))
            key.SetValue(appName, exePath);
    }

    /// <summary>
    /// Creates a minimal system tray icon so the user can right-click → Exit
    /// without needing a taskbar button (notch window is not in the taskbar).
    /// </summary>
    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "WinNotch – Media Notch",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Exit WinNotch", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            Shutdown();
        });

        _trayIcon.ContextMenuStrip = contextMenu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
