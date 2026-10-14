namespace PinyinString2Chinese;

internal sealed record ImeChoice(IntPtr Hkl, string LayoutName, string CultureName)
{
    public long HklValue => Hkl.ToInt64();

    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(LayoutName))
            return CultureName;

        return string.IsNullOrWhiteSpace(CultureName)
            ? LayoutName
            : $"{LayoutName}  ({CultureName})";
    }
}

internal static class ImeController
{
    internal static IReadOnlyList<ImeChoice> List()
    {
        var list = new List<ImeChoice>();
        foreach (InputLanguage language in InputLanguage.InstalledInputLanguages)
        {
            try
            {
                if (!IsChinese(language) || !NativeMethods.ImmIsIME(language.Handle))
                    continue;

                list.Add(new ImeChoice(
                    language.Handle,
                    language.LayoutName ?? "",
                    language.Culture?.Name ?? ""));
            }
            catch (Exception exception)
            {
                AppLog.Error(exception);
            }
        }

        return list;
    }

    internal static ImeChoice? Resolve(AppSettings settings)
    {
        var list = List();
        if (list.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(settings.ImeLayoutName))
        {
            var byName = list.FirstOrDefault(item =>
                item.LayoutName.Equals(settings.ImeLayoutName, StringComparison.Ordinal));
            if (byName != null)
                return byName;
        }

        if (settings.ImeHkl != 0)
        {
            var byHandle = list.FirstOrDefault(item => item.HklValue == settings.ImeHkl);
            if (byHandle != null)
                return byHandle;
        }

        return list.FirstOrDefault(item =>
                   item.LayoutName.Contains("拼音", StringComparison.Ordinal)
                   || item.LayoutName.Contains("Pinyin", StringComparison.OrdinalIgnoreCase))
               ?? list[0];
    }

    internal static bool TryActivate(IntPtr focusHwnd, ImeChoice? choice)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(focusHwnd, out _);
        if (threadId == 0)
            return false;

        if (choice != null)
            RequestLayout(focusHwnd, choice.Hkl, threadId);

        var layout = NativeMethods.GetKeyboardLayout(threadId);
        if (!IsChineseHkl(layout) || !NativeMethods.ImmIsIME(layout))
        {
            AppLog.Info($"前台不是中文输入法：0x{layout.ToInt64():X}");
            return false;
        }

        if (choice != null && !SameHkl(layout, choice.Hkl))
            AppLog.Info($"已是中文输入法，但没有精确切到“{choice.LayoutName}”，继续使用当前中文输入法。");

        ForceChineseMode(focusHwnd);
        return true;
    }

    private static void RequestLayout(IntPtr focusHwnd, IntPtr hkl, uint threadId)
    {
        var root = NativeMethods.GetAncestor(focusHwnd, NativeMethods.GaRoot);
        NativeMethods.PostMessage(focusHwnd, NativeMethods.WmInputLangChangeRequest, IntPtr.Zero, hkl);
        if (root != IntPtr.Zero && root != focusHwnd)
            NativeMethods.PostMessage(root, NativeMethods.WmInputLangChangeRequest, IntPtr.Zero, hkl);

        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < 320)
        {
            if (SameHkl(NativeMethods.GetKeyboardLayout(threadId), hkl))
                return;

            Thread.Sleep(20);
        }
    }

    private static void ForceChineseMode(IntPtr focusHwnd)
    {
        var ime = NativeMethods.ImmGetDefaultIMEWnd(focusHwnd);
        if (ime == IntPtr.Zero)
        {
            var root = NativeMethods.GetAncestor(focusHwnd, NativeMethods.GaRoot);
            ime = NativeMethods.ImmGetDefaultIMEWnd(root);
        }

        if (ime == IntPtr.Zero)
        {
            AppLog.Info("找不到输入法窗口，仍尝试直接输入拼音。");
            return;
        }

        NativeMethods.SendMessageTimeout(
            ime,
            NativeMethods.WmImeControl,
            (IntPtr)NativeMethods.ImcSetOpenStatus,
            (IntPtr)1,
            NativeMethods.SmtoAbortIfHung,
            200,
            out _);

        NativeMethods.SendMessageTimeout(
            ime,
            NativeMethods.WmImeControl,
            (IntPtr)NativeMethods.ImcSetConversionMode,
            (IntPtr)NativeMethods.ChineseConversionMode,
            NativeMethods.SmtoAbortIfHung,
            200,
            out var mode);

        var opened = false;
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < 180)
        {
            NativeMethods.SendMessageTimeout(
                ime,
                NativeMethods.WmImeControl,
                (IntPtr)NativeMethods.ImcGetOpenStatus,
                IntPtr.Zero,
                NativeMethods.SmtoAbortIfHung,
                80,
                out var open);
            if (open != IntPtr.Zero)
            {
                opened = true;
                break;
            }

            Thread.Sleep(20);
        }

        var elapsed = Environment.TickCount64 - started;
        if (elapsed < 100)
            Thread.Sleep((int)(100 - elapsed));

        AppLog.Info(opened
            ? $"中文输入法已打开，切换返回值 0x{mode.ToInt64():X}"
            : $"未确认输入法已打开，切换返回值 0x{mode.ToInt64():X}");
    }

    private static bool IsChinese(InputLanguage language)
    {
        var name = language.Culture?.Name ?? "";
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return true;

        return IsChineseHkl(language.Handle);
    }

    private static bool IsChineseHkl(IntPtr hkl)
    {
        var langId = (ushort)(hkl.ToInt64() & 0xFFFF);
        return (langId & 0x3FF) == 0x04;
    }

    private static bool SameHkl(IntPtr left, IntPtr right) => left == right;
}
