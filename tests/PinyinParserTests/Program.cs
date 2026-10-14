using System.Diagnostics;
using System.Runtime.InteropServices;
using PinyinString2Chinese;

namespace PinyinParserTests;

internal static class Program
{
    private static int _failed;

    [STAThread]
    private static void Main(string[] args)
    {
        TestParser();
        TestGuard();
        TestInputStruct();
        TestEditSelection();
        TestSettingsForm();
        TestFloatBall();
        if (_failed == 0 && args.Contains("--live"))
            TestLiveRestore();

        if (_failed > 0)
        {
            Console.Error.WriteLine($"{_failed} failed");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine("all passed");
    }

    private static void TestParser()
    {
        Equal("nihao", "nihao");
        Equal("NIHAO", "nihao");
        Equal("ni hao", "nihao");
        Equal("btw", "btw");
        Equal("YYDS", "yyds");
        Equal("u1s1", "u1s1");
        Equal("ni3hao3", "ni3hao3");
        Equal("nǐhǎo", "nihao");
        Equal("hello", "hello");
        Equal("xswl", "xswl");
        Equal("  btw  ", "btw");
        Equal("a", "a");
        Reject("nihao!");
        Reject("你好");
        Reject("btw!");
        Reject("nihao\nshi");
        Reject("");
        Reject("   ");
        Reject(new string('a', 121));

        var explained = PinyinParser.Explain("btw");
        Check(explained.Contains("btw", StringComparison.Ordinal), "explain btw");
        Check(PinyinParser.Explain("hello").Contains("hello", StringComparison.Ordinal), "explain hello");
        Check(PinyinParser.Explain("a").Contains("可以还原", StringComparison.Ordinal), "explain short");
        Check(PinyinParser.Allows(PinyinParser.Parse("a")!, TriggerKind.Automatic), "auto allows a");
        Check(PinyinParser.Allows(PinyinParser.Parse("btw")!, TriggerKind.Automatic), "auto allows btw");
        Check(PinyinParser.Allows(PinyinParser.Parse("u1s1")!, TriggerKind.Manual), "manual allows digits");
    }

    private static void TestGuard()
    {
        Check(!TargetGuard.IsSafeTextTarget("explorer", "SysListView32"), "explorer list");
        Check(!TargetGuard.IsSafeTextTarget("explorer", "CabinetWClass"), "explorer cabinet");
        Check(TargetGuard.IsSafeTextTarget("explorer", "Edit"), "explorer edit");
        Check(!TargetGuard.IsSafeTextTarget("SearchHost", "Windows.UI.Core.CoreWindow"), "search");
        Check(TargetGuard.IsSafeTextTarget("Notepad", "Edit"), "notepad edit");
        Check(TargetGuard.IsEditClass("WindowsForms10.EDIT.app.0.1"), "winforms edit");
        Check(!TargetGuard.IsEditClass("Chrome_RenderWidgetHostHWND"), "chrome host");
        Check(TargetGuard.IsSafeTextTarget("WeChat", "Chrome_RenderWidgetHostHWND"), "wechat");
        Check(!TargetGuard.IsSafeTextTarget("app", "Progman"), "desktop");
    }

    private static void TestInputStruct()
    {
        var size = Marshal.SizeOf<NativeMethods.INPUT>();
        var expected = Environment.Is64BitProcess ? 40 : 28;
        Check(size == expected, $"INPUT size {size}, expected {expected}");
    }

    private static void TestEditSelection()
    {
        using var box = new TextBox { Text = "前缀nihao后缀" };
        _ = box.Handle;
        box.Select(2, 5);

        var selected = SelectionReader.ReadEditControl(box.Handle);
        Check(selected == "nihao", "selection was " + (selected ?? "<null>"));
        Check(InputSender.ClearEdit(box.Handle), "clear");
        Check(box.Text == "前缀后缀", "after clear: " + box.Text);

        using var rich = new RichTextBox { Multiline = true };
        var word = "birushuoxianzaizhezhongqingkuang";
        rich.Text = "你好" + Environment.NewLine + word + Environment.NewLine + "结尾";
        _ = rich.Handle;
        var at = rich.Text.IndexOf(word, StringComparison.Ordinal);
        rich.Select(at, word.Length);
        var richSelected = SelectionReader.ReadEditControl(rich.Handle);
        Check(richSelected == word, "rich selection was " + (richSelected ?? "<null>"));
        rich.Select(at, word.Length + 1);
        var withBreak = SelectionReader.ReadEditControl(rich.Handle);
        Check(withBreak == word, "rich trailing break was " + (withBreak ?? "<null>"));

        var stability = new SelectionStability();
        Check(!stability.IsStable("bi"), "first read is not stable");
        Check(!stability.IsStable("biru"), "growing read is not stable");
        Check(stability.IsStable("biru"), "same read is stable");
        Check(SelectionStability.Prefer("qingkua", "qingkuang") == "qingkuang", "prefer longer");
        Check(SelectionStability.Prefer("qingkuang", "huo") == "huo", "prefer latest when different");

        var plain = "你好\r\n" + word;
        Check(SelectionReader.RichIndexToPlain(plain, 3) == 4, "rich index skips extra newline");
        Check(SelectionReader.SliceRichEdit(plain, 3, 3 + word.Length) == word, "slice after newline");
        Check(SelectionReader.SliceRichEdit(word, 0, word.Length) == word, "slice without newline");
    }

    private static void TestSettingsForm()
    {
        using var form = new SettingsForm(new AppSettings(), "Ctrl+Alt+P", "Ctrl+Alt+R");
        Check(form.ClientSize.Height > 400, "settings height " + form.ClientSize.Height);
        AppSettings? saved = null;
        var placed = new AppSettings { Enabled = true, FloatBallX = 120, FloatBallY = 340 };
        using var placedForm = new SettingsForm(placed, "Ctrl+Alt+P", "Ctrl+Alt+R");
        placedForm.SettingsChanged += settings => saved = settings;
        var enabledBox = placedForm.Controls.OfType<CheckBox>().First(box => box.Text.Contains("启用", StringComparison.Ordinal));
        enabledBox.Checked = false;
        Check(saved is { Enabled: false, FloatBallX: 120, FloatBallY: 340 }, "ball position kept");
        var controls = form.Controls.Cast<Control>().ToList();
        for (var i = 0; i < controls.Count; i++)
        {
            for (var j = i + 1; j < controls.Count; j++)
            {
                var a = controls[i];
                var b = controls[j];
                var overlaps = a.Left < b.Right - 1 && a.Right > b.Left + 1 && a.Top < b.Bottom - 1 && a.Bottom > b.Top + 1;
                Check(!overlaps, $"overlap {a.Text} and {b.Text}");
            }
        }
    }

    private static void TestFloatBall()
    {
        Check(FloatBallMath.SizeForDpi(96) == 84, "dpi 96");
        Check(FloatBallMath.SizeForDpi(192) == 168, "dpi 192");
        Check(!FloatBallMath.IsDrag(5, 0), "small move");
        Check(FloatBallMath.IsDrag(6, 0), "drag x");
        Check(FloatBallMath.IsDrag(3, 3), "drag diagonal");
        Check(FloatBallMath.IsInside(42, 42, 84), "center hit");
        Check(!FloatBallMath.IsInside(0, 0, 84), "corner miss");
        var parked = FloatBallMath.Clamp(new Rectangle(0, 0, 200, 200), -10, 500, 84);
        Check(parked == new Point(0, 116), "clamp " + parked);
        var home = FloatBallMath.DefaultLocation(new Rectangle(0, 0, 1000, 800), 84);
        Check(home == new Point(892, 596), "default " + home);

        using var on = FloatBallForm.Draw(true, 84);
        using var off = FloatBallForm.Draw(false, 84);
        var sample = FloatBallMath.BallBounds(84);
        var sampleX = sample.Left + sample.Width / 2;
        var sampleY = sample.Bottom - 8;
        var running = on.GetPixel(sampleX, sampleY);
        var paused = off.GetPixel(sampleX, sampleY);
        var corner = on.GetPixel(0, 0);
        Check(running.A > 200 && running.G > running.R && running.G > 80, "green " + running);
        Check(paused.A > 200 && paused.B > paused.R && paused.R > 80, "gray " + paused);
        Check(corner.A < 20, "clear corner " + corner);
    }

    private static void TestLiveRestore()
    {
        var exe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "PinyinString2Chinese", "bin", "Debug", "net9.0-windows",
            "PinyinString2Chinese.exe"));
        Check(File.Exists(exe), "missing " + exe);
        if (!File.Exists(exe))
            return;

        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinyinString2Chinese");
        Directory.CreateDirectory(settingsDir);
        var logPath = Path.Combine(settingsDir, "app.log");
        if (File.Exists(logPath))
            File.Delete(logPath);
        var settingsPath = Path.Combine(settingsDir, "settings.json");
        if (!File.Exists(settingsPath))
        {
            File.WriteAllText(settingsPath, """
                {
                  "Enabled": true,
                  "RequireAlt": false,
                  "StartWithWindows": false
                }
                """);
        }

        foreach (var existing in Process.GetProcessesByName("PinyinString2Chinese"))
        {
            try
            {
                existing.Kill(entireProcessTree: true);
                existing.WaitForExit(2000);
            }
            catch
            {
                // 正在退出的进程可以忽略。
            }
        }

        Thread.Sleep(400);
        foreach (InputLanguage language in InputLanguage.InstalledInputLanguages)
        {
            Console.WriteLine(
                $"LANG {language.LayoutName} | {language.Culture.Name} | ime={NativeMethods.ImmIsIME(language.Handle)} | 0x{language.Handle.ToInt64():X}");
        }

        Process? app = null;
        Form? form = null;
        try
        {
            app = Process.Start(new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
            });
            var startupLog = "";
            var started = Environment.TickCount64 + 4000;
            while (Environment.TickCount64 < started)
            {
                startupLog = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
                if (startupLog.Contains("快捷键", StringComparison.Ordinal))
                    break;
                Thread.Sleep(50);
            }

            Check(app != null && !app.HasExited, "app exited early");
            Check(startupLog.Contains("快捷键", StringComparison.Ordinal), "app did not start: " + startupLog);
            var restoreKey = startupLog.Contains("还原=Ctrl+Alt+F9", StringComparison.Ordinal) ? 0x78 : 0x52;
            if (app == null || app.HasExited)
                return;

            var typed = new System.Text.StringBuilder();
            var box = new TextBox
            {
                Text = "nihao",
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericSansSerif, 24f),
            };
            box.KeyDown += (_, e) => typed.Append('[').Append(e.KeyCode).Append(']');
            box.KeyPress += (_, e) => typed.Append(e.KeyChar);
            form = new Form
            {
                Text = "拼音还原联调",
                StartPosition = FormStartPosition.CenterScreen,
                Size = new Size(520, 160),
                TopMost = true,
            };
            form.Controls.Add(box);
            form.Show();
            ForceForeground(form);
            box.Focus();
            box.SelectAll();
            Application.DoEvents();
            var foreground = GetForegroundWindow();
            Console.WriteLine("foreground=" + foreground + " form=" + form.Handle + " box=" + box.Handle);
            SendRestoreHotkey(restoreKey);

            var log = "";
            var until = Environment.TickCount64 + 7000;
            while (Environment.TickCount64 < until)
            {
                Application.DoEvents();
                log = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
                if (log.Contains("已送入", StringComparison.Ordinal) ||
                    log.Contains("没能切换", StringComparison.Ordinal) ||
                    log.Contains("还原失败", StringComparison.Ordinal) ||
                    log.Contains("没有读到选区", StringComparison.Ordinal))
                {
                    break;
                }

                Thread.Sleep(40);
            }

            if (log.Contains("已送入", StringComparison.Ordinal))
            {
                SendKey(0x20);
                Application.DoEvents();
                Thread.Sleep(200);
                Application.DoEvents();
            }

            Console.WriteLine("LIVE text=[" + box.Text + "] keys=" + typed);
            Console.WriteLine("lang=" + InputLanguage.CurrentInputLanguage.LayoutName);
            Console.WriteLine(log.Length > 1200 ? log[^1200..] : log);
            Check(log.Contains("已送入 nihao", StringComparison.Ordinal), "live restore did not finish");
            Check(box.Text.Any(ch => ch is >= '\u4E00' and <= '\u9FFF'), "expected Chinese, got [" + box.Text + "]");
        }
        finally
        {
            form?.Close();
            form?.Dispose();
            if (app != null && !app.HasExited)
            {
                try
                {
                    app.Kill(entireProcessTree: true);
                }
                catch
                {
                    // 进程已经退出。
                }
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SwitchToThisWindow")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);

    private static void ForceForeground(Form form)
    {
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
                       AttachThreadInput(currentThread, foregroundThread, true);
        form.TopMost = true;
        BringWindowToTop(form.Handle);
        SetForegroundWindow(form.Handle);
        SwitchToThisWindow(form.Handle, true);
        form.Activate();
        if (attached)
            AttachThreadInput(currentThread, foregroundThread, false);
    }

    private static void SendKey(int virtualKey)
    {
        var inputs = new[] { Key(virtualKey, false), Key(virtualKey, true) };
        NativeMethods.SendInput((uint)inputs.Length, inputs, InputSender.InputStructSize);
    }

    private static void SendRestoreHotkey(int virtualKey)
    {
        var inputs = new[]
        {
            Key(NativeMethods.VkControl, false),
            Key(NativeMethods.VkMenu, false),
            Key(virtualKey, false),
            Key(virtualKey, true),
            Key(NativeMethods.VkMenu, true),
            Key(NativeMethods.VkControl, true),
        };
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputSender.InputStructSize);
        Check(sent == inputs.Length, "send hotkey " + sent);
    }

    private static NativeMethods.INPUT Key(int virtualKey, bool keyUp) =>
        new()
        {
            Type = NativeMethods.InputKeyboard,
            Union = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KEYBDINPUT
                {
                    VirtualKey = (ushort)virtualKey,
                    ScanCode = (ushort)NativeMethods.MapVirtualKey((uint)virtualKey, 0),
                    Flags = keyUp ? NativeMethods.KeyeventfKeyup : 0,
                },
            },
        };

    private static void Equal(string input, string keystrokes)
    {
        var parsed = PinyinParser.Parse(input);
        Check(parsed != null, "parse " + input);
        if (parsed == null)
            return;

        Check(parsed.Keystrokes == keystrokes, $"{input} -> {parsed.Keystrokes}, expected {keystrokes}");
    }

    private static void Reject(string input)
    {
        Check(PinyinParser.Parse(input) == null, "should reject " + input.Replace("\n", "\\n"));
    }

    private static void Check(bool condition, string message)
    {
        if (condition)
            return;

        _failed++;
        Console.Error.WriteLine("FAIL " + message);
    }
}
