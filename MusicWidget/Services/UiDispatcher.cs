using System;
using System.Windows;
using System.Windows.Threading;

namespace MusicWidget.Services;

/// <summary>
/// Safe UI-thread dispatch helpers that no-op during shutdown instead of throwing.
/// </summary>
public static class UiDispatcher
{
    public static void BeginInvokeSafe(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.BeginInvoke(action, priority);
            }
        }
        catch
        {
            // App is closing or the dispatcher rejected the callback.
        }
    }

    public static void InvokeSafe(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }
        catch
        {
            // Shutdown race or a handled UI fault — avoid taking down the process.
        }
    }
}
