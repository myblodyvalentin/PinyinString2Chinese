namespace PinyinString2Chinese;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox _enabledBox;
    private readonly RadioButton _autoRadio;
    private readonly RadioButton _altRadio;
    private readonly ComboBox _imeList;
    private readonly CheckBox _startupBox;
    private readonly TextBox _previewBox;
    private readonly Label _previewResult;
    private readonly Label _recentLabel;
    private readonly Label _toggleHint;
    private readonly Label _restoreHint;
    private int? _floatBallX;
    private int? _floatBallY;
    private bool _loading = true;

    internal event Action<AppSettings>? SettingsChanged;

    internal SettingsForm(AppSettings settings, string toggleText, string restoreText)
    {
        Text = "拼音还原";
        Font = AppIcons.CreateFont(10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        AutoScroll = true;
        BackColor = Color.White;
        Icon = AppIcons.Running;

        var title = MakeLabel("拼音还原", 16f, FontStyle.Bold, Color.FromArgb(22, 28, 36));
        var summary = MakeLabel(
            "用鼠标框住一段英文。软件会删掉这段原文，并把里面的字母和数字全部打进下面指定的中文输入法。缩写也可以，不要求是标准拼音。",
            10f,
            FontStyle.Regular,
            Color.FromArgb(70, 78, 90));
        summary.MaximumSize = new Size(520, 0);

        _enabledBox = new CheckBox
        {
            Text = "启用鼠标框选还原",
            AutoSize = true,
        };
        var ballHint = MakeLabel(
            "桌面悬浮球单击即可开启或关闭，按住可拖动。",
            9.5f,
            FontStyle.Regular,
            Color.FromArgb(96, 104, 116));
        ballHint.MaximumSize = new Size(520, 0);

        var triggerLabel = SectionLabel("触发方式");
        _autoRadio = new RadioButton
        {
            Text = "框选英文字符串后自动还原",
            AutoSize = true,
        };
        _altRadio = new RadioButton
        {
            Text = "按住 Alt 再框选才还原",
            AutoSize = true,
        };

        var imeLabel = SectionLabel("指定的中文输入法");
        _imeList = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 400,
            IntegralHeight = false,
            MaxDropDownItems = 12,
        };
        var refreshButton = new Button
        {
            Text = "刷新",
            Width = 112,
            Height = 30,
        };
        refreshButton.Click += (_, _) =>
        {
            var current = ReadSettings();
            _loading = true;
            ReloadImes(current);
            _loading = false;
        };

        _startupBox = new CheckBox
        {
            Text = "登录 Windows 后自动运行",
            AutoSize = true,
        };

        var hotkeyLabel = SectionLabel("快捷键");
        _toggleHint = MakeLabel("", 10f, FontStyle.Regular, Color.FromArgb(22, 28, 36));
        _restoreHint = MakeLabel("", 10f, FontStyle.Regular, Color.FromArgb(22, 28, 36));

        var previewLabel = SectionLabel("试一试识别");
        _previewBox = new TextBox
        {
            Width = 520,
            PlaceholderText = "例如 btw、yyds、u1s1、nihao",
        };
        _previewResult = MakeLabel("可以还原，将把全部字符送入输入法：btw", 10f, FontStyle.Regular, Color.FromArgb(15, 122, 84));
        _previewResult.Name = "preview";

        var help = MakeLabel(
            "桌面悬浮球单击即可开启或关闭，按住可拖到别的位置。\n" +
            "框选到的字母和数字会按原样全部送进中文输入法，例如 btw、yyds、u1s1、nihao。\n" +
            "字符串里的空格不会打进去，避免输入法中途确认候选。\n" +
            "送错了：先按 Esc 取消输入法，再按 Ctrl+Z，原文会回到原处。\n" +
            "资源管理器里的文件、密码框不会被处理。",
            9.5f,
            FontStyle.Regular,
            Color.FromArgb(96, 104, 116));
        help.MaximumSize = new Size(520, 0);

        _recentLabel = MakeLabel("最近：尚无", 9.5f, FontStyle.Regular, Color.FromArgb(15, 122, 84));
        _recentLabel.MaximumSize = new Size(520, 0);

        var y = 16;
        Controls.Add(Place(title, 20, ref y));
        Controls.Add(Place(summary, 20, ref y, 10));
        Controls.Add(Place(_enabledBox, 20, ref y, 4));
        Controls.Add(Place(ballHint, 20, ref y, 12));
        Controls.Add(Place(triggerLabel, 20, ref y, 4));
        Controls.Add(Place(_autoRadio, 36, ref y, 2));
        Controls.Add(Place(_altRadio, 36, ref y, 12));
        Controls.Add(Place(imeLabel, 20, ref y, 4));

        _imeList.Location = new Point(20, y);
        refreshButton.Location = new Point(428, y - 1);
        Controls.Add(_imeList);
        Controls.Add(refreshButton);
        y += 40;

        Controls.Add(Place(_startupBox, 20, ref y, 12));
        Controls.Add(Place(hotkeyLabel, 20, ref y, 4));
        Controls.Add(Place(_toggleHint, 20, ref y, 2));
        Controls.Add(Place(_restoreHint, 20, ref y, 12));
        Controls.Add(Place(previewLabel, 20, ref y, 4));
        _previewBox.Location = new Point(20, y);
        Controls.Add(_previewBox);
        y += 36;
        Controls.Add(Place(_previewResult, 20, ref y, 12));
        Controls.Add(Place(help, 20, ref y, 12));
        Controls.Add(Place(_recentLabel, 20, ref y, 8));

        ClientSize = new Size(560, Math.Min(y + 8, 760));

        _enabledBox.CheckedChanged += (_, _) => Commit();
        _autoRadio.CheckedChanged += (_, _) =>
        {
            UpdatePreview();
            Commit();
        };
        _altRadio.CheckedChanged += (_, _) => Commit();
        _imeList.SelectedIndexChanged += (_, _) => Commit();
        _startupBox.CheckedChanged += (_, _) => Commit();
        _previewBox.TextChanged += (_, _) => UpdatePreview();

        Apply(settings, toggleText, restoreText);
    }

    internal void Apply(AppSettings settings, string toggleText, string restoreText)
    {
        _loading = true;
        _floatBallX = settings.FloatBallX;
        _floatBallY = settings.FloatBallY;
        _enabledBox.Checked = settings.Enabled;
        _autoRadio.Checked = !settings.RequireAlt;
        _altRadio.Checked = settings.RequireAlt;
        _startupBox.Checked = settings.StartWithWindows;
        _toggleHint.Text = $"{toggleText}    暂停或恢复自动还原";
        _restoreHint.Text = $"{restoreText}    还原当前选中的英文字符串（暂停时也能用）";
        ReloadImes(settings);
        _loading = false;
        UpdatePreview();
    }

    internal void SetRecent(string message)
    {
        _recentLabel.Text = "最近：" + message;
    }

    private void ReloadImes(AppSettings settings)
    {
        _imeList.Items.Clear();
        foreach (var ime in ImeController.List())
            _imeList.Items.Add(ime);

        if (_imeList.Items.Count == 0)
        {
            _imeList.Items.Add("没有检测到中文输入法");
            _imeList.SelectedIndex = 0;
            _imeList.Enabled = false;
            return;
        }

        _imeList.Enabled = true;
        SelectIme(settings);
    }

    private void SelectIme(AppSettings settings)
    {
        var resolved = ImeController.Resolve(settings);
        if (resolved == null)
            return;

        for (var i = 0; i < _imeList.Items.Count; i++)
        {
            if (_imeList.Items[i] is ImeChoice choice &&
                (choice.LayoutName == resolved.LayoutName || choice.Hkl == resolved.Hkl))
            {
                _imeList.SelectedIndex = i;
                return;
            }
        }
    }

    private void UpdatePreview()
    {
        _previewResult.Text = PinyinParser.Explain(_previewBox.Text, _altRadio.Checked);
    }

    private void Commit()
    {
        if (_loading)
            return;

        SettingsChanged?.Invoke(ReadSettings());
    }

    private AppSettings ReadSettings()
    {
        var selected = _imeList.SelectedItem as ImeChoice;
        return new AppSettings
        {
            Enabled = _enabledBox.Checked,
            RequireAlt = _altRadio.Checked,
            ImeLayoutName = selected?.LayoutName,
            ImeHkl = selected?.HklValue ?? 0,
            StartWithWindows = _startupBox.Checked,
            FloatBallX = _floatBallX,
            FloatBallY = _floatBallY,
        };
    }

    private static Label SectionLabel(string text) =>
        MakeLabel(text, 10f, FontStyle.Bold, Color.FromArgb(22, 28, 36));

    private static Label MakeLabel(string text, float size, FontStyle style, Color color) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Font = AppIcons.CreateFont(size, style),
            ForeColor = color,
        };

    private static Control Place(Control control, int x, ref int y, int gap = 8, int width = 520)
    {
        if (control is Label label)
        {
            var measured = TextRenderer.MeasureText(
                string.IsNullOrEmpty(label.Text) ? " " : label.Text,
                label.Font,
                new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak);
            var height = Math.Max(measured.Height + 6, label.Font.Height + 8);
            if (label.Name == "preview")
                height = Math.Max(height, 48);

            label.AutoSize = false;
            label.Size = new Size(width, height);
        }
        else if (control.AutoSize)
        {
            var preferred = control.GetPreferredSize(new Size(width, 0));
            control.AutoSize = false;
            control.Size = new Size(Math.Min(width, Math.Max(preferred.Width, 120)), Math.Max(preferred.Height, 26));
        }

        control.Location = new Point(x, y);
        y += control.Height + gap;
        return control;
    }
}
