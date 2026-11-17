using System.Runtime.InteropServices;

namespace WgetUi.App.Platform.Windows;

/// <summary>FlashWindowEx on the main window: flashes the taskbar button until the window comes to the foreground.</summary>
public sealed partial class WindowsTaskbarFlasher : IWindowFlasher
{
    private const uint FlashAll = 0x3;
    private const uint FlashUntilForeground = 0xC;
    private nint _handle;

    /// <summary>Set by the shell once the main window has a native handle.</summary>
    public void Attach(nint handle) => _handle = handle;

    public void Flash()
    {
        if (_handle == 0)
        {
            return;
        }

        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = _handle,
            Flags = FlashAll | FlashUntilForeground,
            Count = uint.MaxValue,
            Timeout = 0,
        };
        _ = FlashWindowEx(ref info);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }
}
