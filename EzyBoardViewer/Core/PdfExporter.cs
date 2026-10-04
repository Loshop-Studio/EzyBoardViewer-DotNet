using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using EzyBoardViewer.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 把 Page（先经 SvgRenderer 得到受控 SVG 子集）矢量重绘为 PDF。
    /// 与 svgToPdf.ts 对应：背景 / 背景线 / 笔迹中点二次贝塞尔 / 文本 / 图片，均矢量绘制。
    /// 中文字体需调用方提供 .ttf（如 HarmonyOS Sans SC），否则拉丁字符用内置 Helvetica、中文会缺字。
    /// </summary>
    public static class PdfExporter
    {
        public static async Task ExportPagesAsync(IReadOnlyList<Page> pages, IBoardSource src, Stream outStream, string fontPath = null)
        {
            using var doc = new PdfDocument();
            foreach (var page in pages)
            {
                var svg = await SvgRenderer.RenderAsync(page, src);
                var pdfPage = doc.AddPage();
                pdfPage.Width = new XUnit(page.Width, XGraphicsUnit.Point);
                pdfPage.Height = new XUnit(page.Height, XGraphicsUnit.Point);
                var g = XGraphics.FromPdfPage(pdfPage);
                DrawSvg(g, page.Height, svg, fontPath);
            }
            doc.Save(outStream);
        }

        private static void DrawSvg(XGraphics g, double pageH, string svg, string fontPath)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using var r = XmlReader.Create(new StringReader(svg), settings);
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                switch (r.Name)
                {
                    case "rect": DrawRect(g, pageH, r); break;
                    case "path": DrawPath(g, pageH, r); break;
                    case "circle": DrawCircle(g, pageH, r); break;
                    case "ellipse": DrawEllipse(g, pageH, r); break;
                    case "polygon": DrawPolygon(g, pageH, r); break;
                    case "image": DrawImage(g, pageH, r); break;
                    case "text": DrawText(g, pageH, r, fontPath); break;
                }
            }
        }

        private static double Flip(double pageH, double y) => pageH - y;

        private static XColor ParseColor(string hex, double alpha)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#' || hex.Length < 7)
                return XColor.FromArgb((int)(alpha * 255), 0, 0, 0);
            byte r = byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber);
            byte gg = byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber);
            return XColor.FromArgb((int)(alpha * 255), r, gg, b);
        }

        private static void DrawRect(XGraphics g, double pageH, XmlReader r)
        {
            double x = ParseD(r.GetAttribute("x")), y = ParseD(r.GetAttribute("y"));
            double w = ParseD(r.GetAttribute("width")), h = ParseD(r.GetAttribute("height"));
            var (hex, a) = ReadColor(r);
            g.DrawRectangle(new XSolidBrush(ParseColor(hex, a)), x, Flip(pageH, y + h), w, h);
        }

        private static void DrawCircle(XGraphics g, double pageH, XmlReader r)
        {
            double cx = ParseD(r.GetAttribute("cx")), cy = ParseD(r.GetAttribute("cy")), rad = ParseD(r.GetAttribute("r"));
            var (hex, a) = ReadColor(r);
            g.DrawEllipse(new XSolidBrush(ParseColor(hex, a)), cx - rad, Flip(pageH, cy) - rad, rad * 2, rad * 2);
        }

        private static void DrawEllipse(XGraphics g, double pageH, XmlReader r)
        {
            double cx = ParseD(r.GetAttribute("cx")), cy = ParseD(r.GetAttribute("cy"));
            double rx = ParseD(r.GetAttribute("rx")), ry = ParseD(r.GetAttribute("ry"));
            var (hex, a) = ReadColor(r);
            var pen = new XPen(ParseColor(hex, a), 1);
            g.DrawEllipse(pen, cx - rx, Flip(pageH, cy) - ry, rx * 2, ry * 2);
        }

        private static void DrawPolygon(XGraphics g, double pageH, XmlReader r)
        {
            var (hex, a) = ReadColor(r);
            var pen = new XPen(ParseColor(hex, a), 1);
            ApplyDash(pen, r);
            var nums = TokenizeNumbers(r.GetAttribute("points") ?? "");
            if (nums.Count < 4) return;
            var path = new XGraphicsPath();
            path.StartFigure();
            path.AddLine(nums[0], Flip(pageH, nums[1]), nums[2], Flip(pageH, nums[3]));
            for (int i = 4; i + 1 < nums.Count; i += 2)
                path.AddLine(nums[i], Flip(pageH, nums[i + 1]), nums[i], Flip(pageH, nums[i + 1]));
            path.CloseFigure();
            g.DrawPath(pen, path);
        }

        private static void DrawPath(XGraphics g, double pageH, XmlReader r)
        {
            var (hex, a) = ReadColor(r);
            double width = ParseD(r.GetAttribute("stroke-width"), 1);
            var pen = new XPen(ParseColor(hex, a), width);
            ApplyDash(pen, r);
            g.DrawPath(pen, BuildPath(r.GetAttribute("d") ?? "", pageH));
        }

        private static void DrawImage(XGraphics g, double pageH, XmlReader r)
        {
            double x = ParseD(r.GetAttribute("x")), y = ParseD(r.GetAttribute("y"));
            double w = ParseD(r.GetAttribute("width")), h = ParseD(r.GetAttribute("height"));
            var href = r.GetAttribute("xlink:href") ?? r.GetAttribute("href");
            if (href == null || !href.StartsWith("data:")) return;
            var comma = href.IndexOf(",", StringComparison.Ordinal);
            if (comma < 0) return;
            var bytes = Convert.FromBase64String(href.Substring(comma + 1));
            using var ms = new MemoryStream(bytes);
            var img = XImage.FromStream(ms);
            g.DrawImage(img, x, Flip(pageH, y + h), w, h);
        }

        private static void DrawText(XGraphics g, double pageH, XmlReader r, string fontPath)
        {
            double size = ParseD(r.GetAttribute("font-size"), 40);
            var (hex, a) = ReadColor(r);
            var brush = new XSolidBrush(ParseColor(hex, a));
            var font = new XFont("Helvetica", size);
            if (r.IsEmptyElement) return;
            var inner = r.ReadInnerXml();
            using var sr = XmlReader.Create(new StringReader(inner), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            while (sr.Read())
            {
                if (sr.NodeType == XmlNodeType.Element && sr.Name == "tspan")
                {
                    double x = ParseD(sr.GetAttribute("x")), y = ParseD(sr.GetAttribute("y"));
                    var text = sr.ReadElementContentAsString();
                    g.DrawString(text, font, brush, x, Flip(pageH, y));
                }
            }
        }

        private static (string Hex, double Alpha) ReadColor(XmlReader r)
        {
            var stroke = r.GetAttribute("stroke") ?? r.GetAttribute("fill") ?? "#000000";
            double op = ParseD(r.GetAttribute("stroke-opacity") ?? r.GetAttribute("fill-opacity"), 1);
            return (stroke, op);
        }

        private static void ApplyDash(XPen pen, XmlReader r)
        {
            var da = r.GetAttribute("stroke-dasharray");
            if (string.IsNullOrEmpty(da)) return;
            var nums = TokenizeNumbers(da);
            if (nums.Count >= 2) { pen.DashPattern = new[] { nums[0], nums[1] }; pen.DashStyle = XDashStyle.Custom; }
        }

        private static double ParseD(string s, double d = 0)
        {
            if (string.IsNullOrEmpty(s)) return d;
            return double.Parse(s, CultureInfo.InvariantCulture);
        }

        private static XGraphicsPath BuildPath(string d, double pageH)
        {
            var path = new XGraphicsPath();
            var tokens = TokenizePath(d);
            double cx = 0, cy = 0;
            int i = 0;
            char cmd = 'M';
            while (i < tokens.Count)
            {
                if (char.IsLetter(tokens[i][0])) { cmd = tokens[i][0]; i++; }
                if (i >= tokens.Count) break;
                switch (cmd)
                {
                    case 'M':
                        { double x = ParseD(tokens[i++]), y = ParseD(tokens[i++]); cx = x; cy = y; path.StartFigure(); path.AddLine(x, Flip(pageH, y), x, Flip(pageH, y)); break; }
                    case 'L':
                        { double x = ParseD(tokens[i++]), y = ParseD(tokens[i++]); path.AddLine(cx, Flip(pageH, cy), x, Flip(pageH, y)); cx = x; cy = y; break; }
                    case 'H':
                        { double x = ParseD(tokens[i++]); path.AddLine(cx, Flip(pageH, cy), x, Flip(pageH, cy)); cx = x; break; }
                    case 'V':
                        { double y = ParseD(tokens[i++]); path.AddLine(cx, Flip(pageH, cy), cx, Flip(pageH, y)); cy = y; break; }
                    case 'Q':
                        {
                            double cxp = ParseD(tokens[i++]), cyp = ParseD(tokens[i++]), x = ParseD(tokens[i++]), y = ParseD(tokens[i++]);
                            double c1x = cx + 2.0 / 3 * (cxp - cx), c1y = cyp - 2.0 / 3 * (cyp - cy);
                            double c2x = x + 2.0 / 3 * (cxp - x), c2y = y - 2.0 / 3 * (cyp - y);
                            path.AddBezier(cx, Flip(pageH, cy), c1x, Flip(pageH, c1y), c2x, Flip(pageH, c2y), x, Flip(pageH, y));
                            cx = x; cy = y; break;
                        }
                    case 'Z': path.CloseFigure(); break;
                }
            }
            return path;
        }

        private static List<string> TokenizePath(string d)
        {
            var outp = new List<string>();
            int i = 0;
            while (i < d.Length)
            {
                char c = d[i];
                if (char.IsLetter(c)) { outp.Add(c.ToString()); i++; }
                else if (char.IsDigit(c) || c == '.' || c == '-' || c == '+' || c == 'e' || c == 'E')
                {
                    int j = i;
                    if (d[j] == '-' || d[j] == '+') j++;
                    while (j < d.Length && (char.IsDigit(d[j]) || d[j] == '.' || d[j] == 'e' || d[j] == 'E' || (d[j] == '-' && j > i && (d[j - 1] == 'e' || d[j - 1] == 'E'))))
                        j++;
                    outp.Add(d.Substring(i, j - i)); i = j;
                }
                else i++;
            }
            return outp;
        }

        private static List<double> TokenizeNumbers(string s)
        {
            var outp = new List<double>();
            foreach (var t in s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) outp.Add(v);
            return outp;
        }
    }
}
