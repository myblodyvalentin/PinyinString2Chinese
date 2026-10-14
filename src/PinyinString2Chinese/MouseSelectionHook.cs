using System.Runtime.InteropServices;

namespace PinyinString2Chinese;

internal sealed class MouseSelectionHook : IDisposable
{
    private readonly NativeMethods.HookProc _callback;
    private IntPtr _hook;
    private NativeMethods.POINT _downPoint;
    private uint _downTime;
    private bool _altAtDown;
    private bool _secondClick;
    private bool _fromBall;
    private long _lastFire;

    internal event Action<TriggerKind>? Selected;

    internal MouseSelectionHook()
    {
        _callback = OnMouse;
    }

    internal void Install()
    {
        if (_hook != IntPtr.Zero)
            return;

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_hook == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "安装鼠标钩子失败");
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
            return;

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var message = wParam.ToInt32();
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if (message == NativeMethods.WmLButtonDown)
                    OnDown(data);
                else if (message == NativeMethods.WmLButtonUp)
                    OnUp(data);
            }
            catch (Exception exception)
            {
                AppLog.Error(exception);
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void OnDown(NativeMethods.MSLLHOOKSTRUCT data)
    {
        _fromBall = FloatBallForm.Covers(data.Point.X, data.Point.Y);
        var withinTime = _downTime != 0 && unchecked(data.Time - _downTime) <= NativeMethods.GetDoubleClickTime();
        var withinSize = Math.Abs(data.Point.X - _downPoint.X) <= NativeMethods.GetSystemMetrics(NativeMethods.SmCxDoubleClk)
            && Math.Abs(data.Point.Y - _downPoint.Y) <= NativeMethods.GetSystemMetrics(NativeMethods.SmCyDoubleClk);
        _secondClick = !_fromBall && withinTime && withinSize;
        _altAtDown = NativeMethods.IsKeyDown(NativeMethods.VkMenu);
        _downPoint = data.Point;
        _downTime = data.Time;
    }

    private void OnUp(NativeMethods.MSLLHOOKSTRUCT data)
    {
        if (_fromBall)
        {
            _fromBall = false;
            _secondClick = false;
            _downTime = 0;
            return;
        }

        var distance = Math.Abs(data.Point.X - _downPoint.X) + Math.Abs(data.Point.Y - _downPoint.Y);
        var dragged = distance >= 8;
        var doubleClick = _secondClick;
        _secondClick = false;
        if (!dragged && !doubleClick)
            return;

        var now = Environment.TickCount64;
        if (now - _lastFire < 400)
            return;

        _lastFire = now;
        var alt = _altAtDown || NativeMethods.IsKeyDown(NativeMethods.VkMenu);
        Selected?.Invoke(alt ? TriggerKind.AltGesture : TriggerKind.Automatic);
    }
}
