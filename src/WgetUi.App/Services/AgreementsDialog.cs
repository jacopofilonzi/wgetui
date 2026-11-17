using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using WgetUi.App.Localization;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Services;

/// <summary>
/// The agreements modal: lists every package/source agreement of a request (name + link) and returns true
/// only on an explicit Accept. Built in code because its content is a dynamic list; no logic beyond layout.
/// </summary>
public sealed class AgreementsDialog : IAgreementsConfirmer
{
    public Task<bool> ConfirmAsync(IReadOnlyList<PackageAgreements> agreements, CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(() => ShowAsync(agreements));

    private static async Task<bool> ShowAsync(IReadOnlyList<PackageAgreements> agreements)
    {
        var loc = Loc.Instance;
        var list = new StackPanel { Spacing = 16 };
        foreach (var item in agreements)
        {
            list.Children.Add(PackageSection(item, loc));
        }

        var content = new StackPanel { Spacing = 12, MaxWidth = 520 };
        content.Children.Add(new TextBlock { Text = loc["Agreements_Message"], TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 360,
            Content = list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Top,
        });
        content.Children.Add(new TextBlock { Text = loc["Agreements_SettingHint"], TextWrapping = TextWrapping.Wrap, Classes = { "Secondary" } });

        var dialog = new FAContentDialog
        {
            Title = loc.Format("Agreements_Title", agreements.Count),
            Content = content,
            PrimaryButtonText = loc["Agreements_Accept"],
            CloseButtonText = loc["Common_Cancel"],
            DefaultButton = FAContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == FAContentDialogResult.Primary;
    }

    private static StackPanel PackageSection(PackageAgreements item, Loc loc)
    {
        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock
        {
            Text = $"{item.Package.Name} ({item.Package.Id})",
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var agreement in item.PackageTerms)
        {
            section.Children.Add(AgreementLine(agreement));
        }

        if (item.SourceTerms.Count > 0)
        {
            section.Children.Add(new TextBlock { Text = loc.Format("Agreements_SourceTerms", item.Package.SourceId), Classes = { "Secondary" }, Margin = new Avalonia.Thickness(0, 4, 0, 0) });
            foreach (var agreement in item.SourceTerms)
            {
                section.Children.Add(AgreementLine(agreement));
            }
        }

        if (item.License is not null)
        {
            // Informational, not an agreement: shown as a reference to the license.
            section.Children.Add(AgreementLine(new Agreement(loc.Format("Agreements_License", item.License), null, item.LicenseUrl)));
        }

        return section;
    }

    // A reference, never a wall of text: link when there is a URL (opens the default browser), otherwise the
    // text collapsed in an expander.
    private static Control AgreementLine(Agreement agreement)
    {
        if (agreement.Url is not null && Uri.TryCreate(agreement.Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return new HyperlinkButton { Content = agreement.Label, NavigateUri = uri, Padding = new Avalonia.Thickness(0), Margin = new Avalonia.Thickness(12, 0, 0, 0) };
        }

        if (agreement.Text is null)
        {
            return new TextBlock { Text = agreement.Label, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(12, 0, 0, 0) };
        }

        return new Expander
        {
            Header = agreement.Label,
            Content = new TextBlock { Text = agreement.Text, TextWrapping = TextWrapping.Wrap },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Avalonia.Thickness(12, 0, 0, 0),
        };
    }
}
