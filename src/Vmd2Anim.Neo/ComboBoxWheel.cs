using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Vmd2Anim.Neo;

// A closed selector must not silently change a conversion setting while scrolling a page.
public static class ComboBoxWheel
{
    public static readonly DependencyProperty ScrollPageWhenClosedProperty = DependencyProperty.RegisterAttached(
        "ScrollPageWhenClosed", typeof(bool), typeof(ComboBoxWheel), new PropertyMetadata(false, OnChanged));
    public static bool GetScrollPageWhenClosed(DependencyObject value) => (bool)value.GetValue(ScrollPageWhenClosedProperty);
    public static void SetScrollPageWhenClosed(DependencyObject value, bool enabled) => value.SetValue(ScrollPageWhenClosedProperty, enabled);

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not ComboBox combo) return;
        if ((bool)args.NewValue) combo.PreviewMouseWheel += OnWheel;
        else combo.PreviewMouseWheel -= OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs args)
    {
        if (sender is not ComboBox { IsDropDownOpen: false } combo) return;
        args.Handled = true;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(combo); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollViewer scroll) continue;
            scroll.RaiseEvent(new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = scroll
            });
            break;
        }
    }
}
