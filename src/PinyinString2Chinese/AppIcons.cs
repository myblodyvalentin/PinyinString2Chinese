using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PinyinString2Chinese;

internal static class AppIcons
{
    internal static Icon Running { get; } = Create(Color.FromArgb(15, 122, 84));

    internal static Icon Paused { get; } = Create(Color.FromArgb(112, 119, 130));

    internal static Font CreateFont(float size, FontStyle style = FontStyle.Regular)
    {
        try
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }
        catch
        {
            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
        }
    }

    private static Icon Create(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(color);
            using var font = CreatePixelFont();
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            graphics.DrawString("拼", font, Brushes.White, new RectangleF(0, 0, 32, 32), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static Font CreatePixelFont()
    {
        try
        {
            return new Font("Microsoft YaHei UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font(FontFamily.GenericSansSerif, 12f, FontStyle.Bold, GraphicsUnit.Pixel);
        }
    }
}
