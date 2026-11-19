using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Services;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;
using WgetUi.Core.PackageLists;

namespace WgetUi.App.Tests;

/// <summary>Opening lists: from a file, from a URL (fake HTTP handler, never the network), and the URL popup.</summary>
public sealed class PackageListImportTests : IAsyncDisposable
{
    private const string ValidList = """{ "format": "wgetui-packages", "formatVersion": 1, "packages": [ { "managerId": "fake", "sourceId": "main", "id": "a" } ] }""";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wgetui-tests", Guid.NewGuid().ToString("N"));
    private readonly ScriptedManager _manager = new() { ExtraCapabilities = ManagerCapabilities.ListInstalled | ManagerCapabilities.FindById };
    private readonly OperationQueue _queue;
    private readonly SettingsService _settings;
    private readonly string _settingsDir;
    private readonly PackageListOverlay _overlay = new();
    private readonly FakeFilePicker _picker = new();
    private readonly PackageListLauncher _launcher;

    public PackageListImportTests()
    {
        Loc.Instance.SetLanguage("en");
        Directory.CreateDirectory(_dir);
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(registry, NullLogger<OperationQueue>.Instance);
        (_settings, _settingsDir) = TestSettings.Create();
        var actions = new PackageActions(new OperationRequester(registry, _queue, new NoAgreements(), NullLogger<OperationRequester>.Instance), _settings, NullLogger<PackageActions>.Instance);
        _launcher = new PackageListLauncher(registry, _overlay, _picker, new ManualTime(), NullLoggerFactory.Instance, actions, new HttpPackageListDownloader(new HttpClient(new FakeHandler(_ => Respond(ValidList))), NullLogger<HttpPackageListDownloader>.Instance));
    }

    public async ValueTask DisposeAsync()
    {
        await _queue.DisposeAsync();
        Directory.Delete(_dir, recursive: true);
        if (Directory.Exists(_settingsDir))
        {
            Directory.Delete(_settingsDir, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Respond(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpPackageListDownloader Downloader(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new FakeHandler(respond)), NullLogger<HttpPackageListDownloader>.Instance);

    private async Task<ImportViewModel> OpenAsync(Func<Task> open)
    {
        var shown = open();
        var vm = await WaitForAsync<ImportViewModel>();
        vm.CloseCommand.Execute(null);
        await shown;
        return vm;
    }

    [Fact]
    public async Task AFileOpensTheImportPopup()
    {
        var path = Path.Combine(_dir, "home.wgetui");
        await File.WriteAllTextAsync(path, ValidList, Ct);

        var vm = await OpenAsync(() => _launcher.OpenFileAsync(path));

        Assert.False(vm.HasError);
        Assert.Equal(["a"], vm.Rows.Select(r => r.Id));
        Assert.StartsWith("home.wgetui · 1 package", vm.Subtitle, StringComparison.Ordinal);
        Assert.Empty(_queue.Operations);
    }

    [Theory]
    [InlineData("""{ "format": "wgetui-packages", "formatVersion": 9, "packages": [] }""", ErrorCodes.ListNewerFormat)]
    [InlineData("hello", ErrorCodes.ListInvalid)]
    public async Task ABadFileShowsItsError(string content, string code)
    {
        var path = Path.Combine(_dir, "bad.wgetui");
        await File.WriteAllTextAsync(path, content, Ct);

        var vm = await OpenAsync(() => _launcher.OpenFileAsync(path));

        Assert.True(vm.HasError);
        Assert.Contains(Loc.Instance.Error(code), vm.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingFileShowsReadFailed()
    {
        var vm = await OpenAsync(() => _launcher.OpenFileAsync(Path.Combine(_dir, "nope.wgetui")));

        Assert.Contains(Loc.Instance.Error(ErrorCodes.ListReadFailed), vm.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FromFileUsesTheOpenDialogAndCancellingShowsNothing()
    {
        await _launcher.ImportFromFileAsync();

        Assert.Equal(1, _picker.Opened);
        Assert.False(_overlay.IsOpen);
    }

    [Theory]
    [InlineData("https://example.com/lists/home.wgetui", true)]
    [InlineData("  http://example.com  ", true)]
    [InlineData("ftp://example.com/a.wgetui", false)]
    [InlineData("file:///C:/a.wgetui", false)]
    [InlineData("example.com/a.wgetui", false)]
    [InlineData("", false)]
    public void OnlyHttpAndHttpsAddressesAreAccepted(string text, bool valid) =>
        Assert.Equal(valid, HttpPackageListDownloader.TryParseUrl(text, out _));

    [Fact]
    public void TheLabelIsTheHostAndTheFile()
    {
        Assert.Equal("example.com/home list.wgetui", HttpPackageListDownloader.Label(new Uri("https://example.com/x/home%20list.wgetui?raw=1")));
        Assert.Equal("example.com", HttpPackageListDownloader.Label(new Uri("https://example.com/")));
    }

    [Fact]
    public async Task ADownloadIsParsed()
    {
        HttpRequestMessage? seen = null;
        var list = await Downloader(r =>
        {
            seen = r;
            return Respond(ValidList);
        }).DownloadAsync(new Uri("https://example.com/a.wgetui"), Ct);

        Assert.Single(list.Packages);
        Assert.Equal(HttpMethod.Get, seen?.Method);
        Assert.Contains(seen!.Headers.UserAgent, p => p.Product?.Name == "wgetui");
    }

    [Fact]
    public async Task AnHttpErrorIsDownloadFailed()
    {
        var ex = await Assert.ThrowsAsync<PackageListException>(() => Downloader(_ => Respond("", HttpStatusCode.NotFound)).DownloadAsync(new Uri("https://example.com/a"), Ct));

        Assert.Equal(ErrorCodes.ListDownloadFailed, ex.ErrorCode);
    }

    [Fact]
    public async Task ANetworkErrorIsDownloadFailed()
    {
        var ex = await Assert.ThrowsAsync<PackageListException>(() => Downloader(_ => throw new HttpRequestException("offline")).DownloadAsync(new Uri("https://example.com/a"), Ct));

        Assert.Equal(ErrorCodes.ListDownloadFailed, ex.ErrorCode);
    }

    [Fact]
    public async Task ABodyPastTheCapIsTooLarge()
    {
        // No Content-Length: the stream itself is cut at the cap.
        var ex = await Assert.ThrowsAsync<PackageListException>(() => Downloader(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new EndlessStream()),
        }).DownloadAsync(new Uri("https://example.com/a"), Ct));

        Assert.Equal(ErrorCodes.ListTooLarge, ex.ErrorCode);
    }

    [Fact]
    public async Task TheUrlPopupRejectsBadAddressesThenOpensTheImport()
    {
        var shown = _launcher.ImportFromUrlAsync();
        var url = Assert.IsType<ImportUrlViewModel>(_overlay.Current);
        Assert.False(url.LoadListCommand.CanExecute(null));

        url.Address = "ftp://example.com/a";
        await url.LoadListCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Instance.Error(ErrorCodes.ListUrlInvalid), url.ErrorText);
        Assert.Same(url, _overlay.Current);

        url.Address = "https://example.com/home.wgetui";
        Assert.False(url.HasError);
        await url.LoadListCommand.ExecuteAsync(null);

        // The URL popup closed and the import popup took its place.
        var import = await WaitForAsync<ImportViewModel>();
        Assert.StartsWith("example.com/home.wgetui · 1 package", import.Subtitle, StringComparison.Ordinal);
        import.CloseCommand.Execute(null);
        await shown;
        Assert.Empty(_queue.Operations);
    }

    private async Task<T> WaitForAsync<T>()
        where T : class
    {
        for (var i = 0; i < 200; i++)
        {
            if (_overlay.Current is T found)
            {
                return found;
            }

            await Task.Delay(10, Ct);
        }

        throw new TimeoutException($"{typeof(T).Name} was not shown.");
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)' ', offset, count);
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
