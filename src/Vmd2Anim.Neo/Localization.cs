using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace Vmd2Anim.Neo;

public sealed class L : INotifyPropertyChanged
{
    public static L Instance { get; } = new();
    private readonly Dictionary<string, string> english;
    private string preference = "system";
    private string language = ResolveLanguage("system", CultureInfo.CurrentUICulture);
    private L()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("Vmd2Anim.Neo.Locales.en.json")!;
        english = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Preference => preference;
    public string Language => language;
    public string this[string key] => language == "zh-CN" ? key : english.GetValueOrDefault(key, key);
    public static string T(string key) => Instance[key];
    public static string F(string key, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, T(key), arguments);
    public static string ResolveLanguage(string? preference, CultureInfo systemCulture) => preference switch
    {
        "en" => "en",
        "zh-CN" => "zh-CN",
        _ => systemCulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en"
    };
    public void SetLanguage(string? value)
    {
        preference = value is "en" or "zh-CN" ? value : "system";
        language = ResolveLanguage(preference, CultureInfo.CurrentUICulture);
        PropertyChanged?.Invoke(this, new("Item[]"));
    }
}

// Bind to the language source so open pages update without losing queue or focus.
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = L.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
