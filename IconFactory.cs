using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BgToggle;

/// <summary>
/// Generates the tray icon at runtime so we don't have to ship a .ico file.
/// Draws a rounded "BG" badge; color tracks active state (idle gray, active blue).
/// </summary>
public static class IconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Create(bool active = true)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            var bg = active
                ? Color.FromArgb(255, 38, 132, 255)   // blue
                : Color.FromArgb(255, 90, 90, 96);    // gray
            var rect = new Rectangle(1, 1, size - 2, size - 2);

            using (var path = RoundedRect(rect, 7))
            using (var brush = new SolidBrush(bg))
                g.FillPath(brush, path);

            using var font = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var fg = new SolidBrush(Color.White);
            g.DrawString("BG", font, fg, rect, sf);
        }

        // Icon.FromHandle does NOT own the HICON; copy via Clone() then free.
        var hicon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hicon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
