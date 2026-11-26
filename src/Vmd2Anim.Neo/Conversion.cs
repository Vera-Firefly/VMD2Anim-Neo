using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Vmd2Anim.Neo;

// WPF tracks choices by identity; event subscriptions must not change equality or hash codes.
public sealed class OutputMode(string value, string labelKey) : INotifyPropertyChanged
{
    public string Value { get; } = value;
    public string LabelKey { get; } = labelKey;
    public string Label => L.T(LabelKey);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshLanguage() => PropertyChanged?.Invoke(this, new(null));
    public override string ToString() => Label;
}

public sealed record ConversionOptions
{
    public string Language { get; init; } = "system";
    public string CorePath { get; init; } = "";
    public string PmxPath { get; init; } = "";
    public int TargetIndex { get; init; }
    public string TargetPath { get; init; } = "";
    public string OutputKind { get; init; } = "humanoid-ik";
    public string OutputDirectory { get; init; } = "";
    public int SampleRate { get; init; } = 30;
    public double PositionScale { get; init; } = .08;
    public int Coordinate { get; init; } = 2;
    public int Passes { get; init; } = 3;
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount - 1, 1, 256);
    public int MemoryMb { get; init; } = 256;
    public double? Duration { get; init; }
    public double UpperTangentError { get; init; }
    public bool BakeMorphs { get; init; } = true;
    public bool FkContinuity { get; init; } = true;
    public string ExpressionPath { get; init; } = "";
    public string MorphMapPath { get; init; } = "";
    public Dictionary<string, string> SourceIk { get; init; } = new(StringComparer.Ordinal);

    public void Validate(IReadOnlyList<string> inputs, bool requireRuntime = true)
    {
        if (inputs.Count == 0) throw new ArgumentException(L.T("请先添加至少一个 VMD 动作。"));
        RequireFile(CorePath, ".exe", L.T("转换核心"));
        if (!Path.GetFileName(CorePath).Equals("VmdConverterCore.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(L.T("请选择 VmdConverterCore.exe。"));
        if (requireRuntime)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(CorePath))!;
            foreach (var name in new[] { "VmdConverterCore.Shared.dll", "VmdConverterCore.Hps.dll", "UnityHps.Core.dll", "UnityHps.Fbx.dll" })
                if (!File.Exists(Path.Combine(directory, name))) throw new ArgumentException(L.F("核心缺少 {0}，请重新构建或选择完整的 Core 运行目录。", name));
        }
        RequireFile(PmxPath, ".pmx", L.T("源模型"));
        foreach (var input in inputs) RequireFile(input, ".vmd", L.T("动作文件"));
        if (TargetIndex is < 0 or > 3) throw new ArgumentException(L.T("目标骨架类型无效。"));
        if (TargetIndex > 0) RequireFile(TargetPath, TargetIndex == 1 ? ".fbx" : ".json", L.T("目标骨架"));
        if (TargetIndex == 0 && OutputKind != "humanoid-ik") throw new ArgumentException(L.T("目标拟合模式需要选择目标骨架。"));
        if (!OutputKinds.Contains(OutputKind)) throw new ArgumentException(L.T("输出模式无效。"));
        if (!string.IsNullOrWhiteSpace(ExpressionPath)) RequireFile(ExpressionPath, ".vmd", L.T("表情文件"));
        if (!string.IsNullOrWhiteSpace(MorphMapPath)) RequireFile(MorphMapPath, ".json", L.T("形态键映射"));
        if (string.IsNullOrWhiteSpace(OutputDirectory)) throw new ArgumentException(L.T("请选择输出目录。"));
        var fullOutput = Path.GetFullPath(OutputDirectory);
        if (File.Exists(fullOutput)) throw new ArgumentException(L.T("输出目录指向了一个文件，请选择文件夹。"));
        if (SampleRate is < 1 or > 240) throw new ArgumentException(L.T("采样率必须为 1–240 FPS。"));
        if (!double.IsFinite(PositionScale) || PositionScale <= 0 || PositionScale > float.MaxValue) throw new ArgumentException(L.T("位置缩放必须为有限正数。"));
        if (Coordinate is < 0 or > 3 || Passes is < 1 or > 20) throw new ArgumentException(L.T("坐标转换或求解轮数无效。"));
        if (Threads is < 1 or > 256 || MemoryMb is < 32 or > 8192) throw new ArgumentException(L.T("线程数应为 1–256，姿态缓存预算应为 32–8192 MiB。"));
        if (Duration.HasValue && (!double.IsFinite(Duration.Value) || Duration.Value <= 0 || Duration.Value > float.MaxValue)) throw new ArgumentException(L.T("限定时长必须为有限正数，或留空。"));
        if (!double.IsFinite(UpperTangentError) || UpperTangentError is < 0 or > .05) throw new ArgumentException(L.T("上肢插值误差必须为 0–0.05。"));
        foreach (var pair in SourceIk)
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is not ("on" or "off" or "vmd"))
                throw new ArgumentException(L.T("源 IK 名称或模式无效，请重新读取 PMX IK。"));
    }

    public static readonly string[] OutputKinds = ["humanoid-ik", "target-humanoid", "target-humanoid-leg", "target-humanoid-arm", "target-humanoid-all", "target-fk", "target-fk-leg", "target-fk-arm", "target-fk-all"];

    private static void RequireFile(string path, string extension, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new ArgumentException(L.F("找不到{0}，请检查路径。", label));
        if (!Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(L.F("{0}需要 {1} 文件。", label, extension));
    }

    public ProcessStartInfo BuildStartInfo(string input, string output)
    {
        // CreateNoWindow gives this .NET CLI no console. It registers CodePagesEncodingProvider,
        // so Console.OutputEncoding uses Windows ANSI (code page 0), not necessarily UTF-8.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(0);
        var start = new ProcessStartInfo(Path.GetFullPath(CorePath))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = encoding, StandardErrorEncoding = encoding,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(CorePath))!
        };
        void Add(string key, object value)
        {
            start.ArgumentList.Add(key);
            start.ArgumentList.Add(value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value.ToString()!);
        }
        start.ArgumentList.Add("--anim-w");
        Add("--pmx", Path.GetFullPath(PmxPath)); Add("--vmd", Path.GetFullPath(input)); Add("--output", Path.GetFullPath(output));
        Add("--output-kind", OutputKind); Add("--sample-rate", SampleRate); Add("--position-scale", PositionScale);
        Add("--coordinate", Coordinate); Add("--passes", Passes); Add("--threads", Threads);
        Add("--job-threads", 1); Add("--memory-mb", MemoryMb); Add("--upper-tangent-error", UpperTangentError);
        Add("--bake-morphs", BakeMorphs ? "true" : "false"); Add("--fk-continuity", FkContinuity ? "true" : "false");
        if (TargetIndex > 0) Add(TargetIndex switch { 1 => "--fbx", 2 => "--hps-rig", _ => "--descriptor" }, Path.GetFullPath(TargetPath));
        if (Duration.HasValue) Add("--duration", Duration.Value);
        if (!string.IsNullOrWhiteSpace(ExpressionPath)) Add("--expression-vmd", Path.GetFullPath(ExpressionPath));
        if (!string.IsNullOrWhiteSpace(MorphMapPath)) Add("--morph-map", Path.GetFullPath(MorphMapPath));
        foreach (var pair in SourceIk) Add("--ik", pair.Key + "=" + pair.Value);
        return start;
    }

    // PowerShell single quotes preserve dollar signs, backticks, spaces and Unicode.
    public static string DisplayCommand(ProcessStartInfo start)
    {
        static string Quote(string text) => "'" + text.Replace("'", "''") + "'";
        return "& " + Quote(start.FileName) + " " + string.Join(" ", start.ArgumentList.Select(Quote));
    }
}

public static class OutputPaths
{
    public static string Reserve(string directory, string input)
    {
        Directory.CreateDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(input);
        for (var index = 0; index < 100000; index++)
        {
            var path = Path.Combine(directory, stem + (index == 0 ? "" : $" ({index + 1})") + ".anim");
            if (File.Exists(path + ".partial")) continue;
            try
            {
                using var reserved = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return path;
            }
            catch (IOException) when (File.Exists(path)) { }
        }
        throw new IOException(L.T("同名输出过多，请选择其他输出目录。"));
    }
}

public sealed record CoreResult(string Output, int Samples, long Bytes, long ElapsedMs);

public static class CoreRunner
{
    public static async Task<SourceIkBone[]> ReadSourceIkAsync(string corePath, string pmxPath, CancellationToken token)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var process = new Process { StartInfo = new ProcessStartInfo(Path.GetFullPath(corePath))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.GetEncoding(0), StandardErrorEncoding = Encoding.GetEncoding(0),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(corePath))!
        } };
        process.StartInfo.ArgumentList.Add("--pmx"); process.StartInfo.ArgumentList.Add(Path.GetFullPath(pmxPath));
        process.StartInfo.ArgumentList.Add("--list-ik");
        token.ThrowIfCancellationRequested();
        process.Start();
        using var stop = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        });
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException(L.T("无法读取 PMX IK：") + error.Trim());
        return JsonSerializer.Deserialize<SourceIkBone[]>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new IOException(L.T("Core 未返回 IK 骨骼列表。"));
    }

    public static async Task<CoreResult> RunAsync(ConversionOptions options, string input, string output, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = options.BuildStartInfo(input, output) };
        CoreResult? result = null;
        var tail = new Queue<string>();
        var gate = new object();
        void Receive(string? line, bool stdout)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (gate)
            {
                tail.Enqueue(line);
                while (tail.Count > 12) tail.Dequeue();
                if (stdout)
                {
                    try
                    {
                        using var json = JsonDocument.Parse(line);
                        var root = json.RootElement;
                        if (root.TryGetProperty("format", out var format) && format.GetString() == "unity-anim")
                            result = new(root.GetProperty("output").GetString()!, root.GetProperty("sampleCount").GetInt32(), root.GetProperty("bytes").GetInt64(), root.GetProperty("elapsedMs").GetInt64());
                    }
                    catch (JsonException) { }
                    catch (KeyNotFoundException) { }
                    catch (InvalidOperationException) { }
                    catch (FormatException) { }
                }
            }
            log(line);
        }
        process.OutputDataReceived += (_, e) => Receive(e.Data, true);
        process.ErrorDataReceived += (_, e) => Receive(e.Data, false);
        if (!process.Start()) throw new IOException(L.T("无法启动转换核心。"));
        using var cancellation = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        // Wait without cancellation so the process and both redirected streams are drained before cleanup.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException(L.F("Core 退出码 {0}。\n", process.ExitCode) + string.Join(Environment.NewLine, tail));
        if (result == null || !File.Exists(output) || new FileInfo(output).Length == 0 || !Path.GetFullPath(result.Output).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new IOException(L.T("Core 未返回有效的 .anim 转换结果，请查看日志。"));
        return result;
    }
}
