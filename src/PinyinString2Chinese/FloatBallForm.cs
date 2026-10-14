using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace PinyinString2Chinese;

internal static class FloatBallMath
{
    internal const int DesignSize = 84;
    internal const int DragSlop = 6;

    internal static int SizeForDpi(int dpi)
    {
        if (dpi < 96)
            dpi = 96;

        return Math.Max(64, (int)Math.Round(DesignSize * dpi / 96.0));
    }

    internal static int SystemDpi()
    {
        try
        {
            var dpi = (int)NativeMethods.GetDpiForSystem();
            return dpi >= 96 ? dpi : 96;
        }
        catch
        {
            return 96;
        }
    }

    internal static Rectangle BallBounds(int size)
    {
        var margin = Math.Max(6, size / 10);
        var side = Math.Max(1, size - margin * 2);
        return new Rectangle(margin, margin, side, side);
    }

    internal static bool IsInside(int localX, int localY, int size)
    {
        var bounds = BallBounds(size);
        var radius = bounds.Width / 2.0;
        var dx = localX + 0.5 - (bounds.Left + radius);
        var dy = localY + 0.5 - (bounds.Top + radius);
        return dx * dx + dy * dy <= radius * radius;
    }

    internal static bool IsDrag(int dx, int dy) => Math.Abs(dx) + Math.Abs(dy) >= DragSlop;

    internal static Point Clamp(Rectangle area, int x, int y, int size)
    {
        if (size < 1)
            size = 1;

        if (area.Width < size)
            x = area.Left;
        else
            x = Math.Clamp(x, area.Left, area.Right - size);

        if (area.Height < size)
            y = area.Top;
        else
            y = Math.Clamp(y, area.Top, area.Bottom - size);

        return new Point(x, y);
    }

    internal static Point DefaultLocation(Rectangle workingArea, int size) =>
        new(workingArea.Right - size - 24, workingArea.Bottom - size - 120);

    internal static Point Resolve(int? x, int? y, int size)
    {
        if (x is int sx && y is int sy)
        {
            var screen = Screen.FromPoint(new Point(sx + size / 2, sy + size / 2));
            return Clamp(screen.WorkingArea, sx, sy, size);
        }

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        return Clamp(area, DefaultLocation(area, size).X, DefaultLocation(area, size).Y, size);
    }
}

internal sealed class FloatBallForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;

    private static FloatBallForm? _current;

    private readonly ToolTip _tip = new();
    private bool _enabled = true;
    private bool _allowClose;
    private Point _downCursor;
    private Point _downLocation;
    private bool _moved;
    private long _lastToggle;

    internal event Action? Toggled;

    internal event Action<Point>? Moved;

    internal event Action? MenuRequested;

    internal FloatBallForm(int? x, int? y)
    {
        _current = this;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "拼音还原开关";
        ApplySize(FloatBallMath.SystemDpi());
        Location = FloatBallMath.Resolve(x, y, Width);
        UpdateTip();
    }

    internal static bool Covers(int screenX, int screenY)
    {
        var form = _current;
        if (form == null || form.IsDisposed || !form.IsHandleCreated || !form.Visible)
            return false;

        var local = form.PointToClient(new Point(screenX, screenY));
        return FloatBallMath.IsInside(local.X, local.Y, form.ClientSize.Width);
    }

    internal static Bitmap Draw(bool enabled, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var bounds = FloatBallMath.BallBounds(size);
        var shadow = bounds;
        shadow.Offset(0, Math.Max(2, size / 28));
        using (var shadowBrush = new SolidBrush(Color.FromArgb(56, 0, 0, 0)))
            graphics.FillEllipse(shadowBrush, shadow);

        var fill = enabled ? Color.FromArgb(15, 122, 84) : Color.FromArgb(112, 119, 130);
        using (var brush = new SolidBrush(fill))
            graphics.FillEllipse(brush, bounds);

        var gloss = new Rectangle(
            bounds.Left + bounds.Width / 5,
            bounds.Top + bounds.Height / 7,
            bounds.Width / 2,
            bounds.Height / 3);
        using (var glossBrush = new SolidBrush(Color.FromArgb(72, 255, 255, 255)))
            graphics.FillEllipse(glossBrush, gloss);

        using (var edge = new Pen(Color.FromArgb(210, 255, 255, 255), Math.Max(2f, size / 32f)))
            graphics.DrawEllipse(edge, bounds);

        using var font = CreatePixelFont(Math.Max(16f, bounds.Height * 0.42f));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        graphics.DrawString(enabled ? "开" : "关", font, Brushes.White, bounds, format);
        return bitmap;
    }

    internal void PrepareForExit() => _allowClose = true;

    internal void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        AccessibleDescription = enabled ? "运行中，单击关闭" : "已暂停，单击开启";
        UpdateTip();
        if (IsHandleCreated)
            Render();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExLayered = 0x00080000;
            const int wsExNoActivate = 0x08000000;
            const int wsExToolWindow = 0x00000080;
            const int wsExTopMost = 0x00000008;
            var parameters = base.CreateParams;
            parameters.ExStyle |= wsExLayered | wsExNoActivate | wsExToolWindow | wsExTopMost;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var origin = Location;
        ApplySize(DeviceDpi);
        Location = FloatBallMath.Resolve(origin.X, origin.Y, Width);
        Render();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();
        AppLog.Info($"悬浮球位于 {Left},{Top}，大小 {Width}");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        var origin = Location;
        base.OnDpiChanged(e);
        ApplySize(e.DeviceDpiNew);
        Location = FloatBallMath.Resolve(origin.X, origin.Y, Width);
        Render();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _downCursor = Cursor.Position;
            _downLocation = Location;
            _moved = false;
            Capture = true;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Capture && (e.Button & MouseButtons.Left) == MouseButtons.Left)
        {
            var now = Cursor.Position;
            var dx = now.X - _downCursor.X;
            var dy = now.Y - _downCursor.Y;
            if (FloatBallMath.IsDrag(dx, dy))
                _moved = true;

            if (_moved)
            {
                Cursor = Cursors.SizeAll;
                Location = new Point(_downLocation.X + dx, _downLocation.Y + dy);
            }
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Capture = false;
            Cursor = Cursors.Hand;
            if (_moved)
            {
                var parked = FloatBallMath.Resolve(Location.X, Location.Y, Width);
                Location = parked;
                Moved?.Invoke(parked);
            }
            else
            {
                TryToggle();
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            MenuRequested?.Invoke();
        }

        base.OnMouseUp(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (ReferenceEquals(_current, this))
            _current = null;

        _tip.Dispose();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseActivate)
        {
            m.Result = (IntPtr)MaNoActivate;
            return;
        }

        if (m.Msg == WmNcHitTest)
        {
            var packed = unchecked((int)m.LParam.ToInt64());
            var screenX = (short)(packed & 0xFFFF);
            var screenY = (short)((packed >> 16) & 0xFFFF);
            var local = PointToClient(new Point(screenX, screenY));
            if (!FloatBallMath.IsInside(local.X, local.Y, ClientSize.Width))
            {
                m.Result = (IntPtr)HtTransparent;
                return;
            }
        }

        base.WndProc(ref m);
    }

    private void TryToggle()
    {
        var now = Environment.TickCount64;
        var gap = NativeMethods.GetDoubleClickTime();
        if (gap < 200)
            gap = 200;

        if (now - _lastToggle < gap)
            return;

        _lastToggle = now;
        Toggled?.Invoke();
    }

    private void ApplySize(int dpi)
    {
        var size = FloatBallMath.SizeForDpi(dpi);
        if (ClientSize.Width == size && ClientSize.Height == size)
            return;

        ClientSize = new Size(size, size);
    }

    private void UpdateTip()
    {
        _tip.SetToolTip(this, _enabled
            ? "运行中。单击关闭，拖动可换位置。"
            : "已暂停。单击开启，拖动可换位置。");
    }

    private void Render()
    {
        if (!IsHandleCreated || ClientSize.Width < 1)
            return;

        try
        {
            using var bitmap = Draw(_enabled, ClientSize.Width);
            ApplyLayer(bitmap);
        }
        catch (Exception exception)
        {
            AppLog.Error(exception);
        }
    }

    private void ApplyLayer(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var header = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };

        var screen = NativeMethods.GetDC(IntPtr.Zero);
        var memory = NativeMethods.CreateCompatibleDC(screen);
        var section = NativeMethods.CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (section == IntPtr.Zero || bits == IntPtr.Zero)
        {
            AppLog.Error("悬浮球缓冲创建失败，错误码 " + Marshal.GetLastWin32Error());
            if (memory != IntPtr.Zero)
                NativeMethods.DeleteDC(memory);
            if (screen != IntPtr.Zero)
                NativeMethods.ReleaseDC(IntPtr.Zero, screen);
            return;
        }

        var source = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = width * 4;
            var buffer = new byte[rowBytes];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(source.Scan0 + y * source.Stride, buffer, 0, rowBytes);
                for (var i = 0; i < rowBytes; i += 4)
                {
                    var alpha = buffer[i + 3];
                    buffer[i] = (byte)(buffer[i] * alpha / 255);
                    buffer[i + 1] = (byte)(buffer[i + 1] * alpha / 255);
                    buffer[i + 2] = (byte)(buffer[i + 2] * alpha / 255);
                }

                Marshal.Copy(buffer, 0, bits + y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(source);
        }

        var previous = NativeMethods.SelectObject(memory, section);
        var destination = new NativeMethods.POINT { X = Left, Y = Top };
        var origin = new NativeMethods.POINT();
        var size = new NativeMethods.SIZE { CX = width, CY = height };
        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = NativeMethods.AcSrcOver,
            SourceConstantAlpha = 255,
            AlphaFormat = NativeMethods.AcSrcAlpha,
        };
        var updated = NativeMethods.UpdateLayeredWindow(
            Handle,
            screen,
            ref destination,
            ref size,
            memory,
            ref origin,
            0,
            ref blend,
            NativeMethods.UlwAlpha);
        if (!updated)
            AppLog.Error("悬浮球绘制失败，错误码 " + Marshal.GetLastWin32Error());

        NativeMethods.SelectObject(memory, previous);
        NativeMethods.DeleteObject(section);
        NativeMethods.DeleteDC(memory);
        NativeMethods.ReleaseDC(IntPtr.Zero, screen);
    }

    private static Font CreatePixelFont(float pixels)
    {
        try
        {
            return new Font("Microsoft YaHei UI", pixels, FontStyle.Bold, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font(FontFamily.GenericSansSerif, pixels, FontStyle.Bold, GraphicsUnit.Pixel);
        }
    }
}
