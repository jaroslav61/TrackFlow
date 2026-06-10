using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace TrackFlow.Services;

public static class TooltipPreferenceService
{
    private const string DisabledClass = "tooltips-disabled";
    private static volatile bool _tooltipsEnabled = true;

    public static void SetEnabled(bool enabled)
        => _tooltipsEnabled = enabled;

    public static void Attach(Window window)
    {
        if (window == null)
            throw new ArgumentNullException(nameof(window));

        EventHandler? openedHandler = null;
        EventHandler? closedHandler = null;
        EventHandler<Avalonia.VisualTreeAttachmentEventArgs>? attachedHandler = null;

        void ApplyCurrentState() => ApplyToWindow(window, _tooltipsEnabled);

        openedHandler = (_, _) => ApplyCurrentState();
        closedHandler = (_, _) =>
        {
            window.Opened -= openedHandler;
            window.Closed -= closedHandler;
            window.AttachedToVisualTree -= attachedHandler;
        };
        attachedHandler = (_, _) => ApplyCurrentState();

        window.Opened += openedHandler;
        window.Closed += closedHandler;
        window.AttachedToVisualTree += attachedHandler;

        ApplyCurrentState();
    }

    public static void ApplyToWindow(Window window, bool tooltipsEnabled)
    {
        if (window == null)
            throw new ArgumentNullException(nameof(window));

        if (tooltipsEnabled)
            window.Classes.Remove(DisabledClass);
        else if (!window.Classes.Contains(DisabledClass))
            window.Classes.Add(DisabledClass);

        window.SetValue(ToolTip.ServiceEnabledProperty, tooltipsEnabled);

        foreach (var ctrl in window.GetVisualDescendants().OfType<Control>())
            ctrl.SetValue(ToolTip.ServiceEnabledProperty, tooltipsEnabled);
    }
}


