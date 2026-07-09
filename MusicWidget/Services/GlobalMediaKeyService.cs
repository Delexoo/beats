using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MusicWidget.Services;

/// <summary>
/// Listens for headset / keyboard media keys (play-pause, next, previous) and routes
/// them to the active player. Uses global hotkeys plus WM_APPCOMMAND as a fallback.
/// </summary>
public sealed class GlobalMediaKeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_APPCOMMAND = 0x319;

    private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
    private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;
    private const int APPCOMMAND_MEDIA_STOP = 13;
    private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
    private const int APPCOMMAND_MEDIA_PLAY = 46;
    private const int APPCOMMAND_MEDIA_PAUSE = 47;

    private const uint VK_MEDIA_NEXT_TRACK = 0xB0;
    private const uint VK_MEDIA_PREV_TRACK = 0xB1;
    private const uint VK_MEDIA_STOP = 0xB4;
    private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;

    private const int HOTKEY_ID_MEDIA_PLAY_PAUSE = 0x4D5A;
    private const int HOTKEY_ID_MEDIA_NEXT = 0x4D5B;
    private const int HOTKEY_ID_MEDIA_PREV = 0x4D5C;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private HwndSource? _source;
    private bool _playPauseRegistered;
    private bool _nextRegistered;
    private bool _previousRegistered;

    public GlobalMediaKeyService(Window window)
    {
        _window = window;
    }

    public void Register()
    {
        var hwnd = new WindowInteropHelper(_window).EnsureHandle();
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        // Global multimedia keys (AirPods, Bluetooth headsets, keyboard media keys).
        _playPauseRegistered = RegisterHotKey(hwnd, HOTKEY_ID_MEDIA_PLAY_PAUSE, 0, VK_MEDIA_PLAY_PAUSE);
        _nextRegistered = RegisterHotKey(hwnd, HOTKEY_ID_MEDIA_NEXT, 0, VK_MEDIA_NEXT_TRACK);
        _previousRegistered = RegisterHotKey(hwnd, HOTKEY_ID_MEDIA_PREV, 0, VK_MEDIA_PREV_TRACK);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case HOTKEY_ID_MEDIA_PLAY_PAUSE:
                    App.Player.TogglePlayPause();
                    handled = true;
                    break;
                case HOTKEY_ID_MEDIA_NEXT:
                    App.Player.Next();
                    handled = true;
                    break;
                case HOTKEY_ID_MEDIA_PREV:
                    App.Player.Previous();
                    handled = true;
                    break;
            }

            return IntPtr.Zero;
        }

        if (msg == WM_APPCOMMAND)
        {
            var command = (int)((wParam.ToInt64() >> 16) & 0xFFF);
            switch (command)
            {
                case APPCOMMAND_MEDIA_PLAY_PAUSE:
                case APPCOMMAND_MEDIA_PLAY:
                case APPCOMMAND_MEDIA_PAUSE:
                    App.Player.TogglePlayPause();
                    handled = true;
                    break;
                case APPCOMMAND_MEDIA_NEXTTRACK:
                    App.Player.Next();
                    handled = true;
                    break;
                case APPCOMMAND_MEDIA_PREVIOUSTRACK:
                    App.Player.Previous();
                    handled = true;
                    break;
                case APPCOMMAND_MEDIA_STOP:
                    if (App.Player.IsPlaying)
                    {
                        App.Player.TogglePlayPause();
                    }

                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        var hwnd = _source?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            if (_playPauseRegistered)
            {
                UnregisterHotKey(hwnd, HOTKEY_ID_MEDIA_PLAY_PAUSE);
            }

            if (_nextRegistered)
            {
                UnregisterHotKey(hwnd, HOTKEY_ID_MEDIA_NEXT);
            }

            if (_previousRegistered)
            {
                UnregisterHotKey(hwnd, HOTKEY_ID_MEDIA_PREV);
            }
        }

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }
}
