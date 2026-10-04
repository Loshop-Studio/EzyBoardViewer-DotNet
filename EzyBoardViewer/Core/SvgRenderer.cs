using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EzyBoardViewer.Models;

// 占位，确保 Math 可用

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 把 Page 模型渲染为 SVG 字符串。复刻 board.js 的 pageSvg（背景 / 背景线 / 笔迹中点二次贝塞尔 / 文本 tspan / 图片 base64 / 几何）。
    /// </summary>
    public static class SvgRenderer
    {
        public static async Task<string> RenderAsync(Page page, IBoardSource src)
        {
            int W = page.Width, H = page.Height;
            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\">");
            sb.Append($"<rect width=\"{W}\" height=\"{H}\" fill=\"{page.Background}\" fill-opacity=\"1\"/>");

            if (page.BackgroundLine != null)
            {
                var lc = page.BackgroundLine;
                var (k, o) = Util.Col(lc.Color);
                if (o > 0 && lc.Spacing > 0)
                {
                    var d = new StringBuilder();
                    for (float y = lc.Spacing; y < H; y += lc.Spacing) d.Append($"M0 {Util.R2(y)}H{W}");
                    if (lc.Type == 1)
                        for (float x = lc.Spacing; x < W; x += lc.Spacing) d.Append($"M{Util.R2(x)} 0V{H}");
                    sb.Append($"<path d=\"{d}\" stroke=\"{k}\" stroke-opacity=\"{Util.R2(o)}\" stroke-width=\"{Util.R2(lc.Width)}\" fill=\"none\"/>");
                }
            }

            foreach (var el in page.Elements)
                sb.Append(await NodeSvg(el, src));
            sb.Append("</svg>");
            return sb.ToString();
        }

        private static async Task<string> NodeSvg(Element el, IBoardSource src)
        {
            string inner = el switch
            {
                ImageElement ie => await ImageSvg(ie, src),
                StrokeElement se => StrokeSvg(se),
                TextElement te => TextSvg(te),
                GeometryElement ge => GeomSvg(ge),
                _ => ""
            };
            foreach (var c in el.Children)
                inner += await NodeSvg(c, src);

            if (el.Transform != null)
                return $"<g transform=\"matrix({Util.R2(el.Transform)})\">{inner}</g>";
            return inner;
        }

        private static string StrokeSvg(StrokeElement s)
        {
            if (s.Points.Count == 0) return "";
            if (s.Points.Count == 1)
                return $"<circle cx=\"{Util.R2(s.Points[0].X)}\" cy=\"{Util.R2(s.Points[0].Y)}\" r=\"{Util.R2(s.StrokeWidth / 2)}\" fill=\"{s.Color}\"/>";

            string Q(float x, float y) => $"{Util.R2(x)} {Util.R2(y)}";
            int n = s.Points.Count;
            var d = "M" + Q(s.Points[0].X, s.Points[0].Y);
            if (!s.IsBeeline && n > 2)
            {
                for (int i = 1; i < n - 1; i++)
                    d += "Q" + Q(s.Points[i].X, s.Points[i].Y) + " " + Q((s.Points[i].X + s.Points[i + 1].X) / 2, (s.Points[i].Y + s.Points[i + 1].Y) / 2);
                d += "L" + Q(s.Points[n - 1].X, s.Points[n - 1].Y);
            }
            else
            {
                for (int i = 1; i < n; i++) d += "L" + Q(s.Points[i].X, s.Points[i].Y);
            }
            string dash = s.DashArray != null ? $" stroke-dasharray=\"{Util.R2(s.DashArray[0])} {Util.R2(s.DashArray[1])}\"" : "";
            return $"<path d=\"{d}\" fill=\"none\" stroke=\"{s.Color}\" stroke-opacity=\"{Util.R2(s.Opacity)}\" stroke-width=\"{Util.R2(s.StrokeWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"{dash}/>";
        }

        private static string TextSvg(TextElement t)
        {
            float l = t.Rect[0], top = t.Rect[1];
            const string font = "'PingFang SC','Microsoft YaHei','Noto Sans CJK SC',sans-serif";
            var sb = new StringBuilder($"<text font-family=\"{font}\" font-size=\"{Util.R2(t.FontSize)}\" fill=\"{t.Color}\" fill-opacity=\"{Util.R2(t.Opacity)}\" xml:space=\"preserve\">");
            var lines = t.Text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                sb.Append($"<tspan x=\"{Util.R2(l)}\" y=\"{Util.R2(top + t.FontSize * (0.85f + i * 1.25f))}\">{Util.Esc(lines[i])}</tspan>");
            sb.Append("</text>");
            return sb.ToString();
        }

        private static async Task<string> ImageSvg(ImageElement ie, IBoardSource src)
        {
            float l = ie.Rect[0], t = ie.Rect[1], r = ie.Rect[2], b = ie.Rect[3];
            string name = ie.ImageName;
            string key = null;
            if (!string.IsNullOrEmpty(name))
            {
                key = src.Names.FirstOrDefault(n => n == "res/image/" + name)
                   ?? src.Names.FirstOrDefault(n => n.Split('/').Last() == name.Split('/').Last());
            }
            if (key == null)
                return $"<rect x=\"{Util.R2(l)}\" y=\"{Util.R2(t)}\" width=\"{Util.R2(r - l)}\" height=\"{Util.R2(b - t)}\" fill=\"#8883\"/><text x=\"{Util.R2(l + 8)}\" y=\"{Util.R2(t + 32)}\" font-size=\"24\" fill=\"#c92a2a\">缺少图片 {Util.Esc(name ?? "")}</text>";

            var u = await src.ReadAsync(key);
            string href = $"data:{Util.Mime(u)};base64,{Util.B64(u)}";
            return $"<image x=\"{Util.R2(l)}\" y=\"{Util.R2(t)}\" width=\"{Util.R2(r - l)}\" height=\"{Util.R2(b - t)}\" preserveAspectRatio=\"none\" xlink:href=\"{href}\"/>";
        }

        private static string GeomSvg(GeometryElement ge)
        {
            float l = ge.Rect[0], t = ge.Rect[1], r = ge.Rect[2], b = ge.Rect[3];
            string a = $"fill=\"none\" stroke=\"{ge.Color}\" stroke-opacity=\"{Util.R2(ge.Opacity)}\" stroke-width=\"{Util.R2(ge.StrokeWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";
            if (ge.GeoType == 1 && ge.Points.Count > 0)
            {
                var pts = string.Join(" ", ge.Points.Select(p => $"{Util.R2(p.X)},{Util.R2(p.Y)}"));
                return $"<polygon points=\"{pts}\" {a}/>";
            }
            float L = l, T = t, R = r, B = b;
            if (ge.Points.Count >= 2)
            {
                L = ge.Points.Min(p => p.X); R = ge.Points.Max(p => p.X);
                T = ge.Points.Min(p => p.Y); B = ge.Points.Max(p => p.Y);
            }
            return $"<ellipse cx=\"{Util.R2((L + R) / 2)}\" cy=\"{Util.R2((T + B) / 2)}\" rx=\"{Util.R2((R - L) / 2)}\" ry=\"{Util.R2((B - T) / 2)}\" {a}/>";
        }
    }
}
