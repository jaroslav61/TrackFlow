using System.Threading.Tasks;
using Avalonia.Input;

namespace TrackFlow.Helpers;

/// <summary>
/// Compatibility wrapper for Avalonia drag&amp;drop APIs that are currently marked as obsolete
/// (e.g. <see cref="DragEventArgs.Data"/>, <see cref="DataObject"/>, <see cref="DragDrop.DoDragDrop"/>).
/// 
/// Goal: keep obsolete usage isolated in one place, so UI code stays warning-free and
/// future migration to the newer DataTransfer model is localized.
/// </summary>
#pragma warning disable CS0618
public static class DragDropCompat
{
    public static bool Contains(DragEventArgs e, string format)
        => e.Data.Contains(format);

    public static object? Get(DragEventArgs e, string format)
        => e.Data.Get(format);

    public static bool TryGet<T>(DragEventArgs e, string format, out T value)
    {
        var obj = Get(e, format);
        if (obj is T t)
        {
            value = t;
            return true;
        }

        value = default!;
        return false;
    }

    public static IDataObject CreateDataObject(string format, object data)
    {
        var dobj = new DataObject();
        dobj.Set(format, data);
        return dobj;
    }

    public static Task<DragDropEffects> DoDragDropAsync(PointerEventArgs e, string format, object data, DragDropEffects effects)
        => DragDrop.DoDragDrop(e, CreateDataObject(format, data), effects);
}
#pragma warning restore CS0618


