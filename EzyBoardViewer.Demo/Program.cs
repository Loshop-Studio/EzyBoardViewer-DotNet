using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using EzyBoardViewer;
using EzyBoardViewer.Core;
using EzyBoardViewer.Models;

static class Pb
{
    static byte[] Varint(ulong v)
    {
        var l = new List<byte>();
        do { byte b = (byte)(v & 127); v >>= 7; if (v != 0) b |= 128; l.Add(b); } while (v != 0);
        return l.ToArray();
    }
    static byte[] F(int field, byte wire, byte[] val)
    {
        var tag = Varint((ulong)((field << 3) | wire));
        var outp = new List<byte>();
        outp.AddRange(tag);
        if (wire == 2) outp.AddRange(Varint((ulong)val.Length));
        outp.AddRange(val);
        return outp.ToArray();
    }
    public static byte[] V(int field, long val) => F(field, 0, Varint((ulong)val)); // 注意：负数按 64 位补码
    public static byte[] Fl(int field, float f) => F(field, 5, BitConverter.GetBytes(f));
    public static byte[] S(int field, string s) => F(field, 2, Encoding.UTF8.GetBytes(s));
    public static byte[] M(int field, byte[] msg) => F(field, 2, msg);
}

static class Program
{
    static byte[] Rect(float l, float t, float r, float b) => Pb.Fl(1, l).Concat(Pb.Fl(2, t)).Concat(Pb.Fl(3, r)).Concat(Pb.Fl(4, b)).ToArray();

    static byte[] Paint(float w, int color) => Pb.Fl(1, w).Concat(Pb.V(2, color)).ToArray();

    static byte[] Point(float x, float y) => Pb.Fl(1, x).Concat(Pb.Fl(2, y)).ToArray();

    static void Main()
    {
        // 构造一条手绘笔迹（内联点 field11）
        var pts = new[] { Point(10, 10), Point(50, 80), Point(120, 40) };
        var stroke = Pb.S(1, "s1")
            .Concat(Pb.V(2, 3))                       // type = STROKE
            .Concat(Pb.M(3, Rect(0, 0, 200, 100)))    // 包围盒
            .Concat(pts.Select(p => Pb.M(11, p)).SelectMany(x => x).ToArray())  // 内联点
            .Concat(Pb.M(9, Paint(4f, unchecked((int)0xFF000000))))   // 黑色描边
            .ToArray();

        // ROOT 节点（type=0），包含上面的笔迹作为子节点（field5）
        var root = Pb.V(2, 0).Concat(Pb.M(5, stroke)).ToArray();

        // snapshot.bin = GraphSnapshot{ GraphSnapshot field2 = root }
        var snapshot = Pb.M(2, root);

        // header.bin = { width=1080, height=1920 }
        var header = Pb.V(2, 1080).Concat(Pb.V(3, 1920)).ToArray();

        // 打包成 zip：dir = "p1/"，含 p1/header.bin 与 p1/snapshot.bin
        byte[] zip;
        using (var ms = new MemoryStream())
        {
            using (var za = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                void Add(string name, byte[] data)
                {
                    var e = za.CreateEntry(name);
                    using var s = e.Open(); s.Write(data, 0, data.Length);
                }
                Add("p1/header.bin", header);
                Add("p1/snapshot.bin", snapshot);
            }
            zip = ms.ToArray();
        }

        var pkg = BoardViewer.OpenZipAsync(zip).GetAwaiter().GetResult();
        Console.WriteLine($"IsRecord = {pkg.IsRecord}, IsV2 = {pkg.IsV2}, Pages = {pkg.Pages.Count}");
        var page = pkg.Pages[0];
        Console.WriteLine($"Page: {page.Width}x{page.Height}, bg={page.Background}, Elements={page.Elements.Count}");
        foreach (var el in page.Elements)
            Console.WriteLine($"  Element: {el.Kind} pts={((StrokeElement)el).Points.Count}");
        var svg = pkg.ExportToSvgAsync(0).GetAwaiter().GetResult();
        Console.WriteLine("---- SVG (前 600 字) ----");
        Console.WriteLine(svg.Length > 600 ? svg.Substring(0, 600) : svg);

        var pdfPath = Path.Combine(Path.GetTempPath(), "ezy_demo.pdf");
        pkg.ExportToPdfAsync(0, pdfPath).GetAwaiter().GetResult();
        Console.WriteLine($"---- PDF ----");
        Console.WriteLine($"PDF 已写出: {pdfPath}  ({new FileInfo(pdfPath).Length} bytes)");
    }
}
