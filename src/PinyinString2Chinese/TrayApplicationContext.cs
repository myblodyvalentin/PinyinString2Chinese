namespace PinyinString2Chinese;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly MouseSelectionHook _hook;
    private readonly HotkeyWindow _hotkeys;
    private readonly RestoreWorker _worker;
    private readonly System.Windows.Forms.Timer _tooltipTimer;
    private readonly Control _ui = new();
    private AppSettings _settings;
    private readonly FloatBallForm _ball;
    private SettingsForm? _settingsForm;
    private string _statusTip = "";

    internal TrayApplicationContext()
    {
        _ = _ui.Handle;
        AppLog.Info("拼音还原已启动");
        var loaded = SettingsStore.Load();
        _settings = loaded.Settings;
        RememberIme();
        if (_settings.StartWithWindows)
            SettingsStore.ApplyStartup(true);

        _worker = new RestoreWorker(() => _settings, Report);
        _worker.Start();

        _hotkeys = new HotkeyWindow();
        _hotkeys.Create();
        _hotkeys.ToggleRequested += ToggleEnabled;
        _hotkeys.RestoreRequested += () => _worker.Enqueue(new RestoreJob(TriggerKind.Manual));
        AppLog.Info($"快捷键 暂停={_hotkeys.ToggleText} 还原={_hotkeys.RestoreText}");

        _hook = new MouseSelectionHook();
        _hook.Selected += OnMouseSelected;
        _hook.Install();

        _toggleItem = new ToolStripMenuItem();
        var restoreItem = new ToolStripMenuItem($"还原当前选中    {_hotkeys.RestoreText}", null, (_, _) =>
            _worker.Enqueue(new RestoreJob(TriggerKind.Manual)));
        var settingsItem = new ToolStripMenuItem("设置", null, (_, _) => ShowSettings());
        var exitItem = new ToolStripMenuItem("退出", null, (_, _) => ExitThread());
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_toggleItem, restoreItem, settingsItem, new ToolStripSeparator(), exitItem]);
        menu.Opening += (_, _) => UpdateToggleText();

        _ball = new FloatBallForm(_settings.FloatBallX, _settings.FloatBallY);
        _ball.Toggled += ToggleEnabled;
        _ball.Moved += OnBallMoved;
        _ball.MenuRequested += () =>
        {
            UpdateToggleText();
            menu.Show(Cursor.Position);
        };
        _ball.SetEnabled(_settings.Enabled);
        _ball.Show();

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleEnabled();

        _tooltipTimer = new System.Windows.Forms.Timer { Interval = 2800 };
        _tooltipTimer.Tick += (_, _) =>
        {
            _tooltipTimer.Stop();
            _notifyIcon.Text = FitTip(_statusTip);
        };

        ApplyAppearance();
        if (loaded.FirstRun)
        {
            ShowSettings();
            _notifyIcon.ShowBalloonTip(
                5000,
                "拼音还原",
                $"框选英文字符串后，原文会被清除并全部送入中文输入法。单击桌面悬浮球可开关。{_hotkeys.ToggleText} 暂停，{_hotkeys.RestoreText} 还原当前选中。",
                ToolTipIcon.Info);
        }
    }

    protected override void ExitThreadCore()
    {
        _tooltipTimer.Dispose();
        _worker.Stop();
        _hook.Dispose();
        _hotkeys.Dispose();
        _settingsForm?.Dispose();
        _ball.PrepareForExit();
        _ball.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _ui.Dispose();
        base.ExitThreadCore();
    }

    private void OnMouseSelected(TriggerKind kind)
    {
        if (!_settings.Enabled)
            return;

        if (_settings.RequireAlt && kind != TriggerKind.AltGesture)
            return;

        var effective = _settings.RequireAlt ? TriggerKind.AltGesture : TriggerKind.Automatic;
        _worker.Enqueue(new RestoreJob(effective));
    }

    private void ToggleEnabled()
    {
        _settings = _settings with { Enabled = !_settings.Enabled };
        SettingsStore.Save(_settings);
        ApplyAppearance();
        _settingsForm?.Apply(_settings, _hotkeys.ToggleText, _hotkeys.RestoreText);
    }

    private void ShowSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_settings, _hotkeys.ToggleText, _hotkeys.RestoreText);
            _settingsForm.SettingsChanged += OnSettingsChanged;
            _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        }

        _settingsForm.Apply(_settings, _hotkeys.ToggleText, _hotkeys.RestoreText);
        if (_settingsForm.Owner != _ball)
            _settingsForm.Owner = _ball;
        if (!_settingsForm.Visible)
            _settingsForm.Show();

        _settingsForm.Activate();
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        _settings = settings;
        SettingsStore.Save(_settings);
        ApplyAppearance();
    }

    private void Report(string message, bool balloon)
    {
        if (_ui.InvokeRequired)
        {
            _ui.BeginInvoke(() => Report(message, balloon));
            return;
        }

        _settingsForm?.SetRecent(message);
        if (!balloon)
        {
            _notifyIcon.Text = FitTip(message);
            _tooltipTimer.Stop();
            _tooltipTimer.Start();
            return;
        }

        _notifyIcon.ShowBalloonTip(4000, "拼音还原", message, ToolTipIcon.Warning);
    }

    private void ApplyAppearance()
    {
        UpdateToggleText();
        _notifyIcon.Icon = _settings.Enabled ? AppIcons.Running : AppIcons.Paused;
        _ball.SetEnabled(_settings.Enabled);
        _statusTip = _settings.Enabled
            ? "拼音还原：运行中。单击悬浮球可暂停。"
            : "拼音还原：已暂停。单击悬浮球继续。";
        _notifyIcon.Text = FitTip(_statusTip);
    }

    private void UpdateToggleText()
    {
        _toggleItem.Text = (_settings.Enabled ? "暂停自动还原" : "继续自动还原") + "    " + _hotkeys.ToggleText;
    }

    private void OnBallMoved(Point point)
    {
        _settings = _settings with { FloatBallX = point.X, FloatBallY = point.Y };
        SettingsStore.Save(_settings);
        _settingsForm?.Apply(_settings, _hotkeys.ToggleText, _hotkeys.RestoreText);
    }

    private void RememberIme()
    {
        var resolved = ImeController.Resolve(_settings);
        if (resolved == null)
            return;

        if (_settings.ImeLayoutName == resolved.LayoutName && _settings.ImeHkl == resolved.HklValue)
            return;

        _settings = _settings with
        {
            ImeLayoutName = resolved.LayoutName,
            ImeHkl = resolved.HklValue,
        };
        SettingsStore.Save(_settings);
    }

    private static string FitTip(string text)
    {
        const int limit = 63;
        var singleLine = text.Replace("\r", " ").Replace("\n", " ");
        return singleLine.Length <= limit ? singleLine : singleLine[..limit];
    }
}
