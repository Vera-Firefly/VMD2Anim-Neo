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
    public ObservableCollection<SourceIkSwitch> IkSwitches { get; } = [];
    private int ikDefaultIndex;
    public IReadOnlyList<IkMode> IkModes => IkMode.All;
    public int IkDefaultIndex
    {
        get => ikDefaultIndex;
        set { ikDefaultIndex = Math.Clamp(value, 0, 2); foreach (var item in IkSwitches) item.Mode = IkMode.All[ikDefaultIndex]; Notify(); Notify(nameof(IkSummary)); }
    }
    public string IkSummary => IkSwitches.Count == 0 ? "源 IK · 等待读取 PMX" : $"源 IK · {IkSwitches.Count(s => s.Mode.Value != "vmd")} 项覆盖";
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
    public ConversionOptions WithIk(ConversionOptions options) => options with
    {
        SourceIk = IkSwitches.Where(s => s.Mode.Value != "vmd").ToDictionary(s => s.Bone.Key, s => s.Mode.Value, StringComparer.Ordinal)
    };
    public string InputPath { get; } = Path.GetFullPath(input);
    public string Name => Path.GetFileName(InputPath);
    public string OutputPath { get; set; } = "";
    private string status = "待转换";
    private string detail = $"{new FileInfo(input).Length / 1024d:N0} KB · VMD 动作";
    public string Status { get => status; set { status = value; Notify(); Notify(nameof(StatusBrush)); } }
    public string Detail { get => detail; set { detail = value; Notify(); } }
    public Brush StatusBrush => new SolidColorBrush(Status switch { "已完成" => Color.FromRgb(33, 117, 86), "失败" => Color.FromRgb(174, 54, 61), "转换中" => Color.FromRgb(40, 94, 211), _ => Color.FromRgb(83, 101, 124) });
}

public sealed class WorkspaceViewModel : Observable
{
    public ObservableCollection<MotionJob> Jobs { get; } = [];
    public string[] TargetKinds { get; } = ["使用源 PMX 骨架", "目标 FBX 模型", "HPS 标定 Rig · JSON", "骨架 Descriptor · JSON"];
    public int[] SampleRates { get; } = [24, 30, 60, 120];
    public string[] Coordinates { get; } = ["翻转 Z", "翻转 X", "绕 Y 轴旋转 180° · 默认", "不转换坐标"];
    public ObservableCollection<OutputMode> OutputModes { get; } =
    [
        new("humanoid-ik", "Humanoid · 源 IK", "输出 Root 与肌肉曲线；使用 PMX 的 IK 求解。"),
        new("target-humanoid", "Humanoid · 正常模式", "目标根运动、躯干和下肢；保留源 Humanoid 上肢。"),
        new("target-humanoid-leg", "Humanoid · 下肢拟合", "显式使用目标下肢策略，输出整段全身动作。"),
        new("target-humanoid-arm", "Humanoid · 上肢拟合", "拟合目标上肢，保留源躯干和下肢。"),
        new("target-humanoid-all", "Humanoid · 全身拟合", "拟合目标全身，包含目标上肢反解。"),
        new("target-fk", "FK · 正常模式", "沿目标骨骼路径输出局部旋转与必要位移。"),
        new("target-fk-leg", "FK · 下肢拟合", "使用目标下肢策略，输出全身 FK 曲线。"),
        new("target-fk-arm", "FK · 上肢拟合", "使用目标上肢拟合，输出全身 FK 曲线。"),
        new("target-fk-all", "FK · 全身拟合", "拟合目标全身并输出目标骨架 FK 曲线。")
    ];
    private string corePath = FindCore();
    public string CorePath { get => corePath; set { corePath = value; Notify(); Notify(nameof(CoreStatus)); Notify(nameof(CoreStatusBrush)); } }
    public string PmxPath { get; set; } = "";
    public SourceIkBone[] SourceIkBones { get; private set; } = [];
    public string LoadedIkPmx { get; private set; } = "";
    private string ikInfo = "选择 PMX 后读取源 IK；每个动作可单独设置。";
    public string IkInfo { get => ikInfo; set { ikInfo = value; Notify(); } }
    public void ApplyIkBones(string pmx, SourceIkBone[] bones)
    {
        bool preserve = LoadedIkPmx.Equals(pmx, StringComparison.OrdinalIgnoreCase);
        SourceIkBones = bones; LoadedIkPmx = pmx;
        foreach (var job in Jobs) job.SetIkBones(bones, preserve);
        IkInfo = bones.Length == 0 ? "此 PMX 没有 IK 骨骼。" : $"已读取 {bones.Length} 个源 IK · 在动作行内独立设置。";
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
        var job = new MotionJob(path); job.SetIkBones(SourceIkBones, false); Jobs.Add(job);
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
            if (targetIndex == value) return;
            targetIndex = value; TargetPath = ""; Notify(); Notify(nameof(TargetPath)); Notify(nameof(HasTarget)); Notify(nameof(TargetHint));
            SelectedMode = OutputModes[value == 0 ? 0 : 1];
        }
    }
    public bool HasTarget => TargetIndex != 0;
    public string TargetHint => TargetIndex switch { 0 => "无需目标文件，直接使用 PMX Humanoid 骨架。", 1 => "自动读取相邻 .meta 的骨骼映射和导入设置。", 2 => "使用 Unity 导出的 HPS rig.json，保留标定。", _ => "使用由 Core 导出的骨架描述 JSON。" };
    private OutputMode? selectedMode;
    public OutputMode SelectedMode { get => selectedMode ?? OutputModes[0]; set { selectedMode = value; Notify(); } }
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
    public string QueueCountText => $"{Jobs.Count} 个文件";
    public string PendingText => $"待转换 {Jobs.Count} 个动作";
    public string ConvertButtonText => IsRunning ? "正在转换…" : "开始转换";
    public string CoreStatus => HasCoreRuntime ? "Core 已就绪" : "待配置 Core";
    public bool HasCoreRuntime => File.Exists(CorePath) && new[] { "VmdConverterCore.Shared.dll", "VmdConverterCore.Hps.dll", "UnityHps.Core.dll", "UnityHps.Fbx.dll" }.All(n => File.Exists(Path.Combine(Path.GetDirectoryName(CorePath) ?? "", n)));
    public Brush CoreStatusBrush => new SolidColorBrush(HasCoreRuntime ? Color.FromRgb(33, 117, 86) : Color.FromRgb(155, 102, 33));
    private string statusText = "准备好下一段动作";
    public string StatusText { get => statusText; set { statusText = value; Notify(); } }
    private string progressText = "选择源 PMX，添加 VMD，然后开始转换。";
    public string ProgressText { get => progressText; set { progressText = value; Notify(); } }
    private double progressValue;
    public double ProgressValue { get => progressValue; set { progressValue = value; Notify(); } }
    private string logText = "";
    public string LogText { get => logText; set { logText = value; Notify(); } }
    private bool showLog;
    public bool ShowLog { get => showLog; set { showLog = value; Notify(); } }
    public WorkspaceViewModel() => Jobs.CollectionChanged += (_, _) =>
    {
        Notify(nameof(IsQueueEmpty)); Notify(nameof(QueueCountText)); Notify(nameof(PendingText)); Notify(nameof(CanConvert));
        if (IsIdle) { StatusText = Jobs.Count == 0 ? "队列已清空" : "动作已加入，检查设置后即可转换"; ProgressText = PendingText; }
    };

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
            throw new ArgumentException($"{label}格式无效，请使用数字和小数点。");
        }
        static int Integer(string text, string label)
        {
            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
            throw new ArgumentException($"{label}需要整数。");
        }
        return new()
        {
            CorePath = CorePath.Trim(), PmxPath = PmxPath.Trim(), TargetIndex = TargetIndex, TargetPath = TargetPath.Trim(),
            OutputKind = SelectedMode.Value, OutputDirectory = OutputDirectory.Trim(), SampleRate = SampleRate,
            PositionScale = Number(PositionScale, "位置缩放"), Coordinate = CoordinateIndex,
            Passes = Integer(Passes, "求解轮数"), Threads = Integer(Threads, "线程数"), MemoryMb = Integer(MemoryMb, "缓存预算"),
            Duration = string.IsNullOrWhiteSpace(Duration) ? null : Number(Duration, "限定时长"), UpperTangentError = Number(UpperTangentError, "上肢插值误差"),
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
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { AppendLog("无法读取上次设置：" + e.Message); }
    }
}
