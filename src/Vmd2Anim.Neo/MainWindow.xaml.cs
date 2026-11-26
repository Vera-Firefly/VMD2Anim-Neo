using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Vmd2Anim.Neo;

public partial class MainWindow : Window
{
    private readonly WorkspaceViewModel vm = new();
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? ikReading;

    public MainWindow()
    {
        vm.LoadSettings();
        InitializeComponent();
        DataContext = vm;
        PreviewMouseDown += (_, _) => FocusCue.SetKeyboardMode(this, false);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown)
                FocusCue.SetKeyboardMode(this, true);
        };
        SourceInitialized += (_, _) => WindowMaterial.Apply(this);
        if (SystemParameters.HighContrast)
        {
            Resources["Ink"] = Brushes.Black;
            Resources["Muted"] = Brushes.Black;
            Resources["Accent"] = new SolidColorBrush(Color.FromRgb(0, 55, 155));
            Resources["Field"] = Brushes.White;
            Resources["Line"] = Brushes.Black;
        }
        // File association / Explorer "Open with" inputs use the same validation as drag-and-drop.
        Loaded += async (_, _) => { AddPaths(Environment.GetCommandLineArgs().Skip(1)); await RefreshIkAsync(); };
    }

    private void AddVmd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = L.T("添加 VMD 动作"), Filter = L.T("VMD 动作 (*.vmd)|*.vmd"), Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        if (vm.IsRunning) return;
        var rejected = new List<string>();
        foreach (var raw in paths)
        {
            try
            {
                var path = Path.GetFullPath(raw);
                if (!File.Exists(path)) { rejected.Add(Path.GetFileName(raw)); continue; }
                var extension = Path.GetExtension(path);
                if (extension.Equals(".pmx", StringComparison.OrdinalIgnoreCase)) { vm.PmxPath = path; vm.Changed(nameof(vm.PmxPath)); }
                else if (extension.Equals(".vmd", StringComparison.OrdinalIgnoreCase))
                {
                    vm.AddMotion(path);
                }
                else rejected.Add(Path.GetFileName(path));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { rejected.Add(raw); }
        }
        if (rejected.Count > 0) ShowError(L.T("部分文件未添加：仅支持有效的 VMD 和 PMX 文件。"), string.Join(Environment.NewLine, rejected));
        else if (vm.Jobs.Count > 0) { vm.StatusText = "动作已加入，检查设置后即可转换"; vm.ProgressText = "queue-pending"; }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        string filter = name switch
        {
            "PmxPath" => L.T("PMX 模型 (*.pmx)|*.pmx"),
            "TargetPath" when vm.TargetIndex == 1 => L.T("FBX 模型 (*.fbx)|*.fbx"),
            "TargetPath" or "MorphMapPath" => L.T("JSON 文件 (*.json)|*.json"),
            "CorePath" => L.T("转换核心 (VmdConverterCore.exe)|VmdConverterCore.exe"),
            _ => L.T("VMD 动作 (*.vmd)|*.vmd")
        };
        var dialog = new OpenFileDialog { Title = L.T("选择文件"), Filter = filter, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        typeof(WorkspaceViewModel).GetProperty(name)!.SetValue(vm, dialog.FileName);
        vm.Changed(name);
    }

    private void OutputBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L.T("选择 Anim 输出目录"), Multiselect = false };
        if (Directory.Exists(vm.OutputDirectory)) dialog.InitialDirectory = vm.OutputDirectory;
        if (dialog.ShowDialog(this) == true) { vm.OutputDirectory = dialog.FolderName; vm.Changed(nameof(vm.OutputDirectory)); }
    }

    private void FacialBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (!vm.IsIdle || sender is not Button { DataContext: MotionJob job }) return;
        var dialog = new OpenFileDialog { Title = L.F("选择表情 VMD · {0}", job.Name), Filter = L.T("Facial / 表情 VMD (*.vmd)|*.vmd"), CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) job.ExpressionPath = dialog.FileName;
    }
    private void FacialClear_Click(object sender, RoutedEventArgs e)
    {
        if (vm.IsIdle && sender is Button { DataContext: MotionJob job }) job.ExpressionPath = "";
    }
    private void Facial_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!vm.IsIdle || sender is not FrameworkElement { DataContext: MotionJob job } ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        if (files.Length != 1 || !File.Exists(files[0]) || !Path.GetExtension(files[0]).Equals(".vmd", StringComparison.OrdinalIgnoreCase))
        { ShowError(L.T("请拖入一个有效的 Facial / 表情 VMD 文件。")); return; }
        job.ExpressionPath = Path.GetFullPath(files[0]);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = vm.IsIdle && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (vm.IsIdle && e.Data.GetData(DataFormats.FileDrop) is string[] files) AddPaths(files);
        e.Handled = true;
    }
    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelected();
    private void RemoveSelected()
    {
        if (!vm.IsIdle) return;
        foreach (var item in QueueList.SelectedItems.Cast<MotionJob>().ToArray()) vm.Jobs.Remove(item);
    }
    private void Queue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && e.OriginalSource is not TextBoxBase && e.OriginalSource is not ComboBox)
        { RemoveSelected(); e.Handled = true; }
    }
    private void Navigate(string page)
    {
        WorkspacePage.Visibility = page == "workspace" ? Visibility.Visible : Visibility.Collapsed;
        PreferencesPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
        vm.SetPage(page);
    }
    private void Workspace_Click(object sender, RoutedEventArgs e) { Navigate("workspace"); QueueList.Focus(); }
    private void Settings_Click(object sender, RoutedEventArgs e) => Navigate("settings");
    private void About_Click(object sender, RoutedEventArgs e) => Navigate("about");
    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try { vm.SaveSettings(); vm.SettingsFeedback = "设置已保存"; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { vm.SettingsFeedback = ex.Message; }
    }
    private void OpenDocument(string path)
    {
        try
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
            {
                var start = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
                start.ArgumentList.Add(path);
                Process.Start(start);
            }
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { ShowError(ex.Message); }
    }
    private void Link_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string url }) OpenDocument(url); }
    private void Readme_Click(object sender, RoutedEventArgs e) => OpenDocument(Path.Combine(AppContext.BaseDirectory, L.Instance.Language == "zh-CN" ? "README.zh-CN.md" : "README.md"));
    private void License_Click(object sender, RoutedEventArgs e) => OpenDocument(Path.Combine(AppContext.BaseDirectory, "LICENSE"));
    private async void ReadIk_Click(object sender, RoutedEventArgs e) => await RefreshIkAsync();
    private async void PmxPath_LostFocus(object sender, RoutedEventArgs e)
    {
        if (vm.IsIdle && !vm.LoadedIkPmx.Equals(vm.PmxPath.Trim(), StringComparison.OrdinalIgnoreCase)) await RefreshIkAsync();
    }
    private void PmxPath_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && vm.LoadedIkPmx.Length > 0 && !vm.LoadedIkPmx.Equals(vm.PmxPath.Trim(), StringComparison.OrdinalIgnoreCase)) vm.InvalidateIk();
    }
    private async Task RefreshIkAsync()
    {
        if (!vm.IsIdle) return;
        var pmx = vm.PmxPath.Trim();
        if (!File.Exists(pmx) || !File.Exists(vm.CorePath)) { vm.IkInfo = "请选择有效 PMX 和完整 Core，再读取源 IK。"; return; }
        ikReading?.Cancel();
        using var reading = new CancellationTokenSource(); ikReading = reading;
        vm.IkInfo = "正在读取 PMX IK…";
        try
        {
            var bones = await CoreRunner.ReadSourceIkAsync(vm.CorePath, pmx, reading.Token);
            if (vm.IsIdle && vm.PmxPath.Trim().Equals(pmx, StringComparison.OrdinalIgnoreCase)) vm.ApplyIkBones(pmx, bones);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { vm.IkInfo = "IK 读取失败，可检查路径后重试。"; vm.AppendLog(ex.Message); vm.ShowLog = true; }
        finally { if (ikReading == reading) ikReading = null; }
    }
    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(vm.OutputDirectory)) throw new IOException(L.T("输出目录尚不存在，请先完成转换或选择已有目录。"));
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add(Path.GetFullPath(vm.OutputDirectory)); Process.Start(start);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception) { ShowError(ex.Message); }
    }
    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = vm.Snapshot();
            if (vm.Jobs.Count == 0) throw new ArgumentException(L.T("请先添加至少一个 VMD 动作。"));
            var job = QueueList.SelectedItem as MotionJob ?? vm.Jobs[0];
            options = job.WithOptions(options);
            options.Validate([job.InputPath]);
            var example = Path.Combine(Path.GetFullPath(options.OutputDirectory), Path.GetFileNameWithoutExtension(job.InputPath) + ".anim");
            Clipboard.SetText(ConversionOptions.DisplayCommand(options.BuildStartInfo(job.InputPath, example)));
            vm.StatusText = "所选动作的 PowerShell 转换命令已复制";
            vm.ProgressText = "命令使用标准文件名；在终端运行前请检查同名输出。";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.Runtime.InteropServices.COMException) { ShowError(ex.Message); }
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (!vm.CanConvert) return;
        if (!vm.LoadedIkPmx.Equals(vm.PmxPath.Trim(), StringComparison.OrdinalIgnoreCase)) await RefreshIkAsync();
        if (!vm.CanConvert) return;
        if (!vm.LoadedIkPmx.Equals(vm.PmxPath.Trim(), StringComparison.OrdinalIgnoreCase))
        { ShowError(L.T("请先成功读取源 PMX 的 IK 列表，再开始转换。")); return; }
        ConversionOptions options;
        MotionJob[] batch = vm.Jobs.ToArray();
        Dictionary<MotionJob, ConversionOptions> jobOptions;
        try
        {
            options = vm.Snapshot();
            jobOptions = batch.ToDictionary(job => job, job => job.WithOptions(options));
            foreach (var job in batch)
            {
                try { jobOptions[job].Validate([job.InputPath]); }
                catch (ArgumentException ex) { throw new ArgumentException($"{job.Name}：{ex.Message}", ex); }
            }
            Directory.CreateDirectory(options.OutputDirectory);
            vm.SaveSettings();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { ShowError(ex.Message); return; }

        using var source = new CancellationTokenSource();
        cancellation = source;
        vm.IsRunning = true; vm.ProgressValue = 0;
        int completed = 0, failed = 0;
        foreach (var job in batch) { job.Status = "待转换"; job.OutputPath = ""; }
        vm.AppendLog(L.F("[{0:HH:mm:ss}] 开始批次 · {1} 个动作", DateTime.Now, batch.Length));
        try
        {
            foreach (var job in batch)
            {
                source.Token.ThrowIfCancellationRequested();
                string? reserved = null;
                string? staging = null;
                bool success = false;
                job.Status = "转换中";
                vm.StatusText = L.F("正在转换 · {0}", job.Name);
                vm.ProgressText = L.F("已完成 {0} / {1} · 失败 {2} · 当前文件处理中", completed, batch.Length, failed);
                try
                {
                    reserved = OutputPaths.Reserve(options.OutputDirectory, job.InputPath);
                    // The CLI only sees a unique staging path, so cancellation cannot replace an existing result.
                    var stagingDirectory = Path.Combine(options.OutputDirectory, ".neo-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(stagingDirectory);
                    staging = Path.Combine(stagingDirectory, Path.GetFileName(reserved));
                    var perJob = jobOptions[job];
                    vm.AppendLog(ConversionOptions.DisplayCommand(perJob.BuildStartInfo(job.InputPath, staging)));
                    var result = await CoreRunner.RunAsync(perJob, job.InputPath, staging, line => Dispatcher.BeginInvoke(() => vm.AppendLog(line)), source.Token);
                    source.Token.ThrowIfCancellationRequested();
                    File.Move(staging, reserved, true);
                    job.OutputPath = reserved;
                    success = true; completed++;
                    job.Status = "已完成";
                    job.Detail = L.F("{0:N0} 个采样 · {1:N0} KB · {2:0.0} 秒", result.Samples, result.Bytes / 1024d, result.ElapsedMs / 1000d);
                    vm.AppendLog(L.F("完成 · {0} · {1} · {2}", job.Name, job.Detail, reserved));
                    vm.CompleteMotion(job);
                }
                catch (OperationCanceledException) { job.Status = "已取消"; job.Detail = L.T("任务已停止，可重新开始。"); throw; }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    failed++; job.Status = "失败"; job.Detail = L.T("查看运行日志以定位问题");
                    vm.AppendLog(L.F("失败 · {0}\n{1}", job.Name, ex.Message)); vm.ShowLog = true;
                }
                finally
                {
                    // Delete only this run's reserved file and exact, unique staging paths.
                    if (!success && reserved != null) TryDelete(reserved);
                    if (staging != null)
                    {
                        TryDelete(staging); TryDelete(staging + ".partial");
                        try { Directory.Delete(Path.GetDirectoryName(staging)!); }
                        catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                }
                vm.ProgressValue = (completed + failed) * 100d / batch.Length;
            }
            vm.StatusText = failed == 0 ? L.T("转换完成，动作已就绪") : L.F("批次结束 · {0} 个动作需要处理", failed);
            vm.ProgressText = L.F("成功 {0} / {1} · 失败 {2} · 可打开输出目录查看 .anim", completed, batch.Length, failed);
        }
        catch (OperationCanceledException)
        {
            vm.StatusText = "转换已取消";
            vm.ProgressText = L.F("保留 {0} 个已完成结果 · 剩余动作可重新开始", completed);
            vm.AppendLog(L.T("用户取消批次。已清理当前任务的临时文件。"));
        }
        finally { cancellation = null; vm.IsRunning = false; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel(); vm.StatusText = "正在停止转换…";
    }
    private void ShowError(string message, string? details = null)
    {
        vm.StatusText = message.Split('\n')[0];
        vm.ProgressText = "请检查输入或设置后重试。";
        vm.AppendLog(message + (details == null ? "" : Environment.NewLine + details));
        vm.ShowLog = true;
    }
    private void Log_TextChanged(object sender, TextChangedEventArgs e) { if (sender is TextBox box) box.ScrollToEnd(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        ikReading?.Cancel();
        if (vm.IsRunning)
        {
            // Keep the window alive until the cancelled process and temporary files have been drained.
            e.Cancel = true; cancellation?.Cancel();
            vm.StatusText = "正在停止转换，结束后可关闭窗口";
            return;
        }
        try { vm.SaveSettings(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { /* Invalid draft settings do not prevent closing. */ }
    }
    private void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { vm.AppendLog(L.T("临时文件清理失败：") + path + " · " + ex.Message); }
    }
}
