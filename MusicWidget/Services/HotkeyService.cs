using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MusicWidget.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int VK_OEM_5 = 0xDC; // '\' on US keyboards

    // Distinct WM_HOTKEY ids so we know which binding fired.
    private const int HOTKEY_ID_TOGGLE = 0x4D57;   // Alt + \           -> hide/show widget
    private const int HOTKEY_ID_SETTINGS = 0x4D58; // Ctrl + \          -> open settings
    private const int HOTKEY_ID_RESET = 0x4D59;    // Ctrl + Shift + \  -> reset layout

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private HwndSource? _source;
    private bool _toggleRegistered;
    private bool _settingsRegistered;
    private bool _resetRegistered;

    /// <summary>Raised when Alt + \ is pressed.</summary>
    public event EventHandler? ToggleVisibilityPressed;

    /// <summary>Raised when Ctrl + \ is pressed.</summary>
    public event EventHandler? ToggleSettingsPressed;

    /// <summary>Raised when Ctrl + Shift + \ is pressed. Triggers the layout reset.</summary>
    public event EventHandler? ResetLayoutPressed;

    public HotkeyService(Window window)
    {
        _window = window;
    }

    public void Register()
    {
        var helper = new WindowInteropHelper(_window);
        var hwnd = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        // Failures (e.g. the key combo is already owned by another app) are silent;
        // the chevron toggle and gear button still work either way.
        _toggleRegistered = RegisterHotKey(hwnd, HOTKEY_ID_TOGGLE, MOD_ALT, VK_OEM_5);
        _settingsRegistered = RegisterHotKey(hwnd, HOTKEY_ID_SETTINGS, MOD_CONTROL, VK_OEM_5);
        _resetRegistered = RegisterHotKey(hwnd, HOTKEY_ID_RESET, MOD_CONTROL | MOD_SHIFT, VK_OEM_5);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case HOTKEY_ID_TOGGLE:
                ToggleVisibilityPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
            case HOTKEY_ID_SETTINGS:
                ToggleSettingsPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
            case HOTKEY_ID_RESET:
                ResetLayoutPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        try
        {
            var helper = new WindowInteropHelper(_window);
            var hwnd = helper.Handle;
            if (_toggleRegistered)
            {
                UnregisterHotKey(hwnd, HOTKEY_ID_TOGGLE);
                _toggleRegistered = false;
            }
            if (_settingsRegistered)
            {
                UnregisterHotKey(hwnd, HOTKEY_ID_SETTINGS);
                _settingsRegistered = false;
            }
            if (_resetRegistered)
            {
                UnregisterHotKey(hwnd, HOTKEY_ID_RESET);
                _resetRegistered = false;
            }
        }
        catch { /* shutdown best-effort */ }

        if (_source is not null)
        {
            try { _source.RemoveHook(WndProc); } catch { }
            _source = null;
        }
    }
}
