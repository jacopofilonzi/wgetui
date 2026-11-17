namespace WgetUi.App.Platform;

/// <summary>
/// The per-user "run at logon" list (HKCU\Software\Microsoft\Windows\CurrentVersion\Run on Windows).
/// Behind an interface so tests never touch the real registry.
/// </summary>
public interface IRunKey
{
    string? Read(string name);

    void Write(string name, string command);

    void Delete(string name);
}
