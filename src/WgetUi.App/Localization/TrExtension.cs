using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace WgetUi.App.Localization;

/// <summary>
/// <c>Text="{l:Tr Search_Title}"</c>: a one-way binding to <see cref="Loc"/>'s indexer, so the text follows
/// language changes. Keys are checked against both .resx files by the App tests.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new ReflectionBinding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
}
