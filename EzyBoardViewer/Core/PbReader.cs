using System;
using System.Collections.Generic;
using System.Text;

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 一个 protobuf 字段的原始值。wire type 决定 Value 的类型：
    /// 0(varint) → long；5(fixed32) → uint(原始 4 字节)；1(64bit) → byte[8]；2(length-delimited) → byte[]。
    /// 与 EzyBoardViewer-Vue 的 pb() 完全对应。
    /// </summary>
    public readonly struct PbField
    {
        public PbField(byte wireType, object value) { WireType = wireType; Value = value; }
        public byte WireType { get; }
        public object Value { get; }
    }

    /// <summary>
    /// 通用 protobuf 解码器：按字段号读取，无需 .proto 文件。
    /// 复刻自 EzyBoardViewer-Vue/src/core/board.js 的 pb()。
    /// </summary>
    public sealed class PbMessage
    {
        private readonly Dictionary<int, List<PbField>> _fields = new();

        public static PbMessage Parse(ReadOnlySpan<byte> buf)
        {
            var m = new PbMessage();
            int p = 0;
            while (p < buf.Length)
            {
                ulong key = ReadVarint(buf, ref p);
                int field = (int)(key >> 3);
                int wt = (int)(key & 7);
                object v;
                if (wt == 0)
                {
                    ulong val = ReadVarint(buf, ref p);
                    v = (long)val; // asIntN(64)
                }
                else if (wt == 5)
                {
                    var b4 = new byte[4];
                    buf.Slice(p, 4).CopyTo(b4.AsSpan());
                    p += 4;
                    v = BitConverter.ToUInt32(b4, 0);
                }
                else if (wt == 1)
                {
                    var b8 = new byte[8];
                    buf.Slice(p, 8).CopyTo(b8.AsSpan());
                    p += 8;
                    v = b8;
                }
                else if (wt == 2)
                {
                    int l = (int)ReadVarint(buf, ref p);
                    var sub = new byte[l];
                    buf.Slice(p, l).CopyTo(sub.AsSpan());
                    p += l;
                    v = sub;
                }
                else
                {
                    throw new InvalidOperationException($"无法解析 protobuf（wire type {wt}），文件可能不是该格式");
                }

                if (!m._fields.TryGetValue(field, out var list))
                {
                    list = new List<PbField>();
                    m._fields[field] = list;
                }
                list.Add(new PbField((byte)wt, v));
            }
            return m;
        }

        private static ulong ReadVarint(ReadOnlySpan<byte> buf, ref int p)
        {
            ulong r = 0;
            int s = 0;
            byte b;
            do
            {
                b = buf[p++];
                r |= (ulong)(b & 127) << s;
                s += 7;
            } while ((b & 128) != 0);
            return r;
        }

        private PbField? First(int n)
        {
            return _fields.TryGetValue(n, out var l) && l.Count > 0 ? l[0] : null;
        }

        /// <summary>varint 当 int32 读（asIntN(32)）。</summary>
        public int I(int n, int d = 0)
        {
            var e = First(n);
            return e.HasValue && e.Value.WireType == 0 ? (int)(long)e.Value.Value : d;
        }

        /// <summary>varint 当 int64 读（asIntN(64)）。</summary>
        public long L(int n, long d = 0)
        {
            var e = First(n);
            return e.HasValue && e.Value.WireType == 0 ? (long)e.Value.Value : d;
        }

        /// <summary>fixed32 当 float 读。</summary>
        public float F(int n, float d = 0)
        {
            var e = First(n);
            if (e.HasValue && e.Value.WireType == 5)
            {
                uint u = (uint)e.Value.Value;
                return BitConverter.ToSingle(BitConverter.GetBytes(u), 0);
            }
            return d;
        }

        /// <summary>length-delimited 当 UTF-8 字符串读。</summary>
        public string S(int n)
        {
            var e = First(n);
            if (e.HasValue && e.Value.WireType == 2)
            {
                var bytes = (byte[])e.Value.Value;
                return Encoding.UTF8.GetString(bytes);
            }
            return "";
        }

        /// <summary>length-delimited 当嵌套消息读。</summary>
        public PbMessage M(int n)
        {
            var e = First(n);
            if (e.HasValue && e.Value.WireType == 2)
            {
                var bytes = (byte[])e.Value.Value;
                return Parse(bytes);
            }
            return null;
        }

        /// <summary>repeated 嵌套消息。</summary>
        public List<PbMessage> A(int n)
        {
            var list = new List<PbMessage>();
            if (_fields.TryGetValue(n, out var l))
            {
                foreach (var f in l)
                    if (f.WireType == 2)
                        list.Add(Parse((byte[])f.Value));
            }
            return list;
        }

        public int Count(int n) => _fields.TryGetValue(n, out var l) ? l.Count : 0;
    }
}
