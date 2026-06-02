using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace TrackFlow.Views;

public static class ControlExtensions
{
    /// <summary>
    /// Nájde predka daného typu v vizuálnom strome.
    /// </summary>
    public static T? FindAncestorOfType<T>(this StyledElement element) where T : class
    {
        var parent = element.Parent;
        while (parent != null)
        {
            if (parent is T result)
                return result;
            
            parent = (parent as StyledElement)?.Parent;
        }
        
        return null;
    }
}

