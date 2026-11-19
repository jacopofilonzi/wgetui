using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Localization;
using WgetUi.App.Services;
using WgetUi.App.ViewModels;
using WgetUi.Core.Models;
using WgetUi.Core.PackageLists;

namespace WgetUi.App.Tests;

/// <summary>Answers the file dialogs with fixed paths (null = the user cancelled).</summary>
internal sealed class FakeFilePicker : IFilePicker
{
    public string? SavePath { get; set; }

    public string? OpenPath { get; set; }

    public List<string> Suggested { get; } = [];

    public int Opened { get; private set; }

    public Task<string?> PickSaveListAsync(string suggestedFileName)
    {
        Suggested.Add(suggestedFileName);
        return Task.FromResult(SavePath);
    }

    public Task<string?> PickOpenListAsync()
    {
        Opened++;
        return Task.FromResult(OpenPath);
    }
}

public sealed class ExportViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wgetui-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeFilePicker _picker = new();
    private readonly ManualTime _time = new() { Now = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero) };
    private readonly ExportViewModel _vm;

    public ExportViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        Loc.Instance.SetLanguage("en");
        _vm = new ExportViewModel(
            [
                new Package("winget", "winget", "Git.Git", "Git", "2.56.0"),
                new Package("winget", PackageSource.LocalId, @"ARP\Machine\X64\Tool", "Some Tool", "1.0"),
                new Package("winget", "winget", "7zip.7zip", "7-Zip", "26.05", "26.06"),
                new Package("winget", "msstore", "9NKSQGP7F2NH", "WhatsApp", null),
            ],
            p => p.SourceId,
            _picker,
            _time,
            NullLogger.Instance);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void LocalPackagesAreLeftOutAndEverythingElseStartsSelected()
    {
        Assert.Equal(["7-Zip", "Git", "WhatsApp"], _vm.Rows.Select(r => r.Name));
        Assert.All(_vm.Rows, r => Assert.True(r.IsSelected));
        Assert.True(_vm.SelectAll);
        Assert.Equal("3 of 3 selected", _vm.SelectedText);
        Assert.Equal("wgetui-packages-2026-11-19.wgetui", _vm.SuggestedFileName);
    }

    [Fact]
    public void SelectAllIsTriStateOverTheVisibleRows()
    {
        _vm.Rows[0].IsSelected = false;
        Assert.Null(_vm.SelectAll);
        Assert.Equal("2 of 3 selected", _vm.SelectedText);

        _vm.SelectAll = false;
        Assert.False(_vm.SelectAll);
        Assert.False(_vm.SaveCommand.CanExecute(null));

        _vm.Filter = "git";
        Assert.Equal(["Git"], _vm.Rows.Select(r => r.Name));
        _vm.SelectAll = true;
        _vm.Filter = "";
        Assert.Equal(["Git.Git"], _vm.SelectedEntries().Select(e => e.Id));
        Assert.Null(_vm.SelectAll);
    }

    [Fact]
    public async Task SaveWritesTheSelectedPackagesWithTheirInstalledVersionsAndCloses()
    {
        var closed = false;
        _vm.CloseRequested += (_, _) => closed = true;
        _picker.SavePath = Path.Combine(_dir, "list.wgetui");
        _vm.Rows.Single(r => r.Id == "Git.Git").IsSelected = false;

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.Equal(["wgetui-packages-2026-11-19.wgetui"], _picker.Suggested);
        var list = PackageListFile.Parse(File.ReadAllBytes(_picker.SavePath));
        Assert.Equal(
            [
                new PackageListEntry("winget", "winget", "7zip.7zip", "7-Zip", "26.05"),
                new PackageListEntry("winget", "msstore", "9NKSQGP7F2NH", "WhatsApp", null),
            ],
            list.Packages);
        Assert.Equal(_time.Now, list.ExportedAt);
    }

    [Fact]
    public async Task CancellingTheDialogWritesNothingAndStaysOpen()
    {
        var closed = false;
        _vm.CloseRequested += (_, _) => closed = true;

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.Empty(Directory.GetFiles(_dir));
        Assert.False(_vm.HasError);
    }

    [Fact]
    public async Task AWriteFailureShowsAnErrorAndStaysOpen()
    {
        var closed = false;
        _vm.CloseRequested += (_, _) => closed = true;
        _picker.SavePath = Path.Combine(_dir, "missing-folder", "list.wgetui");

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.True(_vm.HasError);
        Assert.Equal(Loc.Instance.Error(ErrorCodes.ListWriteFailed), _vm.ErrorText);
    }
}
