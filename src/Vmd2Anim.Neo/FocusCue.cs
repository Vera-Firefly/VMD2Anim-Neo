using System.Windows;

namespace Vmd2Anim.Neo;

// Pointer focus must not look like selection. Keyboard navigation keeps a visible cue.
public static class FocusCue
{
    public static readonly DependencyProperty KeyboardModeProperty = DependencyProperty.RegisterAttached(
        "KeyboardMode", typeof(bool), typeof(FocusCue), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetKeyboardMode(DependencyObject value) => (bool)value.GetValue(KeyboardModeProperty);
    public static void SetKeyboardMode(DependencyObject value, bool enabled) => value.SetValue(KeyboardModeProperty, enabled);
}
