using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Threading.Tasks;
using MusicWidget.Services;

namespace MusicWidget.Views;

public partial class WidgetWindow : Window
{
    private bool _settingsExpanded;
    private const double DefaultDashboardWidth = 680;
    private const double DefaultDashboardHeight = 720;
    private const double MinDashboardWidth = 380;
    private const double MinDashboardHeight = 400;
    /// <summary>
    /// Pixels below the work area's top edge where the widget sits by default.
    /// Stays in sync between first-launch positioning and the Reset Layout action.
    /// </summary>
    private const double DefaultTopOffset = 16;
    private double _settingsTargetHeight = DefaultDashboardHeight;
    private bool _resizingDashboard;
    private HotkeyService? _hotkeys;

    private bool _userMovedWidget;

    public WidgetWindow()
    {
        InitializeComponent();
        Opacity = 0;

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - 280) / 2;
        Top = workArea.Top + DefaultTopOffset;

        SizeChanged += WidgetWindow_SizeChanged;
        Loaded += WidgetWindow_Loaded;
        Closed += WidgetWindow_Closed;

        App.Player.PlayStateChanged += OnPlayStateChanged;
        App.Player.CurrentTrackChanged += OnCurrentTrackChanged;
        App.Player.LoopCurrentChanged += OnLoopCurrentChanged;

        UpdateLoopIcon();
        UpdatePlayPauseIcon();
    }

    private void WidgetWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyDefaultDashboardSize();
        ScheduleStartupCentering();

        _hotkeys = new HotkeyService(this);
        _hotkeys.ToggleVisibilityPressed += OnToggleVisibilityHotkey;
        _hotkeys.ToggleSettingsPressed += OnToggleSettingsHotkey;
        _hotkeys.ResetLayoutPressed += OnResetLayoutHotkey;
        _hotkeys.Register();

        _ = App.Tools.EnsureToolsAsync().ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception is not null)
            {
                CrashLog.Write(t.Exception.GetBaseException(), "WidgetWindow.EnsureToolsAsync");
            }
        }, TaskScheduler.Default);
    }

    private void ScheduleStartupCentering()
    {
        // Drop legacy pinned coords — every cold start centers the pill.
        var settings = App.Settings.Current;
        if (settings.WindowPositionPinned)
        {
            settings.WindowPositionPinned = false;
            settings.WindowLeft = null;
            settings.WindowTop = null;
            App.Settings.Save();
        }

        void OnInitialContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= OnInitialContentRendered;
            CenterPillOnScreen();
        }

        ContentRendered += OnInitialContentRendered;
        Dispatcher.BeginInvoke(new Action(CenterPillOnScreen), DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            CenterPillOnScreen();
            RevealWindow();
        }), DispatcherPriority.ApplicationIdle);
    }

    private void WidgetWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || IsBeingDragged || _userMovedWidget || _settingsExpanded ||
            _resizingDashboard || _animatingSettings)
        {
            return;
        }

        CenterPillOnScreen();
    }

    private void RevealWindow()
    {
        Opacity = 1;
    }

    private static bool IsPositionOnScreen(double left, double top)
    {
        var workArea = SystemParameters.WorkArea;
        const double minVisible = 48;
        return left + minVisible > workArea.Left &&
               left < workArea.Right &&
               top + minVisible > workArea.Top &&
               top < workArea.Bottom;
    }

    private void ApplyDefaultDashboardSize()
    {
        var workArea = SystemParameters.WorkArea;
        var maxW = Math.Max(MinDashboardWidth, workArea.Width - 40);
        var maxH = Math.Max(MinDashboardHeight, workArea.Height - 80);

        SettingsPanelControl.Width = Math.Clamp(DefaultDashboardWidth, MinDashboardWidth, maxW);
        _settingsTargetHeight = Math.Clamp(DefaultDashboardHeight, MinDashboardHeight, maxH);
    }

    /// <summary>
    /// Nudges the window so the pill sits at the horizontal center of the work area.
    /// Uses screen-space delta math so it stays correct after SizeToContent resizes.
    /// </summary>
    private void CenterPillOnScreen()
    {
        try
        {
            if (!IsLoaded)
            {
                return;
            }

            UpdateLayout();
            if (PillBorder.ActualWidth <= 0)
            {
                return;
            }

            var workArea = SystemParameters.WorkArea;
            var targetCenterX = workArea.Left + workArea.Width / 2.0;
            Left += targetCenterX - GetPillScreenCenterX();
            Top = workArea.Top + DefaultTopOffset;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "WidgetWindow.CenterPillOnScreen");
        }
    }

    private void ApplyDefaultPosition() => CenterPillOnScreen();

    private double GetPillScreenCenterX()
    {
        try
        {
            var topLeft = PillBorder.PointToScreen(new Point(0, 0));
            return topLeft.X + PillBorder.ActualWidth / 2;
        }
        catch
        {
            return Left + ActualWidth / 2;
        }
    }

    /// <summary>
    /// After the window width changes (dashboard open/close), nudge Left so the pill
    /// stays at the same screen X.
    /// </summary>
    private void PreservePillScreenX(double pillCenterXBefore)
    {
        UpdateLayout();
        Left += pillCenterXBefore - GetPillScreenCenterX();
    }

    /// <summary>
    /// Computes the canonical default widget top and the left edge when the pill is
    /// horizontally centered in the work area.
    /// </summary>
    private (double Left, double Top) ComputeDefaultPosition()
    {
        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        var top = workArea.Top + DefaultTopOffset;
        if (PillBorder.ActualWidth <= 0)
        {
            return (Left, top);
        }

        var targetCenterX = workArea.Left + workArea.Width / 2.0;
        var left = Left + targetCenterX - GetPillScreenCenterX();
        return (left, top);
    }

    private void WidgetWindow_Closed(object? sender, EventArgs e)
    {
        App.Player.PlayStateChanged -= OnPlayStateChanged;
        App.Player.CurrentTrackChanged -= OnCurrentTrackChanged;
        App.Player.LoopCurrentChanged -= OnLoopCurrentChanged;

        _hotkeys?.Dispose();
        Application.Current.Shutdown();
    }

    private void OnToggleVisibilityHotkey(object? sender, EventArgs e)
    {
        ToggleVisibility();
    }

    private void OnToggleSettingsHotkey(object? sender, EventArgs e)
    {
        // Make sure the widget is on-screen before opening settings, otherwise the
        // panel would expand from a hidden pill.
        if (!IsLogicallyVisible)
        {
            ToggleVisibility();
        }
        ToggleSettings();
    }

    private void OnResetLayoutHotkey(object? sender, EventArgs e)
    {
        // Hotkey path: skip the confirmation prompt (the user already typed a
        // three-key combo intentionally) and bring the widget back on-screen first
        // so they can see the reset land.
        if (!IsLogicallyVisible)
        {
            ToggleVisibility();
        }
        ResetLayoutToDefaults();
    }

    public event EventHandler<bool>? VisibilityToggleRequested;

    public bool IsLogicallyVisible { get; private set; } = true;

    private bool _animatingVisibility;

    public void ToggleVisibility()
    {
        if (_animatingVisibility)
        {
            return;
        }

        var hiding = IsLogicallyVisible;
        IsLogicallyVisible = !hiding;
        _animatingVisibility = true;
        VisibilityToggleRequested?.Invoke(this, IsLogicallyVisible);

        void OnDone() => _animatingVisibility = false;

        if (hiding)
        {
            CollapseSettingsImmediate();
            WindowAnimator.SlideOffNearestEdge(this, OnDone);
        }
        else
        {
            WindowAnimator.SlideBackIn(this, OnDone);
        }
    }

    /// <summary>
    /// Hides the widget off-screen (same as Ctrl+\ or the top chevron). Dashboard
    /// collapses first so only the pill slides away.
    /// </summary>
    public void MinimizeWidget()
    {
        if (!IsLogicallyVisible || _animatingVisibility)
        {
            return;
        }

        ToggleVisibility();
    }

    /// <summary>
    /// True while the user is actively dragging the widget pill. The HideToggleWindow
    /// reads this and freezes the chevron tab in place during the drag so the tab
    /// doesn't slide around with the widget.
    /// </summary>
    public bool IsBeingDragged { get; private set; }

    private void MoveThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        IsBeingDragged = true;
    }

    private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Left += e.HorizontalChange;
        Top += e.VerticalChange;
    }

    private void MoveThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        IsBeingDragged = false;
        if (Math.Abs(e.HorizontalChange) > 3 || Math.Abs(e.VerticalChange) > 3)
        {
            _userMovedWidget = true;
        }
    }

    private bool _animatingSettings;

    private void Gear_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettings();
    }

    private void ToggleSettings()
    {
        if (_animatingSettings)
        {
            // Drop the spam: only one open/close animation runs at a time.
            return;
        }
        AnimateSettings(!_settingsExpanded);
    }

    private void AnimateSettings(bool open)
    {
        _settingsExpanded = open;
        _animatingSettings = true;

        var pillCenterXBefore = GetPillScreenCenterX();

        if (open)
        {
            SettingsHost.Visibility = Visibility.Visible;
            UpdateLayout();
            PreservePillScreenX(pillCenterXBefore);
        }

        double from = SettingsHost.Height;
        double to = open ? _settingsTargetHeight : 0;

        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        anim.Completed += (_, _) =>
        {
            SettingsHost.BeginAnimation(HeightProperty, null);
            SettingsHost.Height = to;
            if (!open)
            {
                SettingsHost.Visibility = Visibility.Collapsed;
                PreservePillScreenX(pillCenterXBefore);
            }

            _animatingSettings = false;
        };
        SettingsHost.BeginAnimation(HeightProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    private void CollapseSettingsImmediate()
    {
        if (!_settingsExpanded && SettingsHost.Height <= 0)
        {
            _animatingSettings = false;
            return;
        }

        _settingsExpanded = false;
        _animatingSettings = false;
        var pillCenterXBefore = GetPillScreenCenterX();
        SettingsHost.BeginAnimation(HeightProperty, null);
        SettingsHost.Height = 0;
        SettingsHost.Visibility = Visibility.Collapsed;
        PreservePillScreenX(pillCenterXBefore);
    }

    // ----- Dashboard resize grip -----

    private void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (!_settingsExpanded || _animatingSettings)
        {
            // Drop the gesture; can't resize while the panel is mid open/close animation.
            _resizingDashboard = false;
            return;
        }

        _resizingDashboard = true;

        // Stop any held animation so subsequent direct Height writes own the property.
        SettingsHost.BeginAnimation(HeightProperty, null);
        SettingsHost.Height = _settingsTargetHeight;
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_resizingDashboard) return;

        var workArea = SystemParameters.WorkArea;
        var maxW = Math.Max(MinDashboardWidth, workArea.Width - 40);
        var maxH = Math.Max(MinDashboardHeight, workArea.Height - 80);

        var currentW = double.IsNaN(SettingsPanelControl.Width) || SettingsPanelControl.Width <= 0
            ? SettingsPanelControl.ActualWidth
            : SettingsPanelControl.Width;
        var newW = Math.Clamp(currentW + e.HorizontalChange, MinDashboardWidth, maxW);
        var newH = Math.Clamp(_settingsTargetHeight + e.VerticalChange, MinDashboardHeight, maxH);

        SettingsPanelControl.Width = newW;
        _settingsTargetHeight = newH;
        SettingsHost.Height = newH;
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_resizingDashboard) return;
        _resizingDashboard = false;

        // The new dimensions are intentionally not persisted: every launch resets
        // the layout to defaults (see ClearPersistedLayoutOverrides in
        // WidgetWindow_Loaded). Resizing only affects the current session.
    }

    // ----- Reset layout -----

    /// <summary>
    /// Restores the widget position and dashboard dimensions to their first-launch
    /// defaults: centered horizontally at the top of the work area, default dashboard
    /// width/height, with the panel keeping its current open/closed state.
    /// </summary>
    public void ResetLayoutToDefaults()
    {
        // 1. Clamp the defaults against the current screen so this still does the right
        //    thing on smaller monitors. The user can have any work area, so we always
        //    re-validate against MinDashboard*/work-area-derived caps.
        var workArea = SystemParameters.WorkArea;
        var maxW = Math.Max(MinDashboardWidth, workArea.Width - 40);
        var maxH = Math.Max(MinDashboardHeight, workArea.Height - 80);

        var newW = Math.Clamp(DefaultDashboardWidth, MinDashboardWidth, maxW);
        var newH = Math.Clamp(DefaultDashboardHeight, MinDashboardHeight, maxH);

        // 2. Snap dashboard dimensions back. If the panel is open we animate the
        //    height change so the user sees the reset happening.
        SettingsPanelControl.Width = newW;
        _settingsTargetHeight = newH;

        // 2b. Restore the playlist <-> songs splitter inside the dashboard so the
        //     reset is full ("everything"), regardless of how this method is triggered
        //     (button, Ctrl+Shift+\ hotkey, or future callers).
        SettingsPanelControl.ResetSplitter();

        if (_settingsExpanded)
        {
            SettingsHost.Visibility = Visibility.Visible;
            SettingsHost.BeginAnimation(HeightProperty, null);
            var heightAnim = new DoubleAnimation
            {
                From = SettingsHost.Height,
                To = newH,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            heightAnim.Completed += (_, _) =>
            {
                SettingsHost.BeginAnimation(HeightProperty, null);
                SettingsHost.Height = newH;
            };
            SettingsHost.BeginAnimation(HeightProperty, heightAnim, HandoffBehavior.SnapshotAndReplace);
        }

        // 3. Clear persisted overrides so the next launch picks up the same defaults.
        App.Settings.Current.WindowLeft = null;
        App.Settings.Current.WindowTop = null;
        App.Settings.Current.WindowPositionPinned = false;
        App.Settings.Current.DashboardWidth = null;
        App.Settings.Current.DashboardHeight = null;
        App.Settings.Save();

        _userMovedWidget = false;

        // 4. Animate the window back to the default position. Defer until layout has
        //    settled — width may have just changed, which affects ActualWidth and
        //    therefore the centering math.
        Dispatcher.BeginInvoke(new Action(AnimateBackToDefaultPosition), DispatcherPriority.Loaded);
    }

    private void AnimateBackToDefaultPosition()
    {
        var (defaultLeft, defaultTop) = ComputeDefaultPosition();

        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

        var leftAnim = new DoubleAnimation
        {
            From = Left,
            To = defaultLeft,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var topAnim = new DoubleAnimation
        {
            From = Top,
            To = defaultTop,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        leftAnim.Completed += (_, _) =>
        {
            BeginAnimation(LeftProperty, null);
            Left = defaultLeft;
        };
        topAnim.Completed += (_, _) =>
        {
            BeginAnimation(TopProperty, null);
            Top = defaultTop;
        };
        BeginAnimation(LeftProperty, leftAnim, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(TopProperty, topAnim, HandoffBehavior.SnapshotAndReplace);
    }

    private void Loop_Click(object sender, RoutedEventArgs e)
    {
        App.Player.SetLoopCurrent(!App.Player.LoopCurrent);
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        App.Player.Previous();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        App.Player.TogglePlayPause();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        App.Player.Next();
    }

    private void OnPlayStateChanged(object? sender, EventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(UpdatePlayPauseIcon);
    }

    private void OnCurrentTrackChanged(object? sender, EventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(() =>
        {
            try
            {
                var track = App.Player.CurrentTrack;
                if (track is null)
                {
                    NowPlayingLabel.BeginAnimation(OpacityProperty, null);
                    NowPlayingLabel.Opacity = 0;
                    NowPlayingLabel.Text = string.Empty;
                    return;
                }

                NowPlayingLabel.Text = track.DisplayName;
                FlashNowPlayingLabel();
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "WidgetWindow.OnCurrentTrackChanged");
            }
        });
    }

    /// <summary>
    /// Quickly fades the now-playing label in, holds it for 3 seconds, then fades it out.
    /// Called whenever the player advances to a new track. Using SnapshotAndReplace lets
    /// rapid track skips restart the animation cleanly without flicker.
    /// </summary>
    private void FlashNowPlayingLabel()
    {
        const double fadeInMs = 200;
        const double holdMs = 3000;
        const double fadeOutMs = 500;
        var totalMs = fadeInMs + holdMs + fadeOutMs;

        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(totalMs),
            FillBehavior = FillBehavior.HoldEnd,
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(
            0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(
            1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(fadeInMs)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(
            1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(fadeInMs + holdMs))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(
            0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(totalMs)),
            new CubicEase { EasingMode = EasingMode.EaseIn }));

        anim.Completed += (_, _) =>
        {
            // Detach the held animation so the local Opacity value sticks at 0; otherwise
            // a subsequent text assignment could appear at the wrong opacity.
            NowPlayingLabel.BeginAnimation(OpacityProperty, null);
            NowPlayingLabel.Opacity = 0;
        };

        NowPlayingLabel.BeginAnimation(OpacityProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnLoopCurrentChanged(object? sender, EventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(UpdateLoopIcon);
    }

    private void UpdatePlayPauseIcon()
    {
        var key = App.Player.IsPlaying ? "Icon.Pause" : "Icon.Play";
        PlayPauseIcon.Data = (Geometry)Application.Current.Resources[key];
    }

    private void UpdateLoopIcon()
    {
        var on = App.Player.LoopCurrent;
        if (on)
        {
            LoopButton.Background = (Brush)Application.Current.Resources["Brush.BlueDeep"];
            LoopIcon.Stroke = Brushes.White;
            LoopIcon.StrokeThickness = 2.15;
            LoopOneMark.Visibility = Visibility.Visible;
            LoopButton.ToolTip = "Loop: ON (current song)";
        }
        else
        {
            LoopButton.Background = (Brush)Application.Current.Resources["Brush.Blue"];
            LoopIcon.Stroke = Brushes.White;
            LoopIcon.StrokeThickness = 1.9;
            LoopOneMark.Visibility = Visibility.Collapsed;
            LoopButton.ToolTip = "Loop: OFF";
        }
    }
}
