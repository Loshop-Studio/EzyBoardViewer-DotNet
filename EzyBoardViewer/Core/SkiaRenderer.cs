using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EzyBoardViewer.Models;
using SkiaSharp;

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 把 Page 模型绘制到 Skia 画布（与 SvgRenderer 同一套图元逻辑，用于视频逐帧）。
    /// 中文字体需调用方提供 .ttf（如 HarmonyOS Sans SC），否则拉丁字符用内置字体、中文会缺字。
    /// </summary>
    public static class SkiaRenderer
    {
        public static SKBitmap RenderToBitmap(Page page, IBoardSource src, int width, int height)
        {
            var bmp = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bmp);
            DrawBackground(canvas, page);
            foreach (var el in page.Elements)
                DrawElement(canvas, el, src).GetAwaiter().GetResult();
            return bmp;
        }

        private static SKMatrix ToMatrix(float[] m)
        {
            // SVG matrix(a,b,c,d,e,f) → Skia: ScaleX=a, SkewX=c, TransX=e, SkewY=b, ScaleY=d, TransY=f
            return new SKMatrix
            {
                ScaleX = m[0], SkewX = m[2], TransX = m[4],
                SkewY = m[1], ScaleY = m[3], TransY = m[5],
                Persp0 = 0, Persp1 = 0, Persp2 = 1
            };
        }

        private static void DrawBackground(SKCanvas c, Page p)
        {
            using var paint = new SKPaint { Color = SKColor.Parse(p.Background), Style = SKPaintStyle.Fill };
            c.DrawRect(0, 0, p.Width, p.Height, paint);
            if (p.BackgroundLine != null && p.BackgroundLine.Spacing > 0)
            {
                var bl = p.BackgroundLine;
                var col = new SKColor((uint)(bl.Color & 0xFFFFFFFF));
                using var lp = new SKPaint { Color = col, StrokeWidth = bl.Width, Style = SKPaintStyle.Stroke };
                for (float y = bl.Spacing; y < p.Height; y += bl.Spacing) c.DrawLine(0, y, p.Width, y, lp);
                if (bl.Type == 1)
                    for (float x = bl.Spacing; x < p.Width; x += bl.Spacing) c.DrawLine(x, 0, x, p.Height, lp);
            }
        }

        private static async Task DrawElement(SKCanvas c, Element el, IBoardSource src)
        {
            c.Save();
            if (el.Transform != null)
            {
                var m = ToMatrix(el.Transform);
                c.Concat(ref m);
            }
            switch (el)
            {
                case StrokeElement s: DrawStroke(c, s); break;
                case ImageElement ie: await DrawImage(c, ie, src); break;
                case TextElement te: DrawText(c, te); break;
                case GeometryElement ge: DrawGeom(c, ge); break;
            }
            foreach (var ch in el.Children)
                await DrawElement(c, ch, src);
            c.Restore();
        }

        private static void DrawStroke(SKCanvas c, StrokeElement s)
        {
            if (s.Points.Count == 0) return;
            using var paint = new SKPaint
            {
                Color = SKColor.Parse(s.Color).WithAlpha((byte)(s.Opacity * 255)),
                StrokeWidth = s.StrokeWidth,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };
            if (s.DashArray != null) paint.PathEffect = SKPathEffect.CreateDash(s.DashArray.Select(d => d).ToArray(), 0);
            if (s.Points.Count == 1)
            {
                c.DrawCircle(s.Points[0].X, s.Points[0].Y, s.StrokeWidth / 2, new SKPaint { Color = paint.Color, Style = SKPaintStyle.Fill, IsAntialias = true });
                return;
            }
            using var path = new SKPath();
            path.MoveTo(s.Points[0].X, s.Points[0].Y);
            int n = s.Points.Count;
            if (!s.IsBeeline && n > 2)
            {
                for (int i = 1; i < n - 1; i++)
                    path.QuadTo(s.Points[i].X, s.Points[i].Y, (s.Points[i].X + s.Points[i + 1].X) / 2, (s.Points[i].Y + s.Points[i + 1].Y) / 2);
                path.LineTo(s.Points[n - 1].X, s.Points[n - 1].Y);
            }
            else
            {
                for (int i = 1; i < n; i++) path.LineTo(s.Points[i].X, s.Points[i].Y);
            }
            c.DrawPath(path, paint);
        }

        private static void DrawGeom(SKCanvas c, GeometryElement ge)
        {
            using var paint = new SKPaint
            {
                Color = SKColor.Parse(ge.Color).WithAlpha((byte)(ge.Opacity * 255)),
                StrokeWidth = ge.StrokeWidth,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            if (ge.GeoType == 1 && ge.Points.Count > 0)
            {
                using var path = new SKPath();
                path.MoveTo(ge.Points[0].X, ge.Points[0].Y);
                for (int i = 1; i < ge.Points.Count; i++) path.LineTo(ge.Points[i].X, ge.Points[i].Y);
                path.Close();
                c.DrawPath(path, paint);
            }
            else
            {
                float l = ge.Rect[0], t = ge.Rect[1], r = ge.Rect[2], b = ge.Rect[3];
                if (ge.Points.Count >= 2) { l = ge.Points.Min(p => p.X); r = ge.Points.Max(p => p.X); t = ge.Points.Min(p => p.Y); b = ge.Points.Max(p => p.Y); }
                c.DrawOval(new SKRect(l, t, r, b), paint);
            }
        }

        private static void DrawText(SKCanvas c, TextElement te)
        {
            var col = SKColor.Parse(te.Color).WithAlpha((byte)(te.Opacity * 255));
            using var paint = new SKPaint { Color = col, Style = SKPaintStyle.Fill, IsAntialias = true, TextSize = te.FontSize, Typeface = SKTypeface.Default };
            var lines = te.Text.Split('\n');
            float x = te.Rect[0], y = te.Rect[1];
            for (int i = 0; i < lines.Length; i++)
                c.DrawText(lines[i], x, y + te.FontSize * (0.85f + i * 1.25f), paint);
        }

        private static async Task DrawImage(SKCanvas c, ImageElement ie, IBoardSource src)
        {
            float l = ie.Rect[0], t = ie.Rect[1], r = ie.Rect[2], b = ie.Rect[3];
            string name = ie.ImageName;
            string key = null;
            if (!string.IsNullOrEmpty(name))
            {
                key = src.Names.FirstOrDefault(n => n == "res/image/" + name)
                   ?? src.Names.FirstOrDefault(n => n.Split('/').Last() == name.Split('/').Last());
            }
            if (key == null) return;
            var u = await src.ReadAsync(key);
            if (u == null) return;
            using var bmp = SKBitmap.Decode(u);
            if (bmp == null) return;
            c.DrawBitmap(bmp, new SKRect(l, t, r, b));
        }
    }
}
