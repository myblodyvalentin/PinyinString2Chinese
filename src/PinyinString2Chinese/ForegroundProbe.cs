using System.Diagnostics;
using System.Text;

namespace PinyinString2Chinese;

internal readonly record struct ForegroundProbe(
    IntPtr FocusHwnd,
    IntPtr RootHwnd,
    uint ProcessId,
    uint ThreadId,
    string ClassName,
    string ProcessName)
{
    internal static ForegroundProbe Capture()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var threadId = NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>(),
        };

        var focus = foreground;
        if (threadId != 0 && NativeMethods.GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
            focus = info.hwndFocus;

        var root = NativeMethods.GetAncestor(focus, NativeMethods.GaRoot);
        if (root == IntPtr.Zero)
            root = foreground;

        return new ForegroundProbe(
            focus,
            root,
            processId,
            threadId,
            ReadClass(focus),
            ReadProcess(processId));
    }

    internal bool SameFocus(ForegroundProbe other) =>
        FocusHwnd == other.FocusHwnd && ProcessId == other.ProcessId;

    private static string ReadClass(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "";

        var builder = new StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, builder, builder.Capacity) > 0
            ? builder.ToString()
            : "";
    }

    private static string ReadProcess(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }
}
