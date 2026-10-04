using System;
using System.Collections.Generic;

namespace EzyBoardViewer.Core
{
    /// <summary>protubuf 编码（与 board.js / noteVfs.ts 的 pbVarint/pbTag/pbF32/pbI32/pbMsg 对齐）。</summary>
    internal static class PbWriter
    {
        public static byte[] Varint(ulong v)
        {
            var l = new List<byte>();
            do { byte b = (byte)(v & 127); v >>= 7; if (v != 0) b |= 128; l.Add(b); } while (v != 0);
            return l.ToArray();
        }

        public static byte[] Tag(int field, int wire) => Varint((ulong)((field << 3) | wire));

        /// <summary>varint（int32 按无符号编码；负数变 10 字节，与 board.js 的 I() 读取一致）。</summary>
        public static byte[] I32(int field, int val) => Tag(field, 0).Concat(Varint((ulong)(uint)val)).ToArray();

        /// <summary>fixed32 float（wire 5）。</summary>
        public static byte[] F32(int field, float f) => Tag(field, 5).Concat(BitConverter.GetBytes(f)).ToArray();

        public static byte[] Msg(int field, byte[] body) => Tag(field, 2).Concat(Varint((ulong)body.Length)).Concat(body).ToArray();
    }
}
