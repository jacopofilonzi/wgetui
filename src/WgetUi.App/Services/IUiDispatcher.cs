namespace WgetUi.App.Services;

/// <summary>Runs work on the UI thread; view models use it for events raised by background threads.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
