using System;

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 解析云笔记老格式 header.bin（protobuf 编码）：
    ///   field 11 wire 0 (varint)              = bgcolor           int32 0xAARRGGBB
    ///   field 13 wire 2 (length-delimited)     = 背景线配置（BgLines sub-message）：
    ///     f1 wire 0 = color                    int32 0xAARRGGBB
    ///     f2 wire 5 = spacing                  fixed32 float32
    ///     f3 wire 0 = cross                    varint (0=仅横线, 1=横竖交错网格)
    ///     f4 wire 5 = width                    fixed32 float32（可省略，老 Android 未写）
    ///
    /// 老格式笔记（早期 Android 端）没有 data.mdb，背景就放在 header.bin 里。
    /// 已知样本：0xfff6f0e9（米色底）/ 0xfff6f000（米黄底）。
    /// 与 EzyBoardViewer-Vue/src/core/pbHeader.js 的 readHeaderBlobBg 完全对应。
    /// </summary>
    public static class PbHeaderReader
    {
        /// <summary>header.bin 里读出的底色与背景线，二者均可能为 null。</summary>
        public sealed class HeaderBgResult
        {
            public int? bgColor;
            public MdbReader.BgLinesResult bgLines;
            public bool Hit => bgColor != null || bgLines != null;
        }

        /// <summary>读一个 protobuf varint（最多 10 字节 / 70 bit），返回无符号累加值。</summary>
        private static ulong ReadVarint(ReadOnlySpan<byte> u8, ref int p)
        {
            ulong v = 0;
            int shift = 0;
            for (;;)
            {
                if (p >= u8.Length) throw new FormatException("varint 越界 @" + p);
                byte b = u8[p++];
                v |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0) return v;
                shift += 7;
                if (shift > 70) throw new FormatException("varint 过长 @" + p);
            }
        }

        /// <summary>按 wire type 跳过当前字段，返回下一个偏移。</summary>
        private static int SkipField(ReadOnlySpan<byte> u8, int p, int wire)
        {
            if (wire == 0) { ReadVarint(u8, ref p); return p; }
            if (wire == 1) return p + 8;
            if (wire == 2) { ulong len = ReadVarint(u8, ref p); return p + (int)len; }
            if (wire == 5) return p + 4;
            throw new FormatException("未知 wire type: " + wire);
        }

        /// <summary>
        /// 从 header.bin 字节里读底色（f11）与背景线（f13）。字段缺失时对应项为 null。
        /// </summary>
        public static HeaderBgResult ReadHeaderBlobBg(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return new HeaderBgResult();
            var u8 = new ReadOnlySpan<byte>(bytes);
            int? bgColor = null;
            MdbReader.BgLinesResult bgLines = null;
            int p = 0;
            while (p < u8.Length)
            {
                ulong tag = ReadVarint(u8, ref p);
                int field = (int)(tag >> 3);
                int wire = (int)(tag & 7);
                if (field == 11 && wire == 0)
                {
                    ulong v = ReadVarint(u8, ref p);
                    bgColor = unchecked((int)v);
                }
                else if (field == 13 && wire == 2)
                {
                    ulong len = ReadVarint(u8, ref p);
                    int ln = (int)len;
                    if (ln < 0 || p + ln > u8.Length) break;
                    bgLines = ParseBgLines(u8.Slice(p, ln));
                    p += ln;
                }
                else
                {
                    p = SkipField(u8, p, wire);
                }
            }
            return new HeaderBgResult { bgColor = bgColor, bgLines = bgLines };
        }

        private static MdbReader.BgLinesResult ParseBgLines(ReadOnlySpan<byte> sub)
        {
            if (sub.Length == 0) return null;
            int? color = null;
            float? spacing = null;
            int cross = 0;
            float? width = null;
            int q = 0;
            while (q < sub.Length)
            {
                ulong tag = ReadVarint(sub, ref q);
                int field = (int)(tag >> 3);
                int wire = (int)(tag & 7);
                if (wire == 0)
                {
                    ulong v = ReadVarint(sub, ref q);
                    int n = unchecked((int)v);
                    if (field == 1) color = n;
                    else if (field == 3) cross = n;
                }
                else if (wire == 5)
                {
                    if (q + 4 > sub.Length) break;
                    float f = BitConverter.ToSingle(new[] { sub[q], sub[q + 1], sub[q + 2], sub[q + 3] }, 0);
                    if (field == 2) spacing = f;
                    else if (field == 4) width = f;
                    q += 4;
                }
                else
                {
                    q = SkipField(sub, q, wire);
                }
            }
            // 必要字段缺失视为无效（color 不能为 null，spacing 必须 > 0）
            if (color == null || spacing == null || !(spacing > 0)) return null;
            // 线宽缺省给 1：早期 Android 端编码时未写 f4，1 像素与 App 实际渲染线宽吻合
            return new MdbReader.BgLinesResult
            {
                color = color.Value,
                spacing = spacing.Value,
                width = width ?? 1,
                cross = cross != 0
            };
        }
    }
}
