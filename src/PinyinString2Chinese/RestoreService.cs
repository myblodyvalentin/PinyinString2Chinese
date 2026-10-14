using System.Collections.Concurrent;

namespace PinyinString2Chinese;

internal readonly record struct RestoreJob(TriggerKind Kind);

internal sealed class RestoreWorker
{
    private readonly Func<AppSettings> _settings;
    private readonly Action<string, bool> _report;
    private readonly BlockingCollection<RestoreJob> _jobs = new();
    private readonly Thread _thread;

    internal RestoreWorker(Func<AppSettings> settings, Action<string, bool> report)
    {
        _settings = settings;
        _report = report;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "PinyinRestore",
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    internal void Start() => _thread.Start();

    internal void Enqueue(RestoreJob job) => _jobs.Add(job);

    internal void Stop() => _jobs.CompleteAdding();

    private void Loop()
    {
        try
        {
            Application.OleRequired();
        }
        catch (Exception exception)
        {
            AppLog.Error(exception);
        }

        foreach (var job in _jobs.GetConsumingEnumerable())
        {
            try
            {
                Execute(job);
            }
            catch (Exception exception)
            {
                AppLog.Error(exception);
                Report("还原时出现错误。", balloon: true);
            }
        }
    }

    private void Execute(RestoreJob job)
    {
        var settings = _settings();
        if (job.Kind != TriggerKind.Manual && !settings.Enabled)
            return;

        if (!InputSender.WaitUntilModifiersReleased(1500))
        {
            if (job.Kind == TriggerKind.Manual)
                Report("请先松开 Ctrl、Alt 和 Shift。", balloon: true);
            return;
        }

        var captured = ReadSettledSelection(job.Kind);
        if (captured == null)
        {
            var probe = ForegroundProbe.Capture();
            AppLog.Info($"没有读到选区：{probe.ProcessName}/{probe.ClassName}");
            if (job.Kind == TriggerKind.Manual)
                Report("没有读到选中的文字。请先框选英文字符串。", balloon: true);
            return;
        }

        var parsed = PinyinParser.Parse(captured.Text);
        if (parsed == null || !PinyinParser.Allows(parsed, job.Kind))
        {
            var shown = captured.Text.Length > 24 ? captured.Text[..24] + "…" : captured.Text;
            AppLog.Info($"选区不能还原：{shown}");
            if (job.Kind == TriggerKind.Manual)
                Report("选中的不是纯英文字符串。", balloon: true);
            return;
        }

        if (!ForegroundProbe.Capture().SameFocus(captured.Probe))
            return;

        AppLog.Info($"准备还原 {parsed.Keystrokes}，窗口 {captured.Probe.ProcessName}/{captured.Probe.ClassName}，清除 {captured.Clear}");
        var cleared = false;
        try
        {
            InputSender.ReleaseChordKeys();
            if (!Clear(captured))
            {
                Report("没能清除原来的英文字符串。", balloon: true);
                return;
            }

            cleared = true;
            Thread.Sleep(30);

            if (!ForegroundProbe.Capture().SameFocus(captured.Probe))
            {
                Report("焦点已经离开原窗口。请回到那里按 Ctrl+Z 恢复原文。", balloon: true);
                return;
            }

            var ime = ImeController.Resolve(settings);
            if (!ImeController.TryActivate(captured.Probe.FocusHwnd, ime))
            {
                InputSender.TypeUnicode(captured.Text);
                Report("没能切换到中文输入法，已把原文写回去。请在设置里指定一个中文输入法。", balloon: true);
                return;
            }

            if (ForegroundProbe.Capture().ProcessId != captured.Probe.ProcessId)
            {
                Report("切换输入法时窗口变了。请回到原窗口按 Ctrl+Z。", balloon: true);
                return;
            }

            if (!InputSender.TypePinyin(parsed.Keystrokes))
            {
                Report("英文字符串没有完整送入输入法。可按 Esc 后，再按 Ctrl+Z 恢复原文。", balloon: true);
                return;
            }

            var shown = parsed.Keystrokes.Length > 24
                ? parsed.Keystrokes[..24] + "…"
                : parsed.Keystrokes;
            var after = ForegroundProbe.Capture();
            AppLog.Info($"已送入 {shown}，焦点 {after.ProcessName}/{after.ClassName}");
            Report($"已送入输入法：{shown}", balloon: false);
        }
        catch (Exception exception)
        {
            AppLog.Error(exception);
            if (cleared && ForegroundProbe.Capture().SameFocus(captured.Probe))
            {
                InputSender.TypeUnicode(captured.Text);
                Report("还原失败，已尝试写回原文。", balloon: true);
            }
            else if (cleared)
            {
                Report("还原失败。请回到原窗口按 Ctrl+Z。", balloon: true);
            }
        }
    }

    private static CapturedText? ReadSettledSelection(TriggerKind kind)
    {
        // 松开鼠标时，记事本和微信往往还没把最终选区提交出来。
        // 第一次读到的可能只是半截，必须看到连续两次相同内容才采用。
        Thread.Sleep(kind == TriggerKind.Manual ? 20 : 40);
        var started = Environment.TickCount64;
        var passes = 0;
        var stability = new SelectionStability();
        CapturedText? pending = null;
        while (true)
        {
            passes++;
            var attempt = SelectionReader.TryRead(allowClipboard: false);
            if (attempt.Follow == SelectionFollow.Stop)
                return null;

            if (attempt.Text != null)
            {
                pending = attempt.Text;
                if (stability.IsStable(attempt.Text.Text))
                {
                    if (passes > 2)
                        AppLog.Info($"选区在第 {passes} 次读取时就绪");
                    return attempt.Text;
                }
            }

            var elapsed = Environment.TickCount64 - started;
            var copyReady = attempt.Follow == SelectionFollow.Copy && elapsed >= (kind == TriggerKind.Manual ? 0 : 260);
            if (copyReady || elapsed >= 560)
            {
                if (pending != null)
                    return ConfirmPending(pending);

                if (attempt.Follow != SelectionFollow.Copy && elapsed < 560)
                {
                    Thread.Sleep(50);
                    continue;
                }

                var copied = SelectionReader.TryRead(allowClipboard: true);
                if (copied.Text != null && passes > 1)
                    AppLog.Info("选区稍晚才就绪");
                return copied.Text;
            }

            Thread.Sleep(50);
        }
    }

    private static CapturedText ConfirmPending(CapturedText pending)
    {
        Thread.Sleep(70);
        var again = SelectionReader.TryRead(allowClipboard: false);
        if (again.Follow == SelectionFollow.Stop || again.Text == null)
            return pending;

        var chosen = SelectionStability.Prefer(pending.Text, again.Text.Text);
        if (chosen == pending.Text)
            return pending;

        return new CapturedText
        {
            Text = chosen,
            Probe = again.Text.Probe,
            Clear = again.Text.Clear,
        };
    }

    private static bool Clear(CapturedText captured)
    {
        if (captured.Clear == ClearMethod.EditReplace)
            return InputSender.ClearEdit(captured.Probe.FocusHwnd);

        return InputSender.Tap(NativeMethods.VkDelete, extended: true);
    }

    private void Report(string message, bool balloon) => _report(message, balloon);
}
