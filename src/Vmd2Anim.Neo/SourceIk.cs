namespace Vmd2Anim.Neo;

public sealed record SourceIkBone(int Index, string Name, string EnglishName)
{
    public string Key => string.IsNullOrWhiteSpace(Name) ? EnglishName : Name;
    public string Label => string.IsNullOrWhiteSpace(EnglishName) || EnglishName == Name ? Key : $"{Key} · {EnglishName}";
}

public sealed record IkMode(string Value, string Label)
{
    public override string ToString() => Label;
    public static IReadOnlyList<IkMode> All { get; } = [new("vmd", "跟随 VMD"), new("on", "强制开启"), new("off", "强制关闭")];
}

public sealed class SourceIkSwitch(SourceIkBone bone) : Observable
{
    public SourceIkBone Bone { get; } = bone;
    public string Label => Bone.Label;
    private IkMode mode = IkMode.All[0];
    public IkMode Mode { get => mode; set { mode = value; Notify(); } }
}
