using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using Vmd2Anim.Neo;

internal static class UiRegressionChecks
{
    public static void Run(Action<bool, string> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunOnDispatcher(check); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw new Exception("WPF control regression", failure);
    }

    private static void RunOnDispatcher(Action<bool, string> check)
    {
        var original = L.Instance.Preference;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = new ResourceDictionary { Source = new Uri("pack://application:,,,/VMD2Anim-Neo;component/Theme.xaml") };
        application.Resources.MergedDictionaries.Add(resources);
        var vm = new WorkspaceViewModel();
        var root = new StackPanel { Resources = resources, DataContext = vm, Width = 500 };
        ComboBox Add(string items, string selection, string property, string? display = "Label")
        {
            var combo = new ComboBox();
            if (display != null) combo.ItemTemplate = (DataTemplate)resources["ChoiceLabel"];
            combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(items));
            combo.SetBinding(property == "index" ? ComboBox.SelectedIndexProperty : ComboBox.SelectedItemProperty, new Binding(selection));
            root.Children.Add(combo);
            return combo;
        }
        var language = Add(nameof(vm.Languages), nameof(vm.LanguageIndex), "index");
        var target = Add(nameof(vm.TargetKinds), nameof(vm.TargetIndex), "index");
        var mode = Add(nameof(vm.OutputModes), nameof(vm.SelectedMode), "item");
        var coordinates = Add(nameof(vm.Coordinates), nameof(vm.CoordinateIndex), "index");
        var sample = Add(nameof(vm.SampleRates), nameof(vm.SampleRate), "item", null);
        var job = new MotionJob(Path.Combine(Path.GetTempPath(), "neo-ui-motion.vmd"));
        job.SetIkBones([new(1, "右足ＩＫ", "leg IK R")], false);
        job.SetOutputModes(vm.OutputModes);
        vm.Jobs.Add(job);
        var motion = Add(nameof(job.AvailableModes), nameof(job.SelectedOutputMode), "item"); motion.DataContext = job;
        var allIk = Add(nameof(job.IkModes), nameof(job.IkDefaultIndex), "index"); allIk.DataContext = job;
        var ik = Add("", nameof(SourceIkSwitch.Mode), "item");
        ik.DataContext = job.IkSwitches[0]; ik.ItemsSource = IkMode.All;
        var heading = new TextBlock(); heading.SetBinding(TextBlock.TextProperty, new Binding("[设置]") { Source = L.Instance }); root.Children.Add(heading);
        var nav = new Button { Style = (Style)resources["NavigationButton"] };
        nav.SetBinding(System.Windows.Controls.Primitives.Selector.IsSelectedProperty, new Binding(nameof(vm.IsSettingsPage)) { Mode = BindingMode.OneWay });
        root.Children.Add(nav);
        void Flush()
        {
            root.Measure(new Size(500, 1000)); root.Arrange(new Rect(0, 0, 500, 1000)); root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
        string Display(ComboBox combo)
        {
            var selection = (ContentPresenter)combo.Template.FindName("Selection", combo);
            return string.Join("", Descendants(selection).OfType<TextBlock>().Select(t => t.Text));
        }
        try
        {
            L.Instance.SetLanguage("zh-CN"); Flush();
            target.SetCurrentValue(ComboBox.SelectedIndexProperty, 1); Flush();
            check(vm.TargetIndex == 1 && Display(target) == vm.TargetKinds[1].Label, "WPF target selection updates model and displayed label");
            mode.SetCurrentValue(ComboBox.SelectedItemProperty, vm.OutputModes[5]); Flush();
            check(vm.SelectedMode.Value == "target-fk" && Display(mode) == vm.SelectedMode.Label, "WPF output selection updates model and displayed label");
            language.SetCurrentValue(ComboBox.SelectedIndexProperty, 1); Flush();
            check(L.Instance.Preference == "en" && language.SelectedIndex == 1, "WPF language selection applies English and preserves selected option");
            check(heading.Text == "Settings", "WPF localized page text refreshes with language selection");
            check(Display(target) == vm.TargetKinds[1].Label && Display(mode) == vm.SelectedMode.Label && Display(coordinates) == vm.Coordinates[vm.CoordinateIndex].Label, "WPF selected dropdown labels refresh after language change");
            language.SetCurrentValue(ComboBox.SelectedIndexProperty, 2); Flush();
            check(L.Instance.Preference == "zh-CN" && Display(mode) == vm.SelectedMode.Label, "WPF language selection switches back to Chinese");
            sample.SetCurrentValue(ComboBox.SelectedItemProperty, 60); Flush();
            check(vm.SampleRate == 60 && Display(sample) == "60", "WPF numeric dropdown retains selection and label");
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            sample.RaiseEvent(wheel); Flush();
            check(wheel.Handled && vm.SampleRate == 60 && sample.SelectedItem.Equals(60), "Closed dropdown consumes wheel without changing conversion settings");

            motion.SetCurrentValue(ComboBox.SelectedItemProperty, job.AvailableModes.Single(m => m.Value == "target-fk-all"));
            allIk.SetCurrentValue(ComboBox.SelectedIndexProperty, 1); Flush();
            ik.SetCurrentValue(ComboBox.SelectedItemProperty, IkMode.All[2]); Flush();
            for (int i = 0; i < 3; i++)
            {
                language.SetCurrentValue(ComboBox.SelectedIndexProperty, 1); Flush();
                language.SetCurrentValue(ComboBox.SelectedIndexProperty, 2); Flush();
            }
            check(job.SelectedOutputMode.Value == "target-fk-all" && Display(motion) == job.SelectedOutputMode.Label && job.IkSwitches[0].Mode.Value == "off" && Display(ik) == IkMode.All[2].Label, "WPF motion and IK selections survive repeated language changes");
            vm.SetPage("settings"); Flush();
            check(System.Windows.Controls.Primitives.Selector.GetIsSelected(nav) && ((SolidColorBrush)nav.Background).Color == (Color)ColorConverter.ConvertFromString("#D9E7FF"), "Sidebar selected appearance follows current page");
            vm.SetPage("workspace"); Flush();
            check(!System.Windows.Controls.Primitives.Selector.GetIsSelected(nav), "Sidebar selection clears when leaving the page");
            var page = new ScrollViewer { Content = root, Height = 180, Width = 500 };
            page.Measure(new Size(500, 180)); page.Arrange(new Rect(0, 0, 500, 180)); page.UpdateLayout();
            sample.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
            page.UpdateLayout();
            check(page.VerticalOffset > 0 && vm.SampleRate == 60, "Wheel over closed dropdown scrolls its page and preserves selection");
            page.Content = null;
        }
        finally { L.Instance.SetLanguage(original); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
