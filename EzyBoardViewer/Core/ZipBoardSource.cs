using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 最小 zip 读取：从末尾 EOCD 找 central directory，支持 method 0(存)/8(deflate-raw)。
    /// 不支持 zip64。复刻 board.js 的 readZip()。
    /// </summary>
    public sealed class ZipBoardSource : IBoardSource
    {
        private readonly Dictionary<string, ZipEntry> _ents = new();
        private readonly byte[] _buf;

        public IReadOnlyList<string> Names { get; }

        private sealed class ZipEntry
        {
            public int Method;
            public int CompressedSize;
            public int LocalOffset;
        }

        public ZipBoardSource(byte[] zipBytes)
        {
            _buf = zipBytes;
            var u8 = zipBytes;
            int e = u8.Length - 22;
            while (e >= 0)
            {
                if (BitConverter.ToUInt32(u8, e) == 0x06054b50) break;
                e--;
            }
            if (e < 0) throw new InvalidOperationException("不是有效的 zip 文件");

            int n = BitConverter.ToUInt16(u8, e + 10);
            int p = BitConverter.ToInt32(u8, e + 16);
            var names = new List<string>();
            for (int i = 0; i < n && BitConverter.ToUInt32(u8, p) == 0x02014b50; i++)
            {
                int method = BitConverter.ToUInt16(u8, p + 10);
                int cs = BitConverter.ToInt32(u8, p + 20);
                int nl = BitConverter.ToUInt16(u8, p + 28);
                int el = BitConverter.ToUInt16(u8, p + 30);
                int cl = BitConverter.ToUInt16(u8, p + 32);
                int off = BitConverter.ToInt32(u8, p + 42);
                string name = Encoding.UTF8.GetString(u8, p + 46, nl);
                p += 46 + nl + el + cl;
                if (cs == -1 || off == -1)
                    throw new InvalidOperationException("暂不支持 zip64");
                if (!name.EndsWith("/"))
                {
                    _ents[name] = new ZipEntry { Method = method, CompressedSize = cs, LocalOffset = off };
                    names.Add(name);
                }
            }
            Names = names;
        }

        public Task<byte[]> ReadAsync(string name)
        {
            if (!_ents.TryGetValue(name, out var z))
                return Task.FromResult<byte[]>(null);

            int local = z.LocalOffset;
            int nameLen = BitConverter.ToUInt16(_buf, local + 26);
            int extraLen = BitConverter.ToUInt16(_buf, local + 28);
            int s = local + 30 + nameLen + extraLen;
            var d = new byte[z.CompressedSize];
            Array.Copy(_buf, s, d, 0, z.CompressedSize);

            if (z.Method == 0) return Task.FromResult(d);
            if (z.Method != 8) throw new InvalidOperationException($"不支持的压缩方式 {z.Method}");

            using var ms = new MemoryStream(d);
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);
            using var outms = new MemoryStream();
            ds.CopyTo(outms);
            return Task.FromResult(outms.ToArray());
        }
    }
}
