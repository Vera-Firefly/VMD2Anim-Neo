using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Media;

namespace Vmd2Anim.Neo;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class MotionJob(string input) : Observable
{
    private static readonly OutputMode FollowDefault = new("", "跟随全局设置");
    public ObservableCollection<OutputMode> AvailableModes { get; } = [FollowDefault];
    private OutputMode selectedOutputMode = FollowDefault;
    public OutputMode SelectedOutputMode
    {
        get => selectedOutputMode;
        set { if (value == null) return; selectedOutputMode = value; Notify(); Notify(nameof(OutputModeSummary)); }
    }
    public string OutputModeSummary => L.T("输出: ") + SelectedOutputMode.Label;
    public void SetOutputModes(IEnumerable<OutputMode> modes)
    {
        var value = SelectedOutputMode.Value;
        AvailableModes.Clear(); AvailableModes.Add(FollowDefault);
        foreach (var mode in modes) AvailableModes.Add(mode);
        SelectedOutputMode = AvailableModes.FirstOrDefault(m => m.Value == value) ?? FollowDefault;
    }
    public void RefreshLanguage()
    {
        foreach (var mode in AvailableModes) mode.RefreshLanguage();
        Notify(nameof(OutputModeSummary));
        Notify(nameof(ExpressionSummary)); Notify(nameof(IkSummary)); Notify(nameof(Status)); Notify(nameof(Detail));
    }
    private string expressionPath = "";
    public string ExpressionPath
    {
        get => expressionPath;
        set { expressionPath = value; Notify(); Notify(nameof(HasExpression)); Notify(nameof(ExpressionSummary)); }
    }
    public bool HasExpression => !string.IsNullOrWhiteSpace(ExpressionPath);
    public string ExpressionSummary => HasExpression ? L.T("表情: ") + Path.GetFileName(ExpressionPath.Trim()) : L.T("表情: 默认");
    public ObservableCollection<SourceIkSwitch> IkSwitches { get; } = [];
    private int ikDefaultIndex;
    public IReadOnlyList<IkMode> IkModes => IkMode.All;
    public int IkDefaultIndex
    {
        get => ikDefaultIndex;
        set { ikDefaultIndex = Math.Clamp(value, 0, 2); foreach (var item in IkSwitches) item.Mode = IkMode.All[ikDefaultIndex]; Notify(); Notify(nameof(IkSummary)); }
    }
    public string IkSummary => IkSwitches.Count == 0 ? L.T("源 IK: 等待读取 PMX") : L.F("源 IK: {0} 项覆盖", IkSwitches.Count(s => s.Mode.Value != "vmd"));
    public void SetIkBones(IEnumerable<SourceIkBone> bones, bool preserve)
    {
        var previous = preserve ? IkSwitches.ToDictionary(s => s.Bone.Key, s => s.Mode, StringComparer.Ordinal) : [];
        IkSwitches.Clear();
        foreach (var bone in bones)
        {
            var item = new SourceIkSwitch(bone) { Mode = previous.GetValueOrDefault(bone.Key) ?? IkMode.All[preserve ? ikDefaultIndex : 0] };
            item.PropertyChanged += (_, _) => Notify(nameof(IkSummary));
            IkSwitches.Add(item);
        }
        if (!preserve) { ikDefaultIndex = 0; Notify(nameof(IkDefaultIndex)); }
        Notify(nameof(IkSummary));
    }
    public ConversionOptions WithOptions(ConversionOptions options) => options with
    {
        OutputKind = SelectedOutputMode.Value == "" ? options.OutputKind : SelectedOutputMode.Value,
        ExpressionPath = HasExpression ? ExpressionPath.Trim() : options.ExpressionPath,
        SourceIk = IkSwitches.Where(s => s.Mode.Value != "vmd").ToDictionary(s => s.Bone.Key, s => s.Mode.Value, StringComparer.Ordinal)
    };
    public string InputPath { get; } = Path.GetFullPath(input);
    public string Name => Path.GetFileName(InputPath);
    public string OutputPath { get; set; } = "";
    private string status = "待转换";
    private string? detail;
    public string Status { get => L.T(status); set { status = value; Notify(); Notify(nameof(StatusBrush)); } }
    public string Detail { get => detail == null ? L.F("{0:N0} KB · VMD 动作", new FileInfo(InputPath).Length / 1024d) : L.T(detail); set { detail = value; Notify(); } }
    public Brush StatusBrush => new SolidColorBrush(status switch { "已完成" => Color.FromRgb(33, 117, 86), "失败" => Color.FromRgb(174, 54, 61), "转换中" => Color.FromRgb(40, 94, 211), _ => Color.FromRgb(83, 101, 124) });
}

public sealed class WorkspaceViewModel : Observable
{
    public ObservableCollection<MotionJob> Jobs { get; } = [];
    public OutputMode[] TargetKinds { get; } = [new("0", "使用源 PMX 骨架"), new("1", "目标 FBX 模型"), new("2", "HPS 标定 Rig（JSON）"), new("3", "骨架 Descriptor（JSON）")];
    public int[] SampleRates { get; } = [24, 30, 60, 120];
    public OutputMode[] Coordinates { get; } = [new("0", "翻转 Z"), new("1", "翻转 X"), new("2", "绕 Y 轴旋转 180° · 默认"), new("3", "不转换坐标")];
    public ObservableCollection<OutputMode> OutputModes { get; } =
    [
        new("humanoid-ik", "Humanoid · 源 IK"),
        new("target-humanoid", "Humanoid · 正常模式"),
        new("target-humanoid-leg", "Humanoid · 下肢拟合"),
        new("target-humanoid-arm", "Humanoid · 上肢拟合"),
        new("target-humanoid-all", "Humanoid · 全身拟合"),
        new("target-fk", "FK · 正常模式"),
        new("target-fk-leg", "FK · 下肢拟合"),
        new("target-fk-arm", "FK · 上肢拟合"),
        new("target-fk-all", "FK · 全身拟合")
    ];
    private string corePath = FindCore();
    public string CorePath { get => corePath; set { corePath = value; Notify(); Notify(nameof(CoreStatus)); Notify(nameof(CoreStatusBrush)); } }
    public string PmxPath { get; set; } = "";
    public SourceIkBone[] SourceIkBones { get; private set; } = [];
    public string LoadedIkPmx { get; private set; } = "";
    private string ikInfo = "";
    public string IkInfo { get => ikInfo == "ik-loaded" ? (SourceIkBones.Length == 0 ? L.T("此 PMX 没有 IK 骨骼。") : "") : L.T(ikInfo); set { ikInfo = value; Notify(); } }
    public void ApplyIkBones(string pmx, SourceIkBone[] bones)
    {
        bool preserve = LoadedIkPmx.Equals(pmx, StringComparison.OrdinalIgnoreCase);
        SourceIkBones = bones; LoadedIkPmx = pmx;
        foreach (var job in Jobs) job.SetIkBones(bones, preserve);
        IkInfo = "ik-loaded";
    }
    public void InvalidateIk()
    {
        SourceIkBones = []; LoadedIkPmx = "";
        foreach (var job in Jobs) job.SetIkBones([], false);
        IkInfo = "源模型已更改，请重新读取 IK。";
    }
    public void AddMotion(string path)
    {
        if (Jobs.Any(j => j.InputPath.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        var job = new MotionJob(path); job.SetIkBones(SourceIkBones, false); job.SetOutputModes(CompatibleModes); Jobs.Add(job);
    }
    public void CompleteMotion(MotionJob job) => Jobs.Remove(job);
    public string TargetPath { get; set; } = "";
    public string OutputDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VMD2Anim Neo", "Animations");
    private int targetIndex;
    public int TargetIndex
    {
        get => targetIndex;
        set
        {
            if (value is < 0 or > 3 || targetIndex == value) return;
            targetIndex = value; TargetPath = ""; Notify(); Notify(nameof(TargetPath)); Notify(nameof(HasTarget));
            SelectedMode = OutputModes[value == 0 ? 0 : 1];
            foreach (var job in Jobs) job.SetOutputModes(CompatibleModes);
        }
    }
    private IEnumerable<OutputMode> CompatibleModes => OutputModes.Where(m => HasTarget || m.Value == "humanoid-ik");
    public bool HasTarget => TargetIndex != 0;
    private OutputMode? selectedMode;
    public OutputMode SelectedMode { get => selectedMode ?? OutputModes[0]; set { if (value == null) return; selectedMode = value; Notify(); } }
    public int SampleRate { get; set; } = 30;
    public string PositionScale { get; set; } = "0.08";
    public int CoordinateIndex { get; set; } = 2;
    public string Passes { get; set; } = "3";
    public string Threads { get; set; } = Math.Clamp(Environment.ProcessorCount - 1, 1, 256).ToString(CultureInfo.InvariantCulture);
    public string MemoryMb { get; set; } = "256";
    public string Duration { get; set; } = "";
    public string UpperTangentError { get; set; } = "0";
    public bool BakeMorphs { get; set; } = true;
    public bool FkContinuity { get; set; } = true;
    public string ExpressionPath { get; set; } = "";
    public string MorphMapPath { get; set; } = "";
    private bool isRunning;
    public bool IsRunning { get => isRunning; set { isRunning = value; Notify(); Notify(nameof(IsIdle)); Notify(nameof(CanConvert)); Notify(nameof(ConvertButtonText)); } }
    public bool IsIdle => !IsRunning;
    public bool CanConvert => IsIdle && Jobs.Count > 0;
    public bool IsQueueEmpty => Jobs.Count == 0;
    public string QueueCountText => L.F("{0} 个文件", Jobs.Count);
    public string PendingText => L.F("待转换 {0} 个动作", Jobs.Count);
    public string ConvertButtonText => IsRunning ? L.T("正在转换…") : L.T("开始转换");
    public string CoreStatus => HasCoreRuntime ? L.T("Core 已就绪") : L.T("待配置 Core");
    public bool HasCoreRuntime => File.Exists(CorePath) && new[] { "VmdConverterCore.Shared.dll", "VmdConverterCore.Hps.dll", "UnityHps.Core.dll", "UnityHps.Fbx.dll" }.All(n => File.Exists(Path.Combine(Path.GetDirectoryName(CorePath) ?? "", n)));
    public Brush CoreStatusBrush => new SolidColorBrush(HasCoreRuntime ? Color.FromRgb(33, 117, 86) : Color.FromRgb(155, 102, 33));
    private string statusText = "就绪";
    public string StatusText { get => L.T(statusText); set { statusText = value; Notify(); } }
    private string progressText = "";
    public string ProgressText { get => progressText == "queue-pending" ? PendingText : L.T(progressText); set { progressText = value; Notify(); } }
    private double progressValue;
    public double ProgressValue { get => progressValue; set { progressValue = value; Notify(); } }
    private string logText = "";
    public string LogText { get => logText; set { logText = value; Notify(); } }
    private bool showLog;
    public bool ShowLog { get => showLog; set { showLog = value; Notify(); } }
    public OutputMode[] Languages { get; } = [new("system", "跟随系统"), new("en", "English"), new("zh-CN", "简体中文")];
    public int LanguageIndex
    {
        get => L.Instance.Preference switch { "en" => 1, "zh-CN" => 2, _ => 0 };
        set { if (value is < 0 or > 2 || value == LanguageIndex) return; L.Instance.SetLanguage(value switch { 1 => "en", 2 => "zh-CN", _ => "system" }); }
    }
    private string page = "workspace";
    public bool IsWorkspacePage => page == "workspace";
    public bool IsSettingsPage => page == "settings";
    public bool IsAboutPage => page == "about";
    public string PageTitle => page switch { "settings" => L.T("设置"), "about" => L.T("关于"), _ => "VMD2Anim Neo" };
    public void SetPage(string value)
    {
        page = value; Notify(nameof(PageTitle));
        Notify(nameof(IsWorkspacePage)); Notify(nameof(IsSettingsPage)); Notify(nameof(IsAboutPage));
    }
    public string AppVersion => "Version " + (typeof(WorkspaceViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.1");
    public string CoreBuild
    {
        get
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "build-manifest.json")));
                return "Core · " + doc.RootElement.GetProperty("coreRevision").GetString();
            }
            catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException) { return "VMD-Converter-Core"; }
        }
    }
    private string settingsFeedback = "";
    public string SettingsFeedback { get => L.T(settingsFeedback); set { settingsFeedback = value; Notify(); } }
    public WorkspaceViewModel()
    {
        L.Instance.PropertyChanged += (_, _) =>
        {
            foreach (var mode in OutputModes.Concat(TargetKinds).Concat(Coordinates).Concat(Languages)) mode.RefreshLanguage();
            foreach (var mode in IkMode.All) mode.RefreshLanguage();
            foreach (var job in Jobs) job.RefreshLanguage();
            // Refresh translated views only. Rebinding selectors can invoke setters with side effects,
            // such as applying the motion-wide IK default over individual bone overrides.
            foreach (var property in new[] { nameof(LanguageIndex), nameof(IkInfo), nameof(QueueCountText),
                nameof(PendingText), nameof(ConvertButtonText), nameof(CoreStatus), nameof(StatusText), nameof(ProgressText),
                nameof(PageTitle), nameof(SettingsFeedback) }) Notify(property);
        };
        Jobs.CollectionChanged += (_, _) =>
        {
        Notify(nameof(IsQueueEmpty)); Notify(nameof(QueueCountText)); Notify(nameof(PendingText)); Notify(nameof(CanConvert));
        if (IsIdle) { StatusText = Jobs.Count == 0 ? "队列已清空" : "动作已加入"; ProgressText = "queue-pending"; }
        };
    }

    public void Changed(string name) => Notify(name);
    public void AppendLog(string line)
    {
        var next = LogText + line + Environment.NewLine;
        LogText = next.Length > 80000 ? next[^60000..] : next;
    }

    public ConversionOptions Snapshot()
    {
        static double Number(string text, string label)
        {
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;
            throw new ArgumentException(L.F("{0}格式无效，请使用数字和小数点。", label));
        }
        static int Integer(string text, string label)
        {
            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
            throw new ArgumentException(L.F("{0}需要整数。", label));
        }
        return new()
        {
            Language = L.Instance.Preference, CorePath = CorePath.Trim(), PmxPath = PmxPath.Trim(), TargetIndex = TargetIndex, TargetPath = TargetPath.Trim(),
            OutputKind = SelectedMode.Value, OutputDirectory = OutputDirectory.Trim(), SampleRate = SampleRate,
            PositionScale = Number(PositionScale, L.T("位置缩放")), Coordinate = CoordinateIndex,
            Passes = Integer(Passes, L.T("求解轮数")), Threads = Integer(Threads, L.T("线程数")), MemoryMb = Integer(MemoryMb, L.T("缓存预算")),
            Duration = string.IsNullOrWhiteSpace(Duration) ? null : Number(Duration, L.T("限定时长")), UpperTangentError = Number(UpperTangentError, L.T("上肢插值误差")),
            BakeMorphs = BakeMorphs, FkContinuity = FkContinuity,
            ExpressionPath = ExpressionPath.Trim(), MorphMapPath = MorphMapPath.Trim()
        };
    }

    private static string FindCore()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "Core", "VmdConverterCore.exe");
        if (File.Exists(packaged)) return packaged;
        for (var parent = new DirectoryInfo(AppContext.BaseDirectory); parent != null; parent = parent.Parent)
        {
            var path = Path.Combine(parent.FullName, "artifacts", "desktop", "Core", "VmdConverterCore.exe");
            if (File.Exists(path)) return path;
        }
        return packaged;
    }

    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VMD2Anim-Neo", "settings.json");
    public void SaveSettings()
    {
        var options = Snapshot();
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, true);
    }
    public void LoadSettings()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            var o = JsonSerializer.Deserialize<ConversionOptions>(File.ReadAllText(SettingsPath));
            if (o == null) return;
            L.Instance.SetLanguage(o.Language);
            if (File.Exists(o.CorePath)) CorePath = o.CorePath;
            PmxPath = o.PmxPath; TargetIndex = Math.Clamp(o.TargetIndex, 0, 3); TargetPath = o.TargetPath;
            SelectedMode = OutputModes.FirstOrDefault(m => m.Value == o.OutputKind && (TargetIndex != 0 || m.Value == "humanoid-ik")) ?? OutputModes[0];
            OutputDirectory = o.OutputDirectory; SampleRate = SampleRates.Contains(o.SampleRate) ? o.SampleRate : 30;
            PositionScale = o.PositionScale.ToString(CultureInfo.InvariantCulture); CoordinateIndex = Math.Clamp(o.Coordinate, 0, 3);
            Passes = o.Passes.ToString(CultureInfo.InvariantCulture); Threads = o.Threads.ToString(CultureInfo.InvariantCulture);
            MemoryMb = o.MemoryMb.ToString(CultureInfo.InvariantCulture); Duration = o.Duration?.ToString(CultureInfo.InvariantCulture) ?? "";
            UpperTangentError = o.UpperTangentError.ToString(CultureInfo.InvariantCulture); BakeMorphs = o.BakeMorphs;
            FkContinuity = o.FkContinuity; ExpressionPath = o.ExpressionPath; MorphMapPath = o.MorphMapPath;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { AppendLog(L.T("无法读取上次设置：") + e.Message); }
    }
}
