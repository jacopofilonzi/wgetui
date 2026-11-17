using System.ComponentModel;
using System.Globalization;
using System.Resources;
using WgetUi.Core.Models;

namespace WgetUi.App.Localization;

/// <summary>
/// UI strings from Resources/Strings.resx (English, neutral) and Strings.it.resx. XAML binds through
/// <see cref="TrExtension"/> to the indexer, so switching language updates every bound string live;
/// view models re-read their computed texts on <see cref="CultureChanged"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string English = "en";
    public const string Italian = "it";
    private static readonly ResourceManager Strings = new("WgetUi.App.Resources.Strings", typeof(Loc).Assembly);

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CultureChanged;

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(English);

    public string this[string key] => Strings.GetString(key, Culture) ?? $"!{key}!";

    /// <summary>"system" picks Italian on an Italian Windows, English otherwise.</summary>
    public static string ResolveLanguage(string? setting) => setting switch
    {
        English or Italian => setting,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Italian ? Italian : English,
    };

    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);

    /// <summary>Translated text for a stable error code (see <see cref="ErrorCodes"/>).</summary>
    public string Error(string? code)
    {
        var text = code is null ? null : Strings.GetString("Error_" + code, Culture);
        return text ?? this["Error_" + ErrorCodes.Unexpected];
    }

    public void SetLanguage(string language)
    {
        var culture = CultureInfo.GetCultureInfo(ResolveLanguage(language));
        if (Equals(culture, Culture))
        {
            return;
        }

        Culture = culture;
        CultureInfo.CurrentUICulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
