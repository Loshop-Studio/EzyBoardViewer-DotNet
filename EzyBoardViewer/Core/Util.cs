using System;
using System.Text;

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 复刻 board.js 的颜色 / 矩阵 / 矩形 / 转义 / mime 等工具。
    /// </summary>
    public static class Util
    {
        /// <summary>颜色 int（0xAARRGGBB）→ (hex "#RRGGBB", alpha 0..1)。</summary>
        public static (string Hex, float Alpha) Col(int c)
        {
            int rgb = c & 0xFFFFFF;
            string hex = "#" + rgb.ToString("X6").ToLower();
            float a = ((c >> 24) & 255) / 255f;
            return (hex, a);
        }

        /// <summary>Android Matrix 消息 → SVG matrix(a b c d e f)。无有效缩放时返回 null。</summary>
        public static float[] Mx(PbMessage m)
        {
            if (m == null) return null;
            float sx = m.F(2), sy = m.F(6);
            if (sx == 0 && sy == 0) return null;
            return new[] { sx, m.F(5), m.F(3), sy, m.F(4), m.F(7) };
        }

        /// <summary>outRect（field3）→ [l, t, r, b]。</summary>
        public static float[] Rc(PbMessage g)
        {
            var r = g.M(3);
            if (r == null) return new[] { 0f, 0f, 0f, 0f };
            return new[] { r.F(1), r.F(2), r.F(3), r.F(4) };
        }

        /// <summary>SVG 数值保留两位小数（与 board.js r2 一致）。</summary>
        public static string R2(float v) => v.ToString("0.##");

        public static string R2(float[] a) => string.Join(" ", a.Select(R2));

        /// <summary>XML / SVG 文本转义。</summary>
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>按文件头判断图片 mime（与 board.js mime 一致）。</summary>
        public static string Mime(byte[] u)
        {
            if (u.Length > 0 && u[0] == 0x89) return "image/png";
            if (u.Length > 0 && u[0] == 0xFF) return "image/jpeg";
            if (u.Length > 0 && u[0] == 0x47) return "image/gif";
            if (u.Length > 8 && u[8] == 0x57) return "image/webp";
            return "image/png";
        }

        public static string B64(byte[] u) => Convert.ToBase64String(u);

        /// <summary>stroke 的 SVG 描边属性（颜色 / 线宽 / 线端 / 虚线）。</summary>
        public static string StrokeAttr(PbMessage pa)
        {
            var (k, o) = Col(pa.I(2, -16777216));
            float sp = pa.F(3), il = pa.F(4);
            string dash = sp > 0 ? $" stroke-dasharray=\"{R2(il == 0 ? 1 : il)} {R2(sp)}\"" : "";
            return $"fill=\"none\" stroke=\"{k}\" stroke-opacity=\"{R2(o)}\" stroke-width=\"{R2(pa.F(1, 2))}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"{dash}";
        }
    }
}
