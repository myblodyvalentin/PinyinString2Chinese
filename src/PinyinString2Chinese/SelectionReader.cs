using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace PinyinString2Chinese;

internal enum ClearMethod
{
    EditReplace,
    KeyDelete,
}

internal sealed class CapturedText
{
    public required string Text { get; init; }

    public required ForegroundProbe Probe { get; init; }

    public required ClearMethod Clear { get; init; }
}

internal enum SelectionFollow
{
    Done,
    Poll,
    Copy,
    Stop,
}

internal readonly record struct SelectionAttempt(CapturedText? Text, SelectionFollow Follow)
{
    internal static SelectionAttempt Found(CapturedText text) => new(text, SelectionFollow.Done);

    internal static SelectionAttempt Poll() => new(null, SelectionFollow.Poll);

    internal static SelectionAttempt CopyLater() => new(null, SelectionFollow.Copy);

    internal static SelectionAttempt Stop() => new(null, SelectionFollow.Stop);
}

internal sealed class SelectionStability
{
    private string? _previous;

    internal bool IsStable(string text)
    {
        var stable = _previous == text;
        _previous = text;
        return stable;
    }

    internal static string Prefer(string earlier, string later)
    {
        if (later.StartsWith(earlier, StringComparison.Ordinal) && later.Length >= earlier.Length)
            return later;

        if (earlier.StartsWith(later, StringComparison.Ordinal) && earlier.Length > later.Length)
            return earlier;

        return later;
    }
}

internal static class SelectionReader
{
    internal static string? ReadEditControl(IntPtr hwnd)
    {
        var name = new System.Text.StringBuilder(256);
        if (NativeMethods.GetClassName(hwnd, name, name.Capacity) <= 0)
            name.Clear();

        return ReadEditSelection(new ForegroundProbe(hwnd, hwnd, 0, 0, name.ToString(), ""));
    }

    internal static SelectionAttempt TryRead(bool allowClipboard, bool allowSelf = false)
    {
        var probe = ForegroundProbe.Capture();
        if (probe.FocusHwnd == IntPtr.Zero)
            return SelectionAttempt.Stop();

        if (!allowSelf && probe.ProcessId == Environment.ProcessId)
            return SelectionAttempt.Stop();

        if (!TargetGuard.IsSafeTextTarget(probe.ProcessName, probe.ClassName))
            return SelectionAttempt.Stop();

        if (IsPasswordEdit(probe))
            return SelectionAttempt.Stop();

        if (TargetGuard.IsEditClass(probe.ClassName))
        {
            if (IsReadOnlyEdit(probe.FocusHwnd))
                return SelectionAttempt.Stop();

            var edited = ReadEditSelection(probe);
            if (edited != null)
            {
                return SelectionAttempt.Found(new CapturedText
                {
                    Text = edited,
                    Probe = probe,
                    Clear = ClearMethod.EditReplace,
                });
            }

            if (!allowClipboard)
                return SelectionAttempt.Poll();
        }

        var uia = ReadUia(probe.FocusHwnd);
        switch (uia.Status)
        {
            case UiaStatus.Secret:
            case UiaStatus.Rejected:
                return SelectionAttempt.Stop();
            case UiaStatus.Text:
                return SelectionAttempt.Found(new CapturedText
                {
                    Text = uia.Text,
                    Probe = probe,
                    Clear = ClearMethod.KeyDelete,
                });
            case UiaStatus.NotReady:
                if (!allowClipboard)
                    return SelectionAttempt.Poll();
                break;
            default:
                if (!allowClipboard || IsShell(probe.ProcessName))
                    return IsShell(probe.ProcessName) ? SelectionAttempt.Stop() : SelectionAttempt.CopyLater();
                break;
        }

        if (!allowClipboard || IsShell(probe.ProcessName))
            return SelectionAttempt.Stop();

        var copied = ReadClipboard(probe.FocusHwnd);
        if (string.IsNullOrWhiteSpace(copied))
            return SelectionAttempt.Stop();

        return SelectionAttempt.Found(new CapturedText
        {
            Text = copied,
            Probe = probe,
            Clear = ClearMethod.KeyDelete,
        });
    }

    private static string? ReadEditSelection(ForegroundProbe probe)
    {
        // 记事本的 RichEdit 把换行存成一个字符，WM_GETTEXT 却展开成两个。
        // 用 EM_GETSEL 的下标去切 WM_GETTEXT，会切到别的字，或因为越界而读不到。
        if (IsRichEdit(probe.ClassName))
            return ReadRichEditSelection(probe.FocusHwnd);

        var start = 0;
        var end = 0;
        NativeMethods.SendMessageSel(probe.FocusHwnd, NativeMethods.EmGetSel, ref start, ref end);
        if (end <= start)
            return null;

        var length = end - start;
        if (length > PinyinParser.MaxLetters + 8)
            return null;

        var total = NativeMethods.SendMessage(
            probe.FocusHwnd,
            NativeMethods.WmGetTextLength,
            IntPtr.Zero,
            IntPtr.Zero).ToInt32();
        if (total <= 0 || start > total)
            return null;

        var builder = new System.Text.StringBuilder(total + 1);
        NativeMethods.SendMessageText(
            probe.FocusHwnd,
            NativeMethods.WmGetText,
            (IntPtr)(builder.Capacity),
            builder);

        var content = builder.ToString();
        if (end > content.Length)
            return null;

        return NormalizeSelection(content[start..end]);
    }

    private static bool IsRichEdit(string className) =>
        className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase);

    private static string? ReadRichEditSelection(IntPtr hwnd)
    {
        var ranged = ReadRichEditRange(hwnd);
        if (ranged != null)
            return ranged;

        var start = 0;
        var end = 0;
        NativeMethods.SendMessageSel(hwnd, NativeMethods.EmGetSel, ref start, ref end);
        var plain = ReadPlainText(hwnd);
        return plain == null ? null : SliceRichEdit(plain, start, end);
    }

    private static string? ReadRichEditRange(IntPtr hwnd)
    {
        var range = new NativeMethods.CHARRANGE();
        NativeMethods.SendMessageCharRange(hwnd, NativeMethods.EmExGetSel, IntPtr.Zero, ref range);
        if (range.Max <= range.Min)
            return null;

        var length = range.Max - range.Min;
        if (length > PinyinParser.MaxLetters + 8)
            return null;

        var bytes = (length + 1) * 2;
        var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            var zeros = new byte[bytes];
            Marshal.Copy(zeros, 0, buffer, bytes);
            var textRange = new NativeMethods.TEXTRANGEW
            {
                Range = range,
                Text = buffer,
            };
            var copied = NativeMethods.SendMessageTextRange(
                hwnd,
                NativeMethods.EmGetTextRange,
                IntPtr.Zero,
                ref textRange).ToInt32();
            if (copied <= 0)
                return null;

            var text = Marshal.PtrToStringUni(buffer, Math.Min(copied, length)) ?? "";
            return NormalizeSelection(text);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? ReadPlainText(IntPtr hwnd)
    {
        var total = NativeMethods.SendMessage(
            hwnd,
            NativeMethods.WmGetTextLength,
            IntPtr.Zero,
            IntPtr.Zero).ToInt32();
        if (total <= 0)
            return null;

        var builder = new System.Text.StringBuilder(total + 1);
        NativeMethods.SendMessageText(hwnd, NativeMethods.WmGetText, (IntPtr)builder.Capacity, builder);
        return builder.ToString();
    }

    internal static string? SliceRichEdit(string plain, int richStart, int richEnd)
    {
        if (richEnd <= richStart || richStart < 0)
            return null;

        var start = RichIndexToPlain(plain, richStart);
        var end = RichIndexToPlain(plain, richEnd);
        if (start < 0 || end < start || end > plain.Length)
            return null;

        return NormalizeSelection(plain[start..end]);
    }

    internal static int RichIndexToPlain(string plain, int richIndex)
    {
        var rich = 0;
        for (var i = 0; i < plain.Length; i++)
        {
            if (plain[i] == '\n' && i > 0 && plain[i - 1] == '\r')
                continue;

            if (rich == richIndex)
                return i;

            rich++;
        }

        return rich == richIndex ? plain.Length : -1;
    }

    private static string? NormalizeSelection(string text)
    {
        var trimmed = text.TrimEnd('\r', '\n', '\0');
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static UiaRead ReadUia(IntPtr hwnd)
    {
        try
        {
            var fromFocus = ReadElement(AutomationElement.FocusedElement);
            if (fromFocus.Status != UiaStatus.Unsupported)
                return fromFocus;

            return ReadElement(AutomationElement.FromHandle(hwnd));
        }
        catch (Exception exception)
        {
            AppLog.Error(exception);
            return UiaRead.Unsupported();
        }
    }

    private static UiaRead ReadElement(AutomationElement? element)
    {
        if (element == null)
            return UiaRead.Unsupported();

        try
        {
            if (element.Current.IsPassword)
                return UiaRead.Secret();

            if (IsBlockingControl(element.Current.ControlType))
                return UiaRead.Rejected();

            if (IsReadOnly(element))
                return UiaRead.Rejected();

            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) || pattern is not TextPattern textPattern)
                return UiaRead.Unsupported();

            var ranges = textPattern.GetSelection();
            if (ranges == null || ranges.Length == 0)
                return UiaRead.NotReady();

            if (!SelectionIsVisible(ranges[0].GetBoundingRectangles()))
                return UiaRead.NotReady();

            var text = ranges[0].GetText(PinyinParser.MaxLetters + 1) ?? "";
            if (text.Length == 0)
                return UiaRead.NotReady();

            if (text.Length > PinyinParser.MaxLetters)
                return UiaRead.Rejected();

            return UiaRead.WithText(text);
        }
        catch (Exception exception)
        {
            AppLog.Error(exception);
            return UiaRead.Unsupported();
        }
    }

    private static bool SelectionIsVisible(System.Windows.Rect[]? rectangles)
    {
        if (rectangles == null || rectangles.Length == 0)
            return true;

        return rectangles.Any(rectangle => rectangle.Width > 1 && rectangle.Height > 1);
    }

    private static bool IsReadOnly(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) &&
                value is ValuePattern valuePattern &&
                valuePattern.Current.IsReadOnly)
            {
                return true;
            }
        }
        catch
        {
            // 有些控件不提供 ValuePattern。
        }

        return false;
    }

    private static bool IsBlockingControl(ControlType controlType) =>
        controlType == ControlType.Button
        || controlType == ControlType.ListItem
        || controlType == ControlType.TreeItem
        || controlType == ControlType.DataItem
        || controlType == ControlType.Header
        || controlType == ControlType.HeaderItem
        || controlType == ControlType.MenuItem
        || controlType == ControlType.Menu
        || controlType == ControlType.MenuBar
        || controlType == ControlType.Tab
        || controlType == ControlType.TabItem
        || controlType == ControlType.ToolBar
        || controlType == ControlType.TitleBar
        || controlType == ControlType.Image
        || controlType == ControlType.Hyperlink
        || controlType == ControlType.SplitButton
        || controlType == ControlType.CheckBox
        || controlType == ControlType.RadioButton
        || controlType == ControlType.Slider
        || controlType == ControlType.Spinner
        || controlType == ControlType.ScrollBar
        || controlType == ControlType.Thumb
        || controlType == ControlType.Separator
        || controlType == ControlType.StatusBar
        || controlType == ControlType.ProgressBar
        || controlType == ControlType.List
        || controlType == ControlType.Tree
        || controlType == ControlType.Table
        || controlType == ControlType.DataGrid;

    private static string? ReadClipboard(IntPtr focusHwnd)
    {
        if (!TryBackupClipboard(out var backup))
            return null;

        try
        {
            var before = NativeMethods.GetClipboardSequenceNumber();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (NativeMethods.GetClipboardSequenceNumber() == before)
                {
                    NativeMethods.SendMessageTimeout(
                        focusHwnd,
                        NativeMethods.WmCopy,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        NativeMethods.SmtoAbortIfHung,
                        180,
                        out _);

                    if (NativeMethods.GetClipboardSequenceNumber() == before)
                        InputSender.Chord(NativeMethods.VkControl, 'C');
                }

                var started = Environment.TickCount64;
                while (NativeMethods.GetClipboardSequenceNumber() == before &&
                       Environment.TickCount64 - started < 200)
                {
                    Thread.Sleep(20);
                }

                if (NativeMethods.GetClipboardSequenceNumber() == before)
                {
                    Thread.Sleep(40);
                    continue;
                }

                var text = TryGetClipboardText();
                if (text == "")
                    return null;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    Thread.Sleep(30);
                    return text;
                }

                Thread.Sleep(40);
            }

            return null;
        }
        catch (Exception exception)
        {
            AppLog.Error(exception);
            return null;
        }
        finally
        {
            RestoreClipboard(backup);
        }
    }

    private static string? TryGetClipboardText()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                    return "";

                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (Exception exception)
            {
                if (attempt == 3)
                    AppLog.Error(exception);
                else
                    Thread.Sleep(30);
            }
        }

        return null;
    }

    private static bool TryBackupClipboard(out DataObject? backup)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                backup = CopyClipboard();
                return true;
            }
            catch (Exception exception)
            {
                last = exception;
                Thread.Sleep(40);
            }
        }

        if (last != null)
            AppLog.Error(last);

        backup = null;
        return false;
    }

    private static DataObject CopyClipboard()
    {
        var backup = new DataObject();
        var data = Clipboard.GetDataObject();
        if (data == null)
            return backup;

        foreach (var format in data.GetFormats() ?? [])
        {
            try
            {
                if (!data.GetDataPresent(format))
                    continue;

                var value = data.GetData(format);
                if (value == null)
                    continue;

                backup.SetData(format, value);
            }
            catch
            {
                // 个别格式读不出来时，保留其余格式。
            }
        }

        return backup;
    }

    private static void RestoreClipboard(DataObject? backup)
    {
        if (backup == null)
            return;

        Exception? last = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                var formats = backup.GetFormats();
                if (formats == null || formats.Length == 0)
                    Clipboard.Clear();
                else
                    Clipboard.SetDataObject(backup, true, 4, 40);

                return;
            }
            catch (Exception exception)
            {
                last = exception;
                Thread.Sleep(60);
            }
        }

        if (last != null)
            AppLog.Error(last);
    }

    private static bool IsPasswordEdit(ForegroundProbe probe)
    {
        if (!TargetGuard.IsEditClass(probe.ClassName))
            return false;

        var style = NativeMethods.GetWindowLongPtr(probe.FocusHwnd, NativeMethods.GwlStyle).ToInt64();
        return (style & NativeMethods.EsPassword) != 0;
    }

    private static bool IsReadOnlyEdit(IntPtr hwnd)
    {
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
        return (style & NativeMethods.EsReadOnly) != 0;
    }

    private static bool IsShell(string processName) =>
        processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase);

    private enum UiaStatus
    {
        Unsupported,
        NotReady,
        Rejected,
        Secret,
        Text,
    }

    private readonly record struct UiaRead(UiaStatus Status, string Text)
    {
        public static UiaRead Unsupported() => new(UiaStatus.Unsupported, "");

        public static UiaRead NotReady() => new(UiaStatus.NotReady, "");

        public static UiaRead Rejected() => new(UiaStatus.Rejected, "");

        public static UiaRead Secret() => new(UiaStatus.Secret, "");

        public static UiaRead WithText(string text) => new(UiaStatus.Text, text);
    }
}
