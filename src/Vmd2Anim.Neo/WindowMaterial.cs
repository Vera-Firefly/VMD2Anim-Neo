using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Vmd2Anim.Neo;

internal static class WindowMaterial
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    public static void Apply(Window window)
    {
        if (SystemParameters.HighContrast || !SystemParameters.IsGlassEnabled || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (HwndSource.FromHwnd(handle) is not { CompositionTarget: not null } source) return;
        int backdrop = 3; // DWMSBT_TRANSIENTWINDOW: desktop acrylic, Windows 11 22H2+.
        if (DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int)) != 0) return;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (DwmExtendFrameIntoClientArea(handle, ref margins) != 0) return;
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        window.Background = Brushes.Transparent;
    }
}
