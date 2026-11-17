using Avalonia;
using Avalonia.Styling;

namespace WgetUi.App.Services;

/// <summary>Applies the theme setting ("system", "light", "dark") to the whole app.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Injected service, kept instance-based for testability.")]
public sealed class ThemeService
{
    public const string Light = "light";
    public const string Dark = "dark";

    public void Apply(string theme)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                Light => ThemeVariant.Light,
                Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
