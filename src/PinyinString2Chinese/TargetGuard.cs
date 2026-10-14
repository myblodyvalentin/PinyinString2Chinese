namespace PinyinString2Chinese;

internal static class TargetGuard
{
    internal static bool IsSafeTextTarget(string processName, string className)
    {
        if (IsBlockedClass(className))
            return false;

        if (IsShellProcess(processName) && !IsEditClass(className))
            return false;

        return true;
    }

    internal static bool IsEditClass(string className) =>
        className.Contains("Edit", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedClass(string className) =>
        className is "SysListView32"
            or "SysTreeView32"
            or "SHELLDLL_DefView"
            or "Progman"
            or "WorkerW"
            or "CabinetWClass";

    private static bool IsShellProcess(string processName) =>
        processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase);
}
