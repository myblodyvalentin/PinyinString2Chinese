namespace PinyinString2Chinese;

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int ToggleId = 1;
    private const int RestoreId = 2;

    internal string ToggleText { get; private set; } = "未注册";

    internal string RestoreText { get; private set; } = "未注册";

    internal event Action? ToggleRequested;

    internal event Action? RestoreRequested;

    internal void Create()
    {
        CreateHandle(new CreateParams
        {
            Caption = "PinyinString2Chinese.Hotkeys",
            Parent = (IntPtr)(-3),
        });

        if (TryRegister(ToggleId, 0x50, "Ctrl+Alt+P", toggle: true) ||
            TryRegister(ToggleId, 0x77, "Ctrl+Alt+F8", toggle: true))
        {
        }
        else
        {
            AppLog.Error("没有可用的暂停热键。");
        }

        if (!TryRegister(RestoreId, 0x52, "Ctrl+Alt+R", toggle: false) &&
            !TryRegister(RestoreId, 0x78, "Ctrl+Alt+F9", toggle: false))
        {
            AppLog.Error("没有可用的还原热键。");
        }
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero)
            return;

        NativeMethods.UnregisterHotKey(Handle, ToggleId);
        NativeMethods.UnregisterHotKey(Handle, RestoreId);
        DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmHotkey)
        {
            var id = m.WParam.ToInt32();
            AppLog.Info(id == ToggleId ? "热键：暂停或恢复" : "热键：还原当前选中");
            switch (id)
            {
                case ToggleId:
                    ToggleRequested?.Invoke();
                    break;
                case RestoreId:
                    RestoreRequested?.Invoke();
                    break;
            }

            return;
        }

        base.WndProc(ref m);
    }

    private bool TryRegister(int id, uint virtualKey, string label, bool toggle)
    {
        var modifiers = NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat;
        if (!NativeMethods.RegisterHotKey(Handle, id, modifiers, virtualKey))
            return false;

        if (toggle)
            ToggleText = label;
        else
            RestoreText = label;

        return true;
    }
}
