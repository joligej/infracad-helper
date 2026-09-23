using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using MediaImageSource = System.Windows.Media.ImageSource;

namespace NlcsLegenda.Plugin;

internal static class RibbonIcons
{
    private static readonly Color Badge = Color.FromArgb(0, 120, 215);
    private static readonly Dictionary<string, MediaImageSource> Cache = new();

    public static MediaImageSource? Get(string command, int size)
    {
        var key = $"{command}@{size}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float s = size / 32f;
                DrawBadge(g, s);
                DrawGlyph(command, g, s);
            }

            var image = ToImageSource(bitmap);
            Cache[key] = image;
            return image;
        }
        catch
        {
            // Zonder icoon toont de knop gewoon zijn tekst.
            return null;
        }
    }

    private static void DrawBadge(Graphics g, float s)
    {
        using var brush = new SolidBrush(Badge);
        using var path = Rounded(new RectangleF(1.5f * s, 1.5f * s, 29f * s, 29f * s), 7f * s);
        g.FillPath(brush, path);
    }

    private static void DrawGlyph(string command, Graphics g, float s)
    {
        using var pen = new Pen(Color.White, Math.Max(1.5f, 2.3f * s))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var fill = new SolidBrush(Color.White);

        switch (command)
        {
            case "NLCSLEGENDA":
                g.FillRectangle(fill, 8 * s, 9 * s, 6 * s, 4 * s);
                g.DrawLine(pen, 16 * s, 11 * s, 24 * s, 11 * s);
                g.DrawRectangle(pen, 8 * s, 18 * s, 6 * s, 4 * s);
                g.DrawLine(pen, 16 * s, 20 * s, 24 * s, 20 * s);
                break;

            case "NLCSLEGENDAUPDATE":
                g.DrawArc(pen, 9 * s, 9 * s, 14 * s, 14 * s, 30, 265);
                FillTriangle(g, fill, s, 22.5f, 7.5f, 25.5f, 12.5f, 19.5f, 12.5f);
                break;

            case "NLCSLEGENDAINFO":
                foreach (var y in new[] { 11f, 16f, 21f })
                {
                    g.FillEllipse(fill, (8 * s) - 1.4f * s, (y * s) - 1.4f * s, 2.8f * s, 2.8f * s);
                    g.DrawLine(pen, 12.5f * s, y * s, 24 * s, y * s);
                }
                break;

            case "NLCSLEGENDAEXPORT":
                g.DrawLines(pen, new[]
                {
                    new PointF(9 * s, 16 * s), new PointF(9 * s, 24 * s),
                    new PointF(23 * s, 24 * s), new PointF(23 * s, 16 * s)
                });
                g.DrawLine(pen, 16 * s, 22 * s, 16 * s, 9 * s);
                FillTriangle(g, fill, s, 16f, 7f, 12.5f, 12f, 19.5f, 12f);
                break;

            case "NLCSLEGENDABATCH":
                g.DrawRectangle(pen, 11 * s, 8 * s, 11 * s, 14 * s);
                g.DrawLine(pen, 9 * s, 11 * s, 9 * s, 24 * s);
                g.DrawLine(pen, 9 * s, 24 * s, 20 * s, 24 * s);
                g.DrawLine(pen, 14 * s, 12 * s, 19 * s, 12 * s);
                g.DrawLine(pen, 14 * s, 15 * s, 19 * s, 15 * s);
                g.DrawLine(pen, 14 * s, 18 * s, 19 * s, 18 * s);
                break;

            case "NLCSLEGENDAVIEWPORT":
                g.DrawRectangle(pen, 7 * s, 9 * s, 18 * s, 14 * s);
                g.DrawRectangle(pen, 11 * s, 13 * s, 10 * s, 6 * s);
                break;

            case "NLCSLEGENDAOPTIES":
                g.DrawLine(pen, 8 * s, 12 * s, 24 * s, 12 * s);
                g.DrawLine(pen, 8 * s, 20 * s, 24 * s, 20 * s);
                Knob(g, fill, pen, s, 19f, 12f);
                Knob(g, fill, pen, s, 12f, 20f);
                break;

            case "NLCSLEGENDASAMENSTELLEN":
                g.DrawLines(pen, new[] { new PointF(8 * s, 11 * s), new PointF(10 * s, 13 * s), new PointF(13 * s, 9 * s) });
                g.DrawLine(pen, 16 * s, 11 * s, 24 * s, 11 * s);
                g.DrawLines(pen, new[] { new PointF(8 * s, 19 * s), new PointF(10 * s, 21 * s), new PointF(13 * s, 17 * s) });
                g.DrawLine(pen, 16 * s, 19 * s, 24 * s, 19 * s);
                break;

            case "NLCSLEGENDABEHEER":
                // Lijst van legenda's: kleine kaders met een regel ernaast.
                g.DrawRectangle(pen, 7 * s, 9 * s, 5 * s, 4 * s);
                g.DrawLine(pen, 14 * s, 11 * s, 25 * s, 11 * s);
                g.DrawRectangle(pen, 7 * s, 18 * s, 5 * s, 4 * s);
                g.DrawLine(pen, 14 * s, 20 * s, 25 * s, 20 * s);
                break;

            case "NLCSLEGENDAOMSCHRIJVINGEN":
                g.DrawLine(pen, 9 * s, 10 * s, 24 * s, 10 * s);
                g.DrawLine(pen, 9 * s, 14.5f * s, 21 * s, 14.5f * s);
                g.DrawLine(pen, 9 * s, 19 * s, 24 * s, 19 * s);
                g.DrawLine(pen, 9 * s, 23.5f * s, 18 * s, 23.5f * s);
                break;

            case "NLCSLEGENDATEKST":
                g.DrawLine(pen, 10 * s, 22 * s, 21 * s, 11 * s);
                FillTriangle(g, fill, s, 23f, 9f, 19.5f, 10.5f, 21f, 13f);
                g.DrawLine(pen, 9 * s, 23 * s, 11 * s, 21 * s);
                break;

            case "NLCSLEGENDASTATUS":
                g.DrawLine(pen, 9 * s, 10 * s, 23 * s, 10 * s);
                g.DrawLine(pen, 9 * s, 16 * s, 20 * s, 16 * s);
                g.DrawLine(pen, 9 * s, 22 * s, 17 * s, 22 * s);
                g.FillRectangle(fill, 5.5f * s, 8.5f * s, 2.2f * s, 2.2f * s);
                g.FillRectangle(fill, 5.5f * s, 14.5f * s, 2.2f * s, 2.2f * s);
                g.FillRectangle(fill, 5.5f * s, 20.5f * s, 2.2f * s, 2.2f * s);
                break;

            case "NLCSLEGENDAXREFS":
                g.DrawRectangle(pen, 8 * s, 9 * s, 11 * s, 11 * s);
                g.DrawRectangle(pen, 14 * s, 14 * s, 11 * s, 11 * s);
                break;

            case "NLCSLEGENDAPRESET":
                using (var path = new GraphicsPath())
                {
                    path.AddLines(new[]
                    {
                        new PointF(10 * s, 8 * s), new PointF(22 * s, 8 * s),
                        new PointF(22 * s, 24 * s), new PointF(16 * s, 19 * s), new PointF(10 * s, 24 * s)
                    });
                    path.CloseFigure();
                    g.DrawPath(pen, path);
                }
                break;

            case "NLCSLEGENDACONFIG":
                using (var path = new GraphicsPath())
                {
                    path.AddLines(new[]
                    {
                        new PointF(8 * s, 12 * s), new PointF(13 * s, 12 * s), new PointF(15 * s, 14 * s),
                        new PointF(24 * s, 14 * s), new PointF(24 * s, 23 * s), new PointF(8 * s, 23 * s)
                    });
                    path.CloseFigure();
                    g.DrawPath(pen, path);
                }
                break;
            case "NLCSLEGENDAWAAROM":
                // Vraagteken: boog, korte staart en een punt.
                g.DrawArc(pen, 11 * s, 8 * s, 10 * s, 9 * s, 175, 235);
                g.DrawLine(pen, 16 * s, 15 * s, 16 * s, 18 * s);
                g.FillEllipse(fill, 16 * s - 1.4f * s, 21.5f * s - 1.4f * s, 2.8f * s, 2.8f * s);
                break;
        }
    }

    private static void Knob(Graphics g, Brush fill, Pen pen, float s, float cx, float cy)
    {
        float r = 3f * s;
        g.FillEllipse(fill, cx * s - r, cy * s - r, r * 2, r * 2);
        using var ring = new Pen(Badge, Math.Max(1f, 1.4f * s));
        g.DrawEllipse(ring, cx * s - r * 0.45f, cy * s - r * 0.45f, r * 0.9f, r * 0.9f);
    }

    private static void FillTriangle(Graphics g, Brush fill, float s,
        float x1, float y1, float x2, float y2, float x3, float y3) =>
        g.FillPolygon(fill, new[]
        {
            new PointF(x1 * s, y1 * s), new PointF(x2 * s, y2 * s), new PointF(x3 * s, y3 * s)
        });

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static MediaImageSource ToImageSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
