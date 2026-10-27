using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nacre.Services;

internal static class NativeWindows
{
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Time; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    private static uint _lastObservedInput;

    public static long? ObserveInputInThisProcess()
    {
        var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (!GetLastInputInfo(ref input) || input.Time == _lastObservedInput) return null;
        _lastObservedInput = input.Time;
        GetWindowThreadProcessId(GetForegroundWindow(), out var process);
        if (process != Environment.ProcessId) return null;
        return Environment.TickCount64 - unchecked((uint)Environment.TickCount - input.Time);
    }

    public static void CloseOwnedNativeDialogs(Window owner)
    {
        var main = new WindowInteropHelper(owner).Handle;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (window != main && GetWindow(window, 4) == main)
                PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE, only this thread's owned dialogs
            return true;
        }, IntPtr.Zero);
    }
}
