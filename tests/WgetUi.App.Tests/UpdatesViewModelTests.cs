using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

public sealed class UpdatesViewModelTests : IAsyncDisposable
{
    private readonly ScriptedManager _manager = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly OperationQueue _queue;
    private readonly UpdatesViewModel _vm;
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "wgetui-tests", Guid.NewGuid().ToString("N"));

    public UpdatesViewModelTests()
    {
        _manager.Upgrades.AddRange([ScriptedManager.Package("a"), ScriptedManager.Package("b"), ScriptedManager.Package("c")]);
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(registry, NullLogger<OperationQueue>.Instance);
        var requester = new OperationRequester(registry, _queue, new NeverConfirm(), NullLogger<OperationRequester>.Instance);
        var settings = new SettingsService(new SettingsStore(AppPaths.FromArgs(["--data-dir", _dataDir]), NullLogger<SettingsStore>.Instance));
        var actions = new PackageActions(requester, settings, NullLogger<PackageActions>.Instance);
        _vm = new UpdatesViewModel(registry, actions, _queue, _dispatcher, NullLogger<UpdatesViewModel>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _queue.DisposeAsync();
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PackageRowViewModel Row(string id) => _vm.Upgrades.Single(r => r.Id == id);

    private async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            _dispatcher.Drain();
            if (condition())
            {
                return;
            }

            await Task.Delay(25, Ct);
        }

        Assert.Fail("Condition not reached in time.");
    }

    [Fact]
    public async Task SuccessfulUpgradesRemoveTheirRowsAndLowerTheCount()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(3, _vm.Count);

        // Second one queued while the first is running, like two quick clicks.
        await Row("a").ActCommand.ExecuteAsync(null);
        await Row("b").ActCommand.ExecuteAsync(null);
        _manager.Complete("a", OperationResult.Success());
        _manager.Complete("b", OperationResult.Success());

        await UntilAsync(() => _vm.Count == 1);
        Assert.Equal("c", Assert.Single(_vm.Upgrades).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledUpgradeKeepsTheRowAndEnablesItAgain(bool cancel)
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        await Row("a").ActCommand.ExecuteAsync(null);
        Assert.True(Row("a").IsQueued);
        Assert.False(Row("a").ActCommand.CanExecute(null));

        _manager.Complete("a", cancel ? OperationResult.Cancelled() : OperationResult.Failure("boom", "failed"));

        await UntilAsync(() => !Row("a").IsQueued);
        Assert.Equal(3, _vm.Count);
        Assert.True(Row("a").ActCommand.CanExecute(null));
    }

    private sealed class NeverConfirm : IAgreementsConfirmer
    {
        public Task<bool> ConfirmAsync(IReadOnlyList<PackageAgreements> agreements, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
