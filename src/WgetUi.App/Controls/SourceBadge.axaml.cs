using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace WgetUi.App.Controls;

/// <summary>
/// Pill showing where a package comes from. Colours and icon are looked up in the theme resources
/// (Themes/SourceBadges.axaml) by source id, falling back to the "default" entries.
/// </summary>
public partial class SourceBadge : UserControl
{
    public static readonly StyledProperty<string?> SourceIdProperty = AvaloniaProperty.Register<SourceBadge, string?>(nameof(SourceId));

    public static readonly StyledProperty<string?> SourceNameProperty = AvaloniaProperty.Register<SourceBadge, string?>(nameof(SourceName));

    // Rebound when the source changes; the old bindings are disposed first. Bindings die with the control.
    private readonly List<IDisposable> _bindings = [];

    public SourceBadge() => InitializeComponent();

    public string? SourceId
    {
        get => GetValue(SourceIdProperty);
        set => SetValue(SourceIdProperty, value);
    }

    public string? SourceName
    {
        get => GetValue(SourceNameProperty);
        set => SetValue(SourceNameProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == SourceNameProperty)
        {
            Label.Text = SourceName;
        }
        else if (change.Property == SourceIdProperty)
        {
            ApplyLook();
        }
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        ApplyLook();
    }

    private string KeyFor(string part)
    {
        var specific = $"SourceBadge.{SourceId}.{part}";
        return SourceId is not null && this.TryFindResource(specific, ActualThemeVariant, out _) ? specific : $"SourceBadge.default.{part}";
    }

    private void ApplyLook()
    {
        // Resource observables follow theme switches (light/dark) without extra wiring.
        foreach (var binding in _bindings)
        {
            binding.Dispose();
        }

        _bindings.Clear();
        var foreground = this.GetResourceObservable(KeyFor("Foreground"));
        _bindings.Add(Pill.Bind(Border.BackgroundProperty, this.GetResourceObservable(KeyFor("Background"))));
        _bindings.Add(Label.Bind(TextBlock.ForegroundProperty, foreground));
        _bindings.Add(Icon.Bind(FASymbolIcon.ForegroundProperty, foreground));
        if (this.TryFindResource(KeyFor("Icon"), ActualThemeVariant, out var icon) && icon is string name && Enum.TryParse<FASymbol>(name, out var symbol))
        {
            Icon.Symbol = symbol;
        }
    }
}
