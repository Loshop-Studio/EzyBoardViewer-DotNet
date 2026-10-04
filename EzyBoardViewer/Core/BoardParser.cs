using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EzyBoardViewer.Models;

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 把 IBoardSource（zip / 笔记 VFS）解析成 Page 模型。
    /// 复刻 board.js 的 pageSvg / convert / pagesOf / detect，但产出对象而非 SVG 字符串。
    /// </summary>
    public sealed class BoardParser
    {
        private readonly IBoardSource _src;
        public BoardParser(IBoardSource src) => _src = src;

        public async Task<List<(string dir, int idx)>> PagesOfAsync()
        {
            var rp = _src.Names.FirstOrDefault(n => n.Split('/').Last() == "page_router.bin");
            if (rp != null)
            {
                var root = rp.Substring(0, rp.LastIndexOf('/') + 1);
                var rpm = PbMessage.Parse(await _src.ReadAsync(rp));
                return rpm.A(1)
                    .Select((m, i) => (root + m.S(1) + "/", m.I(2, i)))
                    .OrderBy(x => x.Item2)
                    .ToList();
            }
            return _src.Names
                .Where(n => n.EndsWith("snapshot.bin"))
                .Select(n => (n.Substring(0, n.Length - 12), 0))
                .ToList();
        }

        public async Task<bool> DetectAsync()
        {
            var audioRe = new Regex(@"\.(mp3|aac|m4a|wav|ogg|opus|amr|flac)$", RegexOptions.IgnoreCase);
            bool reason = _src.Names.Any(n => audioRe.IsMatch(n));
            var pages = await PagesOfAsync();
            var events = new HashSet<int>();
            long dur = 0;
            int audioRecord = 0;
            foreach (var (dir, _) in pages)
            {
                var cf = _src.Names.FirstOrDefault(n => n.StartsWith(dir) && n.EndsWith("_command.bin"));
                if (cf != null)
                    foreach (var m in PbMessage.Parse(await _src.ReadAsync(cf)).A(1))
                        events.Add(m.I(4));
                var hb = await _src.ReadAsync(dir + "header.bin");
                if (hb != null) dur = Math.Max(dur, PbMessage.Parse(hb).L(6));
                var rb = await _src.ReadAsync(dir + "router.bin");
                if (rb != null && PbMessage.Parse(rb).Count(2) > 0) audioRecord++;
            }
            return reason || events.Contains(6) || events.Contains(10) || events.Contains(11) || dur > 0 || audioRecord > 0;
        }

        public async Task<(List<Page> pages, bool isRecord, bool isV2)> ParseAllAsync()
        {
            bool isV2 = _src.Names.Any(n =>
            {
                var l = n.ToLower();
                return l.EndsWith(".mdb") && !l.Contains("lock");
            });
            var dirs = await PagesOfAsync();
            var pages = new List<Page>();
            foreach (var (dir, _) in dirs)
            {
                var p = await ParsePageAsync(dir, isV2);
                if (p != null) pages.Add(p);
            }
            var isRecord = await DetectAsync();
            return (pages, isRecord, isV2);
        }

        public async Task<Page> ParsePageAsync(string dir, bool isV2)
        {
            var hb = await _src.ReadAsync(dir + "header.bin");
            var sb = await _src.ReadAsync(dir + "snapshot.bin");
            if (hb == null || sb == null) return null;

            var h = PbMessage.Parse(hb);
            var sn = PbMessage.Parse(sb);
            int W = h.I(2, 1080), H = h.I(3, 1920);
            var page = new Page { Width = W, Height = H, IsV1 = !isV2, IsV2 = isV2 };

            int bgc = h.Count(11) > 0 ? h.I(11) : (h.Count(10) > 0 ? h.I(10) : -1);
            page.Background = Util.Col(bgc).Hex;

            var lc = h.M(14) ?? h.M(13);
            if (lc != null)
                page.BackgroundLine = new BackgroundLine
                {
                    Color = lc.I(1),
                    Spacing = lc.F(2),
                    Type = lc.I(3),
                    Width = lc.F(4) == 0 ? 1 : lc.F(4)
                };

            var cache = new Dictionary<string, PbMessage>();
            async Task<PbMessage> Src(string n)
            {
                if (string.IsNullOrEmpty(n)) return null;
                var bn = n.Split('/').Last();
                if (cache.TryGetValue(bn, out var cached)) return cached;
                var b = await _src.ReadAsync(dir + bn);
                var m = b == null ? new PbMessage() : PbMessage.Parse(b);
                cache[bn] = m;
                return m;
            }
            async Task<PbMessage> Item(string srcName, int sid)
            {
                if (string.IsNullOrEmpty(srcName)) return null;
                var sm = await Src(srcName);
                var list = sm.A(1);
                return sid >= 0 && sid < list.Count ? list[sid] : null;
            }

            async Task<StrokeElement> Stroke(PbMessage g)
            {
                List<(float X, float Y)> pts = null;
                PbMessage pa = null;
                var ti = await Item(g.S(6), g.I(7));
                if (ti != null) { pts = ti.A(1).Select(e => (e.F(3), e.F(4))).ToList(); pa = ti.M(2); }
                if (pts == null || pts.Count == 0) { pts = g.A(11).Select(e => (e.F(1), e.F(2))).ToList(); pa = g.M(9); }
                if (pts == null || pts.Count == 0) return null;
                var s = new StrokeElement { Rect = Util.Rc(g), IsBeeline = g.I(2) == 4 };
                if (pa != null)
                {
                    var (k, o) = Util.Col(pa.I(2, -16777216));
                    s.Color = k; s.Opacity = o; s.StrokeWidth = pa.F(1, 2);
                    float sp = pa.F(3), il = pa.F(4);
                    if (sp > 0) s.DashArray = new[] { il == 0 ? 1 : il, sp };
                }
                s.Points = pts;
                return s;
            }

            async Task<ImageElement> Image(PbMessage g)
            {
                var fi = await Item(g.S(6), g.I(7));
                var name = fi != null && !string.IsNullOrEmpty(fi.S(1)) ? fi.S(1) : g.S(12);
                return new ImageElement { Rect = Util.Rc(g), ImageName = name };
            }

            async Task<TextElement> Text(PbMessage g)
            {
                var ti = await Item(g.S(6), g.I(7));
                var c = ti?.S(1) ?? "";
                var pa = ti?.M(2) ?? g.M(9);
                if (string.IsNullOrEmpty(c)) c = g.S(10);
                if (string.IsNullOrEmpty(c)) return null;
                var t = new TextElement { Rect = Util.Rc(g), Text = c };
                if (pa != null)
                {
                    var (k, o) = Util.Col(pa.I(2, -16777216));
                    t.Color = k; t.Opacity = o;
                    t.FontSize = pa.F(6, 40) == 0 ? 40 : pa.F(6, 40);
                }
                return t;
            }

            async Task<GeometryElement> Geom(PbMessage g)
            {
                var gi = await Item(g.S(6), g.I(7));
                if (gi == null) return null;
                var ge = new GeometryElement { Rect = Util.Rc(g), GeoType = gi.I(1) };
                ge.Points = gi.A(2).Select(e => (e.F(1), e.F(2))).ToList();
                var pa = gi.M(3);
                if (pa != null)
                {
                    var (k, o) = Util.Col(pa.I(2, -16777216));
                    ge.Color = k; ge.Opacity = o; ge.StrokeWidth = pa.F(1, 2);
                }
                return ge;
            }

            async Task<Element> Node(PbMessage g)
            {
                Element el = g.I(2) switch
                {
                    2 => await Image(g),
                    3 => await Stroke(g),
                    4 => await Stroke(g),
                    5 => await Text(g),
                    6 => await Geom(g),
                    _ => new GroupElement()
                };
                if (el == null) el = new GroupElement();
                el.Transform = Util.Mx(g.M(4));
                el.Rect = Util.Rc(g);
                foreach (var c in g.A(5))
                    el.Children.Add(await Node(c));
                return el;
            }

            var root = await Node(sn.M(2) ?? new PbMessage());
            page.Elements = root.Children;
            return page;
        }
    }
}
