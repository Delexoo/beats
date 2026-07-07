using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    /// <summary>Collapsed pill width: padding 20 + art/info 262 + transport 102.</summary>
    private const double DefaultPillWidthEstimate = 282;
    /// <summary>20px buttons + 5px right margin — matches website .pill-hover-controls.</summary>
    private const double HoverControlsExpandedWidth = 25;
    private const double HoverWidthAnimMs = 280;
    private static readonly IEasingFunction HoverWidthEase = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction HoverCollapseEase = new CubicEase { EasingMode = EasingMode.EaseIn };
    private DispatcherTimer? _hoverCollapseTimer;
    private double _hoverClipWidth;
    private bool _hoverAnimTargetExpanded;
    private double _hoverAnimTargetWidth;
    private double _settingsTargetHeight = DefaultDashboardHeight;
    private bool _resizingDashboard;
    private HotkeyService? _hotkeys;

    private bool _userMovedWidget;
    private bool _hoverExpanded;
    private bool _animatingHover;
    private double _pillBaseWidth;
    private double _hoverAnchorCenterX;
    private bool _settingsAnimOpen;
    private double _settingsAnimPillCenterX;
    private bool _awaitingHoverCollapseBeforeCloseComplete;
    private MusicWidget.Models.Track? _boundTrack;

    public WidgetWindow()
    {
        InitializeComponent();
        Opacity = 0;

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - DefaultPillWidthEstimate) / 2;
        Top = workArea.Top + DefaultTopOffset;

        SizeChanged += WidgetWindow_SizeChanged;
        Loaded += WidgetWindow_Loaded;
        Closed += WidgetWindow_Closed;

        App.Player.PlayStateChanged += OnPlayStateChanged;
        App.Player.CurrentTrackChanged += OnCurrentTrackChanged;
        App.Player.LoopCurrentChanged += OnLoopCurrentChanged;

        UpdateLoopIcon();
        UpdatePlayPauseIcon();
        BindNowPlayingTrack(App.Player.CurrentTrack);
    }

    private void WidgetWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyDefaultDashboardSize();
        ScheduleStartupCentering();
        Dispatcher.BeginInvoke(new Action(ApplyKeepWidgetExpandedPreference), DispatcherPriority.Loaded);

        _hoverCollapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _hoverCollapseTimer.Tick += HoverCollapseTimer_Tick;

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
        if (_resizingDashboard && !IsBeingDragged)
        {
            if (e.WidthChanged && _settingsExpanded)
            {
                SyncPillDuringDashboardResize();
            }

            return;
        }

        if (e.WidthChanged && _animatingSettings && !IsBeingDragged)
        {
            if (_settingsAnimOpen)
            {
                AlignPillOverDashboardPreservingScreenX(_settingsAnimPillCenterX);
            }
            else
            {
                PreservePillScreenX(_hoverAnchorCenterX);
            }

            return;
        }

        if (!e.WidthChanged || IsBeingDragged || _userMovedWidget || _settingsExpanded ||
            _animatingSettings || _animatingHover || _hoverExpanded)
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
        UpdatePillDashboardAlignment();
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
        try
        {
            if (!IsLoaded || IsBeingDragged)
            {
                return;
            }

            var delta = pillCenterXBefore - GetPillScreenCenterX();
            if (Math.Abs(delta) < 0.5)
            {
                return;
            }

            Left += delta;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "WidgetWindow.PreservePillScreenX");
        }
    }

    private void AlignPillOverDashboardPreservingScreenX(double pillCenterX)
    {
        UpdatePillDashboardAlignment();
        UpdateLayout();
        PreservePillScreenX(pillCenterX);
    }

    private bool IsDashboardHosted =>
        SettingsHost.Visibility == Visibility.Visible;

    /// <summary>
    /// True while the dashboard is opening/open (host visible + expanded flag).
    /// Height is intentionally excluded so the pill centers as soon as the window
    /// widens, not after the height animation passes zero.
    /// </summary>
    private bool ShouldAlignPillOverDashboard =>
        _settingsExpanded && IsDashboardHosted;

    private bool ShouldKeepWidgetExpanded() =>
        App.Settings.Current.KeepWidgetExpanded ||
        (_settingsExpanded && IsDashboardHosted && (_settingsAnimOpen || !_animatingSettings));

    /// <summary>
    /// Applies the dashboard "Extended widget" preference to the pill hover controls.
    /// </summary>
    public void ApplyKeepWidgetExpandedPreference()
    {
        if (ShouldKeepWidgetExpanded())
        {
            _hoverAnchorCenterX = GetPillScreenCenterX();
            SetHoverControlsExpanded(true, immediate: true, force: true);
            UpdatePillLayoutDuringHover();
            return;
        }

        if (!PillBorder.IsMouseOver)
        {
            SetHoverControlsExpanded(false, immediate: true, force: true);
        }
    }

    /// <summary>
    /// Keeps the pill centered over the dashboard when open, or pinned to its screen
    /// center while hover expands/collapses when the dashboard is closed.
    /// </summary>
    private void UpdatePillLayoutDuringHover()
    {
        if (IsBeingDragged)
        {
            return;
        }

        if (_animatingSettings)
        {
            if (ShouldAlignPillOverDashboard)
            {
                UpdatePillDashboardAlignment();
            }
            else if (_hoverExpanded || _animatingHover)
            {
                PreservePillScreenX(_hoverAnchorCenterX);
            }

            return;
        }

        if (ShouldAlignPillOverDashboard)
        {
            UpdatePillDashboardAlignment();
            return;
        }

        if (_hoverExpanded || _animatingHover)
        {
            PreservePillScreenX(_hoverAnchorCenterX);
        }
    }

    /// <summary>
    /// When the dashboard is open, center the pill stack over the panel
    /// (including while hover expands/collapses the pill width).
    /// </summary>
    private void UpdatePillDashboardAlignment()
    {
        try
        {
            if (!IsLoaded || IsBeingDragged)
            {
                return;
            }

            PillBorder.HorizontalAlignment = HorizontalAlignment.Left;
            PillBorder.BeginAnimation(FrameworkElement.MarginProperty, null);
            PillBorder.Margin = new Thickness(0);

            PillStack.HorizontalAlignment = ShouldAlignPillOverDashboard
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "WidgetWindow.UpdatePillDashboardAlignment");
        }
    }

    private void ResetPillDashboardAlignment()
    {
        PillStack.HorizontalAlignment = HorizontalAlignment.Left;
        PillBorder.BeginAnimation(FrameworkElement.MarginProperty, null);
        PillBorder.Margin = new Thickness(0);
    }

    /// <summary>
    /// Keeps the pill aligned over the dashboard while preserving its screen X during resize.
    /// Layout must run before measuring pill/dashboard widths.
    /// </summary>
    private void SyncPillDuringDashboardResize()
    {
        if (!ShouldAlignPillOverDashboard)
        {
            return;
        }

        UpdateLayout();
        UpdatePillDashboardAlignment();
        UpdateLayout();
        PreservePillScreenX(_pillCenterXBeforeResize);
    }

    private void CachePillBaseWidth()
    {
        if (!IsLoaded)
        {
            _pillBaseWidth = DefaultPillWidthEstimate;
            return;
        }

        UpdateLayout();
        var measured = PillBorder.ActualWidth - _hoverClipWidth;
        _pillBaseWidth = measured > 0 ? measured : DefaultPillWidthEstimate;
    }

    private double GetEffectivePillWidth()
    {
        var measured = PillBorder.ActualWidth;
        if (measured > 0)
        {
            return measured;
        }

        var clipW = HoverControlsClip.Width;
        if (double.IsNaN(clipW) || clipW < 0)
        {
            clipW = _hoverClipWidth;
        }

        return _pillBaseWidth + clipW;
    }

    private double GetDashboardWidth()
    {
        var dashboardW = SettingsPanelControl.Width;
        if (double.IsNaN(dashboardW) || dashboardW <= 0)
        {
            dashboardW = SettingsHost.ActualWidth;
        }

        if (dashboardW <= 0)
        {
            dashboardW = SettingsPanelControl.ActualWidth;
        }

        return dashboardW;
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

        if (_boundTrack is not null)
        {
            _boundTrack.PropertyChanged -= BoundTrack_PropertyChanged;
            _boundTrack = null;
        }

        if (_hoverCollapseTimer is not null)
        {
            _hoverCollapseTimer.Stop();
            _hoverCollapseTimer.Tick -= HoverCollapseTimer_Tick;
            _hoverCollapseTimer = null;
        }

        HoverControlsClip.BeginAnimation(FrameworkElement.WidthProperty, null);
        SettingsHost.BeginAnimation(HeightProperty, null);
        PillBorder.BeginAnimation(FrameworkElement.MarginProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

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
            SetHoverControlsExpanded(false, immediate: true, force: true);
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
        if (!IsLogicallyVisible)
        {
            return;
        }

        if (_animatingVisibility)
        {
            Dispatcher.BeginInvoke(MinimizeWidget, DispatcherPriority.ApplicationIdle);
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

        _hoverCollapseTimer?.Stop();
        _animatingHover = false;

        HoverControlsClip.BeginAnimation(FrameworkElement.WidthProperty, null);
        PillBorder.BeginAnimation(FrameworkElement.MarginProperty, null);
        HoverControlsClip.Width = _hoverClipWidth;
        PillBorder.BeginAnimation(FrameworkElement.MarginProperty, null);
        PillBorder.Margin = new Thickness(0);

        IsBeingDragged = true;
    }

    private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!IsBeingDragged)
        {
            return;
        }

        Left += e.HorizontalChange;
        Top += e.VerticalChange;
    }

    private void MoveThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        IsBeingDragged = false;
        _hoverAnchorCenterX = GetPillScreenCenterX();

        if (Math.Abs(e.HorizontalChange) > 3 || Math.Abs(e.VerticalChange) > 3)
        {
            _userMovedWidget = true;
        }
    }

    private void PillBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        _hoverCollapseTimer?.Stop();

        if (IsBeingDragged || _animatingSettings)
        {
            return;
        }

        SetHoverControlsExpanded(true);
    }

    private void PillBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        if (IsBeingDragged || _animatingSettings || ShouldKeepWidgetExpanded())
        {
            return;
        }

        // Debounced leave — avoids flicker when crossing child elements.
        _hoverCollapseTimer?.Stop();
        _hoverCollapseTimer?.Start();
    }

    private void HoverCollapseTimer_Tick(object? sender, EventArgs e)
    {
        _hoverCollapseTimer?.Stop();
        if (PillBorder.IsMouseOver || IsBeingDragged || ShouldKeepWidgetExpanded() || _animatingSettings)
        {
            return;
        }

        SetHoverControlsExpanded(false);
    }

    private void SetHoverControlsExpanded(bool expanded, bool immediate = false, bool force = false)
    {
        if (!force && IsBeingDragged)
        {
            return;
        }

        // While the dashboard opens, defer hover expansion until the height animation finishes.
        if (!force && _animatingSettings && expanded)
        {
            return;
        }

        if (!force && !expanded && ShouldKeepWidgetExpanded())
        {
            return;
        }

        if (!immediate && !_animatingHover && _hoverExpanded == expanded)
        {
            return;
        }

        _hoverExpanded = expanded;
        _hoverCollapseTimer?.Stop();
        _hoverAnchorCenterX = GetPillScreenCenterX();

        CachePillBaseWidth();

        if (immediate)
        {
            HoverControlsClip.BeginAnimation(FrameworkElement.WidthProperty, null);
            _animatingHover = false;
            _hoverClipWidth = expanded ? HoverControlsExpandedWidth : 0;
            ApplyHoverVisualState(expanded, _hoverClipWidth);
            return;
        }

        _animatingHover = true;
        _hoverAnimTargetExpanded = expanded;
        _hoverAnimTargetWidth = expanded ? HoverControlsExpandedWidth : 0;

        if (expanded)
        {
            HoverControlsPanel.IsHitTestVisible = true;
        }

        var ease = expanded ? HoverWidthEase : HoverCollapseEase;
        var widthAnim = new DoubleAnimation
        {
            To = _hoverAnimTargetWidth,
            Duration = TimeSpan.FromMilliseconds(HoverWidthAnimMs),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop,
        };
        widthAnim.CurrentTimeInvalidated += OnHoverWidthAnimationTick;
        widthAnim.Completed += OnHoverWidthAnimationCompleted;

        HoverControlsClip.BeginAnimation(FrameworkElement.WidthProperty, widthAnim, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnHoverWidthAnimationTick(object? sender, EventArgs e)
    {
        if (IsBeingDragged)
        {
            return;
        }

        UpdatePillLayoutDuringHover();
    }

    private void OnHoverWidthAnimationCompleted(object? sender, EventArgs e)
    {
        if (sender is DoubleAnimation anim)
        {
            anim.Completed -= OnHoverWidthAnimationCompleted;
        }

        _animatingHover = false;
        _hoverClipWidth = _hoverAnimTargetWidth;
        ApplyHoverVisualState(_hoverAnimTargetExpanded, _hoverClipWidth);

        if (_awaitingHoverCollapseBeforeCloseComplete && !_hoverAnimTargetExpanded)
        {
            _awaitingHoverCollapseBeforeCloseComplete = false;
            CompleteDashboardCloseLayout();
        }
    }

    private void ApplyHoverVisualState(bool expanded, double clipWidth)
    {
        HoverControlsClip.BeginAnimation(FrameworkElement.WidthProperty, null);
        HoverControlsClip.Width = clipWidth;
        HoverControlsPanel.Opacity = 1;
        HoverControlsPanel.IsHitTestVisible = expanded && clipWidth >= HoverControlsExpandedWidth - 0.5;

        if (!_animatingSettings || !_settingsAnimOpen)
        {
            UpdatePillLayoutDuringHover();
        }
    }

    /// <summary>
    /// Sets hover clip width without triggering dashboard-close layout side effects.
    /// </summary>
    private void ApplyHoverStateSilently(bool expanded)
    {
        HoverControlsClip.BeginAnimation(FrameworkElement.WidthProperty, null);
        _animatingHover = false;
        _hoverExpanded = expanded;
        _hoverClipWidth = expanded ? HoverControlsExpandedWidth : 0;
        HoverControlsClip.Width = _hoverClipWidth;
        HoverControlsPanel.Opacity = 1;
        HoverControlsPanel.IsHitTestVisible = expanded;
    }

    /// <summary>
    /// Eases the pill margin back after the dashboard height reaches zero, then collapses
    /// the host and hover controls in one layout pass so the window width does not stutter.
    /// </summary>
    private void FinalizeDashboardClose()
    {
        _hoverAnchorCenterX = GetPillScreenCenterX();
        _settingsExpanded = false;
        SettingsHost.BeginAnimation(HeightProperty, null);
        SettingsHost.Height = 0;

        var hoverStillVisible = !App.Settings.Current.KeepWidgetExpanded
            && (_animatingHover || _hoverExpanded || _hoverClipWidth > 0.5);

        if (hoverStillVisible)
        {
            _awaitingHoverCollapseBeforeCloseComplete = true;
            if (!_animatingHover)
            {
                SetHoverControlsExpanded(false);
            }
        }

        if (App.Settings.Current.KeepWidgetExpanded && PillStack.HorizontalAlignment == HorizontalAlignment.Center)
        {
            SettingsHost.Visibility = Visibility.Collapsed;
            UpdateLayout();
            PreservePillScreenX(_hoverAnchorCenterX);
            FinishDashboardCloseAlignment();
            return;
        }

        if (hoverStillVisible)
        {
            SettingsHost.Visibility = Visibility.Collapsed;
            ResetPillDashboardAlignment();
            UpdateLayout();
            PreservePillScreenX(_hoverAnchorCenterX);
            return;
        }

        CompleteDashboardCloseLayout();
    }

    private void CompleteDashboardCloseLayout()
    {
        PillBorder.BeginAnimation(FrameworkElement.MarginProperty, null);

        if (!App.Settings.Current.KeepWidgetExpanded)
        {
            if (_animatingHover || _hoverClipWidth > 0.5)
            {
                if (!_animatingHover)
                {
                    SetHoverControlsExpanded(false);
                }

                SettingsHost.Visibility = Visibility.Collapsed;
                ResetPillDashboardAlignment();
                UpdateLayout();
                PreservePillScreenX(_hoverAnchorCenterX);
                return;
            }

            ApplyHoverStateSilently(false);
        }

        ResetPillDashboardAlignment();
        SettingsHost.Visibility = Visibility.Collapsed;

        UpdateLayout();
        PreservePillScreenX(_hoverAnchorCenterX);
        EndSettingsAnimation();
    }

    private void FinishDashboardCloseAlignment()
    {
        ResetPillDashboardAlignment();
        UpdateLayout();
        PreservePillScreenX(_hoverAnchorCenterX);
        CompleteDashboardCloseLayout();
    }

    private void SchedulePillPositionAfterLayout(double pillCenterX, Action? onComplete = null)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded)
            {
                onComplete?.Invoke();
                return;
            }

            if (ShouldAlignPillOverDashboard)
            {
                AlignPillOverDashboardPreservingScreenX(pillCenterX);
            }
            else
            {
                UpdateLayout();
                PreservePillScreenX(pillCenterX);
            }

            onComplete?.Invoke();
        }), DispatcherPriority.Loaded);
    }

    private void EndSettingsAnimation()
    {
        _animatingSettings = false;

        if (_settingsExpanded || ShouldKeepWidgetExpanded())
        {
            return;
        }

        if (!PillBorder.IsMouseOver)
        {
            return;
        }

        // Defer hover re-expand until the close layout finishes so the pill does not
        // widen again in the same frame the dashboard host collapses.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_settingsExpanded && !_animatingSettings && PillBorder.IsMouseOver)
            {
                SetHoverControlsExpanded(true);
            }
        }), DispatcherPriority.ApplicationIdle);
    }

    private bool _animatingSettings;
    private double _pillCenterXBeforeResize;

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
        _settingsAnimOpen = open;
        _settingsAnimPillCenterX = GetPillScreenCenterX();
        _animatingSettings = true;
        _awaitingHoverCollapseBeforeCloseComplete = false;

        if (open)
        {
            _settingsExpanded = true;
            SettingsHost.Visibility = Visibility.Visible;
            AlignPillOverDashboardPreservingScreenX(_settingsAnimPillCenterX);
            SchedulePillPositionAfterLayout(_settingsAnimPillCenterX);
        }
        else if (!App.Settings.Current.KeepWidgetExpanded)
        {
            _hoverAnchorCenterX = _settingsAnimPillCenterX;
            SetHoverControlsExpanded(false);
        }

        double from = SettingsHost.Height;
        double to = open ? _settingsTargetHeight : 0;

        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(open ? 220 : HoverWidthAnimMs),
            EasingFunction = open
                ? new CubicEase { EasingMode = EasingMode.EaseOut }
                : HoverCollapseEase,
        };
        anim.Completed += OnSettingsHeightAnimationCompleted;
        SettingsHost.BeginAnimation(HeightProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnSettingsHeightAnimationCompleted(object? sender, EventArgs e)
    {
        if (sender is DoubleAnimation anim)
        {
            anim.Completed -= OnSettingsHeightAnimationCompleted;
        }

        var to = _settingsAnimOpen ? _settingsTargetHeight : 0;

        SettingsHost.BeginAnimation(HeightProperty, null);
        SettingsHost.Height = to;
        if (!_settingsAnimOpen)
        {
            FinalizeDashboardClose();
        }
        else
        {
            SetHoverControlsExpanded(true, immediate: true, force: true);
            AlignPillOverDashboardPreservingScreenX(_settingsAnimPillCenterX);
            SchedulePillPositionAfterLayout(_settingsAnimPillCenterX, EndSettingsAnimation);
        }
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
        var pillCenterX = GetPillScreenCenterX();
        SettingsHost.BeginAnimation(HeightProperty, null);
        SettingsHost.Height = 0;
        SettingsHost.Visibility = Visibility.Collapsed;
        ResetPillDashboardAlignment();
        ApplyHoverStateSilently(false);
        UpdateLayout();
        PreservePillScreenX(pillCenterX);
        _hoverAnchorCenterX = pillCenterX;
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
        _pillCenterXBeforeResize = GetPillScreenCenterX();

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
        var widthChanged = Math.Abs(newW - currentW) > 0.5;

        SettingsPanelControl.Width = newW;
        _settingsTargetHeight = newH;
        SettingsHost.Height = newH;

        if (widthChanged)
        {
            SyncPillDuringDashboardResize();
        }
        else
        {
            UpdateLayout();
        }
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_resizingDashboard) return;
        _resizingDashboard = false;

        if (_settingsExpanded)
        {
            SyncPillDuringDashboardResize();
        }

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
            UpdatePillDashboardAlignment();
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
                UpdatePillDashboardAlignment();
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
        UpdatePillDashboardAlignment();
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
                BindNowPlayingTrack(App.Player.CurrentTrack);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "WidgetWindow.OnCurrentTrackChanged");
            }
        });
    }

    private void BindNowPlayingTrack(MusicWidget.Models.Track? track)
    {
        if (ReferenceEquals(_boundTrack, track))
        {
            UpdateNowPlayingDisplay();
            return;
        }

        if (_boundTrack is not null)
        {
            _boundTrack.PropertyChanged -= BoundTrack_PropertyChanged;
        }

        _boundTrack = track;

        if (_boundTrack is not null)
        {
            _boundTrack.PropertyChanged += BoundTrack_PropertyChanged;
            if (_boundTrack.ArtworkSource is null)
            {
                var capture = _boundTrack;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var art = await App.Artwork.GetArtworkAsync(capture).ConfigureAwait(false);
                        if (art is null) return;
                        UiDispatcher.BeginInvokeSafe(() =>
                        {
                            if (!ReferenceEquals(_boundTrack, capture)) return;
                            capture.ArtworkSource = art;
                        }, DispatcherPriority.Background);
                    }
                    catch
                    {
                        // Best-effort: missing artwork falls back to initials.
                    }
                });
            }
        }

        UpdateNowPlayingDisplay();
    }

    private void BoundTrack_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(UpdateNowPlayingDisplay);
    }

    private void UpdateNowPlayingDisplay()
    {
        if (!IsLoaded)
        {
            return;
        }

        try
        {
            var t = _boundTrack;
            if (t is null)
            {
            TrackTitle.Text = "Nothing playing";
            TrackArtist.Text = string.Empty;
            TrackTitle.ToolTip = null;
            TrackArtist.ToolTip = null;
                AlbumArtInitials.Text = "♪";
                AlbumArtInitials.Visibility = Visibility.Visible;
                AlbumArtImageHost.Visibility = Visibility.Collapsed;
                AlbumArtBrush.ImageSource = null;
                return;
            }

            TrackTitle.Text = !string.IsNullOrWhiteSpace(t.Title) ? t.Title : t.FriendlyDisplayName;
            TrackArtist.Text = !string.IsNullOrWhiteSpace(t.Artist) ? t.Artist : "Unknown artist";
            TrackTitle.ToolTip = TrackTitle.Text;
            TrackArtist.ToolTip = TrackArtist.Text;

            if (t.ArtworkSource is not null)
            {
                AlbumArtBrush.ImageSource = t.ArtworkSource;
                AlbumArtImageHost.Visibility = Visibility.Visible;
                AlbumArtInitials.Visibility = Visibility.Collapsed;
            }
            else
            {
                AlbumArtBrush.ImageSource = null;
                AlbumArtImageHost.Visibility = Visibility.Collapsed;
                AlbumArtInitials.Text = t.Initials;
                AlbumArtInitials.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "WidgetWindow.UpdateNowPlayingDisplay");
        }
    }

    private void OnLoopCurrentChanged(object? sender, EventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(UpdateLoopIcon);
    }

    private void UpdatePlayPauseIcon()
    {
        if (!IsLoaded)
        {
            return;
        }

        try
        {
            var key = App.Player.IsPlaying ? "Icon.Pause" : "Icon.Play";
            PlayPauseIcon.Data = (Geometry)Application.Current.Resources[key];
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "WidgetWindow.UpdatePlayPauseIcon");
        }
    }

    private void UpdateLoopIcon()
    {
        if (!IsLoaded)
        {
            return;
        }

        try
        {
            var on = App.Player.LoopCurrent;
            var dark = (Brush)Application.Current.Resources["Brush.WidgetIcon"];
            var white = (Brush)Application.Current.Resources["Brush.WhiteButton"];

            if (on)
            {
                LoopButton.Background = dark;
                LoopIconRect.Fill = Brushes.White;
                LoopButton.ToolTip = "Loop current song (on)";
            }
            else
            {
                LoopButton.Background = white;
                LoopIconRect.Fill = dark;
                LoopButton.ToolTip = "Loop current song (off)";
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "WidgetWindow.UpdateLoopIcon");
        }
    }
}
