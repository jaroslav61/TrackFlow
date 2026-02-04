using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.Windows.Input;

namespace TrackFlow.Behaviors
{
    public class DragDropBehavior
    {
        static DragDropBehavior()
        {
            IsDragSourceProperty.Changed.Subscribe(OnIsDragSourceChanged);
            IsDropTargetProperty.Changed.Subscribe(OnIsDropTargetChanged);
        }

        public static readonly AttachedProperty<bool> IsDragSourceProperty =
            AvaloniaProperty.RegisterAttached<DragDropBehavior, Control, bool>("IsDragSource");

        public static readonly AttachedProperty<bool> IsDropTargetProperty =
            AvaloniaProperty.RegisterAttached<DragDropBehavior, Control, bool>("IsDropTarget");

        public static readonly AttachedProperty<string?> DragFormatProperty =
            AvaloniaProperty.RegisterAttached<DragDropBehavior, Control, string?>("DragFormat");

        public static readonly AttachedProperty<ICommand?> DropCommandProperty =
            AvaloniaProperty.RegisterAttached<DragDropBehavior, Control, ICommand?>("DropCommand");

        public static bool GetIsDragSource(Control control) => control.GetValue(IsDragSourceProperty);
        public static void SetIsDragSource(Control control, bool value) => control.SetValue(IsDragSourceProperty, value);

        public static bool GetIsDropTarget(Control control) => control.GetValue(IsDropTargetProperty);
        public static void SetIsDropTarget(Control control, bool value) => control.SetValue(IsDropTargetProperty, value);

        public static string? GetDragFormat(Control control) => control.GetValue(DragFormatProperty);
        public static void SetDragFormat(Control control, string? value) => control.SetValue(DragFormatProperty, value);

        public static ICommand? GetDropCommand(Control control) => control.GetValue(DropCommandProperty);
        public static void SetDropCommand(Control control, ICommand? value) => control.SetValue(DropCommandProperty, value);

        private static void OnIsDragSourceChanged(AvaloniaPropertyChangedEventArgs<bool> e)
        {
            if (e.Sender is not Control c)
                return;

            if (e.NewValue.Value)
                AttachDragSource(c);
            else
                DetachDragSource(c);
        }

        private static void OnIsDropTargetChanged(AvaloniaPropertyChangedEventArgs<bool> e)
        {
            if (e.Sender is not Control c)
                return;

            if (e.NewValue.Value)
                AttachDropTarget(c);
            else
                DetachDropTarget(c);
        }

        private static void AttachDragSource(Control c)
        {
            c.PointerPressed -= Control_PointerPressed;
            c.PointerPressed += Control_PointerPressed;
            EnsureUnloadedHook(c);
        }

        private static void DetachDragSource(Control c)
        {
            c.PointerPressed -= Control_PointerPressed;
            ClearUnloadedHookIfUnused(c);
        }

        private static void AttachDropTarget(Control c)
        {
            c.RemoveHandler(DragDrop.DropEvent, DropHandler);
            c.AddHandler(DragDrop.DropEvent, DropHandler, RoutingStrategies.Bubble);
            EnsureUnloadedHook(c);
        }

        private static void DetachDropTarget(Control c)
        {
            c.RemoveHandler(DragDrop.DropEvent, DropHandler);
            ClearUnloadedHookIfUnused(c);
        }

        private static void EnsureUnloadedHook(Control c)
        {
            c.Unloaded -= Control_Unloaded;
            c.Unloaded += Control_Unloaded;
        }

        private static void ClearUnloadedHookIfUnused(Control c)
        {
            if (!GetIsDragSource(c) && !GetIsDropTarget(c))
                c.Unloaded -= Control_Unloaded;
        }

        private static void Control_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control c)
                return;

            // safety: avoid leaks if control removed while properties still true
            if (GetIsDragSource(c))
                DetachDragSource(c);

            if (GetIsDropTarget(c))
                DetachDropTarget(c);

            // If both flags are false after detach, remove the Unloaded hook too.
            ClearUnloadedHookIfUnused(c);
        }

        private static async void Control_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control c)
                return;

            // Guard: start drag only on left button press
            if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed)
                return;

            var format = GetDragFormat(c) ?? "trackflow/wagon";
            var data = c.DataContext;
            if (data == null)
                return;

            try
            {
                var dobj = new DataObject();
                dobj.Set(format, data);
                await DragDrop.DoDragDrop(e, dobj, DragDropEffects.Move);
                e.Handled = true;
            }
            catch
            {
                // ignore
            }
        }

        private static void DropHandler(object? sender, DragEventArgs e)
        {
            if (sender is not Control c)
                return;

            var format = GetDragFormat(c) ?? "trackflow/wagon";
            if (!e.Data.Contains(format))
                return;

            var dropped = e.Data.Get(format);
            if (dropped == null)
                return;

            // find target element under the event source
            var source = e.Source as Control;
            Control? targetControl = source;
            object? targetData = null;

            while (targetControl != null)
            {
                if (targetControl.DataContext != null && !object.ReferenceEquals(targetControl.DataContext, c.DataContext))
                {
                    targetData = targetControl.DataContext;
                    break;
                }
                targetControl = targetControl.GetVisualParent() as Control;
            }

            if (targetData == null)
            {
                return;
            }

            var cmd = GetDropCommand(c);
            if (cmd == null)
                return;

            var param = new object[] { targetData, dropped };
            if (cmd.CanExecute(param))
                cmd.Execute(param);

            e.Handled = true;
        }
    }
}
