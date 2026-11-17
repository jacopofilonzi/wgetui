using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WgetUi.App.Platform.Windows;

/// <summary>
/// Process list through <see cref="Process"/> plus a few Win32 calls for what .NET does not expose: the image
/// path (QueryFullProcessImageName), the command line (NtQueryInformationProcess, class 60, Windows 8.1+), the
/// parent id (class 0) and the token elevation. Everything is opened with PROCESS_QUERY_LIMITED_INFORMATION.
/// A non-elevated process sees every process name (SYSTEM ones included, e.g. consent.exe). Elevated processes
/// of the same user usually show path and command line but are flagged by their token; protected or other
/// users' processes come back with null fields.
/// </summary>
public sealed partial class WindowsProcessSource : IProcessSource
{
    private const uint QueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;
    private const int ProcessBasicInformation = 0;
    private const int ProcessCommandLineInformation = 60;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    public bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    public IReadOnlyList<ProcessSnapshot> ListSessionProcesses()
    {
        var session = Environment.ProcessId is var self ? SessionOf(self) : -1;
        var result = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                int sessionId;
                try
                {
                    sessionId = process.SessionId;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    continue; // Exited meanwhile.
                }

                if (sessionId != session)
                {
                    continue;
                }

                result.Add(Read(process.Id, sessionId));
            }
        }

        return result;
    }

    private static int SessionOf(int processId)
    {
        using var process = Process.GetProcessById(processId);
        return process.SessionId;
    }

    private static ProcessSnapshot Read(int id, int sessionId)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, (uint)id);
        if (handle == 0)
        {
            return new ProcessSnapshot(id, null, sessionId, null, null, null);
        }

        try
        {
            return new ProcessSnapshot(id, ParentId(handle), sessionId, ImagePath(handle), CommandLine(handle), Elevated(handle));
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    private static unsafe string? ImagePath(nint handle)
    {
        var buffer = new char[32768];
        var size = (uint)buffer.Length;
        fixed (char* name = buffer)
        {
            return QueryFullProcessImageNameW(handle, 0, name, ref size) ? new string(name, 0, (int)size) : null;
        }
    }

    // The result is a UNICODE_STRING (Length, MaximumLength, padding, Buffer) followed by the text it points to.
    private static unsafe string? CommandLine(nint handle)
    {
        var size = 4096;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = new byte[size];
            fixed (byte* data = buffer)
            {
                var status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, (nint)data, (uint)size, out var needed);
                if (status == StatusInfoLengthMismatch)
                {
                    size = (int)needed;
                    continue;
                }

                if (status != 0)
                {
                    return null;
                }

                var length = *(ushort*)data;
                var text = *(nint*)(data + nint.Size);
                return length == 0 || text == 0 ? null : new string((char*)text, 0, length / sizeof(char));
            }
        }

        return null;
    }

    // PROCESS_BASIC_INFORMATION: ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId,
    // InheritedFromUniqueProcessId: six pointer-sized fields; the parent id is the last one.
    private static unsafe int? ParentId(nint handle)
    {
        var info = stackalloc nint[6];
        var status = NtQueryInformationProcess(handle, ProcessBasicInformation, (nint)info, (uint)(6 * nint.Size), out _);
        return status == 0 ? (int)info[5] : null;
    }

    private static bool? Elevated(nint process)
    {
        if (!OpenProcessToken(process, TokenQuery, out var token))
        {
            return null;
        }

        try
        {
            return GetTokenInformation(token, TokenElevationClass, out var elevation, sizeof(int), out _) ? elevation != 0 : null;
        }
        finally
        {
            _ = CloseHandle(token);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(nint process, uint flags, char* name, ref uint size);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(nint process, int informationClass, nint information, uint length, out uint returnLength);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int informationClass, out int information, int length, out int returnLength);
}
