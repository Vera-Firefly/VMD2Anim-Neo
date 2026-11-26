using System.Globalization;
using System.IO;
using Vmd2Anim.Neo;

var root = Path.Combine(Path.GetTempPath(), "Neo-bridge-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    passed++; Console.WriteLine("PASS " + label);
}
void Reject(ConversionOptions value, string input, string label)
{
    try { value.Validate([input], false); }
    catch (ArgumentException) { Check(true, label); return; }
    throw new Exception("Accepted invalid input: " + label);
}
try
{
    var input = Path.Combine(root, "日本語 动作 ' $x `test.vmd");
    var pmx = Path.Combine(root, "源模型.pmx");
    var core = Path.Combine(root, "VmdConverterCore.exe");
    foreach (var path in new[] { input, pmx, core }) File.WriteAllText(path, "fixture");
    var o = new ConversionOptions { CorePath = core, PmxPath = pmx, OutputDirectory = root };
    o.Validate([input], false);
    Check(true, "Unicode paths validate");
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
    var command = o.BuildStartInfo(input, Path.Combine(root, "输出.anim"));
    Check(command.ArgumentList[command.ArgumentList.IndexOf("--position-scale") + 1] == "0.08", "Decimal arguments use invariant culture");
    Check(command.ArgumentList.Contains(input) && !command.UseShellExecute, "Paths pass as individual arguments without a shell");
    Check(ConversionOptions.DisplayCommand(command).Contains("'' $x `test.vmd'"), "PowerShell command protects quotes and metacharacters");
    var legacyJson = System.Text.Json.JsonSerializer.Serialize(o)[..^1] + ",\"TargetLegIk\":true}";
    var legacySettings = System.Text.Json.JsonSerializer.Deserialize<ConversionOptions>(legacyJson)!;
    var migratedCommand = legacySettings.BuildStartInfo(input, Path.Combine(root, "legacy.anim"));
    Check(!migratedCommand.ArgumentList.Contains("--target-leg-ik"), "Legacy target-leg IK setting cannot enable extra target IK");
    var workspace = new WorkspaceViewModel();
    workspace.ApplyIkBones(pmx, [new(1, "右足ＩＫ", "leg IK R"), new(2, "左足ＩＫ", "leg IK L")]);
    workspace.AddMotion(input); workspace.AddMotion(pmx);
    var firstJob = workspace.Jobs[0]; var otherJob = workspace.Jobs[1];
    firstJob.IkDefaultIndex = 2;
    firstJob.IkSwitches[1].Mode = IkMode.All[1];
    var frozenIk = firstJob.WithIk(o);
    firstJob.IkSwitches[0].Mode = IkMode.All[0];
    Check(frozenIk.SourceIk["右足ＩＫ"] == "off" && frozenIk.SourceIk["左足ＩＫ"] == "on" && otherJob.WithIk(o).SourceIk.Count == 0, "Per-motion IK overrides are independent and frozen");
    var ikArgs = frozenIk.BuildStartInfo(input, Path.Combine(root, "ik.anim")).ArgumentList;
    Check(ikArgs.Count(a => a == "--ik") == 2 && ikArgs.Contains("右足ＩＫ=off") && ikArgs.Contains("左足ＩＫ=on"), "Per-bone source IK uses repeated shell-free CLI arguments");
    workspace.CompleteMotion(firstJob);
    Check(workspace.Jobs.Count == 1 && workspace.PendingText.Contains("1"), "Successful motion leaves queue and updates pending total");
    workspace.Jobs.Remove(otherJob);
    Check(workspace.IsQueueEmpty && workspace.PendingText.Contains("0") && workspace.ProgressText == workspace.PendingText, "Manual removal clears pending count and idle status");
    workspace.AddMotion(input); workspace.Jobs[0].IkDefaultIndex = 2;
    workspace.ApplyIkBones(pmx + "-changed", [new(3, "different IK", "")]);
    Check(workspace.Jobs[0].WithIk(o).SourceIk.Count == 0, "Changing PMX clears incompatible per-motion overrides");
    foreach (var index in new[] { 1, 2, 3 })
    {
        var target = Path.Combine(root, index == 1 ? "target.fbx" : "rig.json"); File.WriteAllText(target, "{}");
        var targeted = o with { TargetIndex = index, TargetPath = target, OutputKind = "target-fk-all" };
        targeted.Validate([input], false);
        var arguments = targeted.BuildStartInfo(input, Path.Combine(root, "out.anim")).ArgumentList;
        Check(arguments.Contains(index == 1 ? "--fbx" : index == 2 ? "--hps-rig" : "--descriptor"), "Explicit target argument " + index);
    }
    Reject(o with { PositionScale = double.NaN }, input, "Nonfinite scale rejected");
    Reject(o with { UpperTangentError = .051 }, input, "Interpolation budget validated");
    Reject(o with { Duration = -1 }, input, "Negative duration rejected");
    Reject(o with { Threads = 257 }, input, "Thread limit validated");
    Reject(o with { MemoryMb = 31 }, input, "Memory budget validated");
    Reject(o with { OutputKind = "target-fk" }, input, "Target mode cannot silently fall back to source rig");
    Reject(o with { TargetIndex = 1, TargetPath = "missing.fbx" }, input, "Missing target rejected");
    Reject(o with { PmxPath = input }, input, "Source model extension validated");
    var original = Path.Combine(root, Path.GetFileNameWithoutExtension(input) + ".anim");
    File.WriteAllText(original, "keep me");
    var reserved = OutputPaths.Reserve(root, input);
    var second = OutputPaths.Reserve(root, input);
    Check(original != reserved && reserved != second && File.ReadAllText(original) == "keep me", "Output reservations preserve existing and duplicate names");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await CoreRunner.RunAsync(o, input, reserved, _ => { }, cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(true, "Pre-cancelled conversion never starts a process"); }

    if (args.Length >= 4 && args[0] == "--integration")
    {
        var real = o with { CorePath = Path.GetFullPath(args[1]), PmxPath = Path.GetFullPath(args[2]), Duration = .2, Threads = 2 };
        var motion = Path.GetFullPath(args[3]);
        var bones = await CoreRunner.ReadSourceIkAsync(real.CorePath, real.PmxPath, CancellationToken.None);
        Check(bones.Length > 0 && bones.All(b => !string.IsNullOrWhiteSpace(b.Key)), "Real Core lists PMX IK bones");
        real.Validate([motion]);
        var output = Path.Combine(root, "真实转换.anim");
        var lines = new List<string>();
        var result = await CoreRunner.RunAsync(real, motion, output, line => { lock (lines) lines.Add(line); Console.WriteLine(line); }, CancellationToken.None);
        Check(result.Samples == 7 && File.ReadAllText(output).Contains("AnimationClip:"), "Real Core source-PMX to Unity YAML .anim");
        Check(lines.Any(line => line.Contains(Path.GetFileName(motion), StringComparison.Ordinal)), "Core diagnostics preserve Unicode filenames");
        var overridden = real with { SourceIk = new() { [bones[0].Key] = "off" } };
        var overrideResult = await CoreRunner.RunAsync(overridden, motion, Path.Combine(root, "source-ik-off.anim"), _ => { }, CancellationToken.None);
        Check(overrideResult.Samples == 7, "Real Core converts with a per-motion source IK override");
        if (args.Length >= 5)
        {
            var target = real with { TargetIndex = 1, TargetPath = Path.GetFullPath(args[4]), OutputKind = "target-fk-all" };
            target.Validate([motion]);
            var targetOutput = Path.Combine(root, "目标FK.anim");
            var fk = await CoreRunner.RunAsync(target, motion, targetOutput, Console.WriteLine, CancellationToken.None);
            Check(fk.Samples == 7 && fk.Bytes > 0 && File.ReadAllText(targetOutput).Contains("AnimationClip:"), "Real Core FBX target-FK to .anim");
        }
        var malformed = Path.Combine(root, "损坏.vmd"); File.WriteAllText(malformed, "bad vmd");
        try { await CoreRunner.RunAsync(real, malformed, Path.Combine(root, "bad.anim"), _ => { }, CancellationToken.None); throw new Exception("Failure not detected"); }
        catch (IOException) { Check(true, "Real Core failure propagates to UI bridge"); }
        using var stop = new CancellationTokenSource();
        stop.CancelAfter(150);
        try { await CoreRunner.RunAsync(real with { Duration = null }, motion, Path.Combine(root, "cancel.anim"), _ => { }, stop.Token); throw new Exception("Running conversion not cancelled"); }
        catch (OperationCanceledException) { Check(true, "Running Core process is cancelled and drained"); }
    }
    Console.WriteLine($"{passed} checks passed.");
}
finally { Directory.Delete(root, true); }
