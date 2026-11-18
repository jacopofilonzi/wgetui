namespace WgetUi.App.Services.AppUpdates;

/// <summary>
/// Command line of a downloaded wgetui setup (installer/wgetui.iss). <c>/UPDATE</c> makes it wait for wgetui to
/// quit and reopen it with <c>--updated</c>; <c>/TRAY</c> (honoured only with <c>/UPDATE</c>) reopens it hidden in
/// the tray (<c>--tray --updated</c>), for an update installed at a <c>--tray</c> start. <c>/LOG</c> writes
/// <see cref="LogName"/> in the updates folder.
/// </summary>
public static class SetupCommand
{
    public const string BaseArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE";

    public const string TrayParameter = "/TRAY";

    /// <summary>Setup log in the updates folder (kept across starts: only setups are deleted).</summary>
    public const string LogName = "setup.log";

    public static string Arguments(string logDirectory, bool tray) =>
        $"{BaseArguments}{(tray ? " " + TrayParameter : string.Empty)} /LOG=\"{Path.Combine(logDirectory, LogName)}\"";
}
