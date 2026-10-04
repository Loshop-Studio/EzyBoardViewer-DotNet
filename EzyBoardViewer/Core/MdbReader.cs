using System;
using System.Collections.Generic;

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 把新版云笔记页目录里的 page_mdb/data.mdb（LMDB + ObjectBox FlatBuffers）解析成 TouchSource protobuf 字节，
    /// 供 BoardParser 的 stroke() 直接消费。完整移植自 EzyBoardViewer-Vue/src/core/mdb.js。
    /// </summary>
    public static class MdbReader
    {
        private const int PAGE_SIZE = 4096;
        private const int P_LEAF = 0x02;
        private const int NODE_HDR = 8;
        private const int F_BIGDATA = 0x01;

        private const double MIN_MS = 1.5e12;
        private const double MAX_MS = 4e13;
        private const long MAX_SEGMENTS = 4096;

        /// <summary>FlatBuffers 只读访问（little-endian）。</summary>
        private sealed class Fb
        {
            private readonly byte[] b;
            public Fb(byte[] data) { b = data; }
            public int Len => b.Length;
            public ushort U16(int o) => (ushort)((b[o] & 0xFF) | ((b[o + 1] & 0xFF) << 8));
            public int I32(int o) => (b[o] & 0xFF) | ((b[o + 1] & 0xFF) << 8) | ((b[o + 2] & 0xFF) << 16) | ((b[o + 3] & 0xFF) << 24);
            public long I64(int o) => (long)((ulong)(uint)U32(o));
            public uint U32(int o) => (uint)((b[o] & 0xFF) | ((b[o + 1] & 0xFF) << 8) | ((b[o + 2] & 0xFF) << 16) | ((b[o + 3] & 0xFF) << 24));
            public float F32(int o) => BitConverter.ToSingle(new[] { b[o], b[o + 1], b[o + 2], b[o + 3] }, 0);
        }

        private sealed class FbTable { public int Pos; public int Vt; public int VtSize; }

        private static FbTable ReadTable(Fb dv, int pos)
        {
            if (pos < 0 || pos + 4 > dv.Len) return null;
            int vt = pos - dv.I32(pos);
            if (vt < 0 || vt + 4 > dv.Len) return null;
            int vtSize = dv.U16(vt);
            if (vtSize < 4 || vt + vtSize > dv.Len) return null;
            return new FbTable { Pos = pos, Vt = vt, VtSize = vtSize };
        }

        private static int FbField(Fb dv, FbTable t, int fieldId)
        {
            int slot = 4 + fieldId * 2;
            if (slot + 2 > t.VtSize) return -1;
            int off = dv.U16(t.Vt + slot);
            if (off == 0) return -1;
            int p = t.Pos + off;
            return p + 4 <= dv.Len ? p : -1;
        }

        private static FbTable RootTable(Fb dv)
        {
            if (dv.Len < 8) return null;
            return ReadTable(dv, (int)dv.U32(0));
        }

        /// <summary>遍历 LMDB leaf 页，返回全部 key/value。</summary>
        public static List<(byte[] key, byte[] val)> ReadMdbEntries(byte[] u8)
        {
            var outp = new List<(byte[] key, byte[] val)>();
            if (u8 == null || u8.Length < PAGE_SIZE) return outp;
            int pages = u8.Length / PAGE_SIZE;
            for (int p = 0; p < pages; p++)
            {
                int o = p * PAGE_SIZE;
                int flags = (u8[o + 10] & 0xFF) | ((u8[o + 11] & 0xFF) << 8);
                if ((flags & P_LEAF) != P_LEAF) continue;
                int lower = (u8[o + 12] & 0xFF) | ((u8[o + 13] & 0xFF) << 8);
                int upper = (u8[o + 14] & 0xFF) | ((u8[o + 15] & 0xFF) << 8);
                if (lower < 16 || upper < lower || upper > PAGE_SIZE) continue;
                int n = (lower - 16) >> 1;
                for (int k = 0; k < n; k++)
                {
                    int np = (u8[o + 16 + k * 2] & 0xFF) | ((u8[o + 16 + k * 2 + 1] & 0xFF) << 8);
                    if (np < 16 || np + NODE_HDR > PAGE_SIZE) continue;
                    int lo = (u8[np] & 0xFF) | ((u8[np + 1] & 0xFF) << 8);
                    int hi = (u8[np + 2] & 0xFF) | ((u8[np + 3] & 0xFF) << 8);
                    int nflags = (u8[np + 4] & 0xFF) | ((u8[np + 5] & 0xFF) << 8);
                    int ksz = (u8[np + 6] & 0xFF) | ((u8[np + 7] & 0xFF) << 8);
                    int dsz = lo | (hi << 16);
                    if ((nflags & F_BIGDATA) != 0) continue;
                    if (np + NODE_HDR + ksz + dsz > PAGE_SIZE) continue;
                    int ks = np + NODE_HDR;
                    var key = new byte[ksz]; Array.Copy(u8, ks, key, 0, ksz);
                    var val = new byte[dsz]; Array.Copy(u8, ks + ksz, val, 0, dsz);
                    outp.Add((key, val));
                }
            }
            return outp;
        }

        private sealed class TouchEvent { public long id; public float width; public int color; public float dashGap; public float dashLen; }
        private sealed class TouchInfo { public float x; public float y; public long time; public long eventId; }

        private static TouchEvent ParseTouchEvent(byte[] val)
        {
            if (val.Length < 16) return null;
            var dv = new Fb(val);
            FbTable t = RootTable(dv); if (t == null) return null;
            int pw = FbField(dv, t, 1), pc = FbField(dv, t, 2);
            if (pw < 0 || pc < 0) return null;
            float width = dv.F32(pw);
            if (!(width >= 0.5 && width <= 200)) return null;
            int pid = FbField(dv, t, 0);
            if (pid >= 0 && pid + 8 > dv.Len) return null;
            int p3 = FbField(dv, t, 3), p4 = FbField(dv, t, 4);
            float dashGap = p3 >= 0 ? dv.F32(p3) : 0;
            float dashLen = p4 >= 0 ? dv.F32(p4) : 0;
            return new TouchEvent
            {
                id = pid >= 0 ? dv.I64(pid) : 0,
                width = width,
                color = dv.I32(pc),
                dashGap = float.IsFinite(dashGap) && dashGap > 0 ? dashGap : 0,
                dashLen = float.IsFinite(dashLen) && dashLen > 0 ? dashLen : 0
            };
        }

        private static TouchInfo ParseTouchInfo(byte[] val)
        {
            if (val.Length < 24) return null;
            var dv = new Fb(val);
            FbTable t = RootTable(dv); if (t == null) return null;
            int pt = FbField(dv, t, 1), px = FbField(dv, t, 2), py = FbField(dv, t, 3), pe = FbField(dv, t, 5);
            if (pt < 0 || px < 0 || py < 0 || pe < 0) return null;
            if (pt + 8 > dv.Len || pe + 8 > dv.Len) return null;
            long time = dv.I64(pt);
            if (!(time >= MIN_MS && time <= MAX_MS)) return null;
            long eventId = dv.I64(pe);
            if (!(eventId > 0 && eventId <= MAX_SEGMENTS)) return null;
            float x = dv.F32(px), y = dv.F32(py);
            if (!float.IsFinite(x) || !float.IsFinite(y)) return null;
            if (Math.Abs(x) > 1e5 || Math.Abs(y) > 1e5) return null;
            return new TouchInfo { x = x, y = y, time = time, eventId = eventId };
        }

        private static byte[] EncodeTouchSource(List<(TouchEvent seg, List<TouchInfo> pts)> segs)
        {
            var outp = new List<byte>();
            foreach (var (seg, pts) in segs)
            {
                if (pts == null || pts.Count == 0) continue;
                var body = new List<byte>();
                foreach (var pt in pts)
                    body.AddRange(PbWriter.Msg(1, PbWriter.F32(3, pt.x).Concat(PbWriter.F32(4, pt.y)).ToArray()));
                var paint = new List<byte>();
                paint.AddRange(PbWriter.F32(1, seg.width));
                paint.AddRange(PbWriter.I32(2, seg.color));
                if (seg.dashGap > 0)
                    paint.AddRange(PbWriter.F32(3, seg.dashGap).Concat(PbWriter.F32(4, seg.dashLen > 0 ? seg.dashLen : 1)));
                body.AddRange(PbWriter.Msg(2, paint.ToArray()));
                outp.AddRange(PbWriter.Msg(1, body.ToArray()));
            }
            return outp.ToArray();
        }

        /// <summary>解析一页 data.mdb 合成 TouchSource 字节（含采样点）。返回 null 表示没找到笔触数据。</summary>
        public static byte[] BuildTouchSource(byte[] mdb)
        {
            var entries = ReadMdbEntries(mdb);
            if (entries.Count == 0) return null;
            var points = new List<TouchInfo>();
            var cands = new Dictionary<uint, Dictionary<long, TouchEvent>>();
            foreach (var (key, val) in entries)
            {
                if (key.Length != 8 || val.Length < 16) continue;
                var info = ParseTouchInfo(val);
                if (info != null) { points.Add(info); continue; }
                var ev = ParseTouchEvent(val);
                if (ev == null) continue;
                uint prefix = (uint)((key[0] << 24) | (key[1] << 16) | (key[2] << 8) | key[3]) >>> 0;
                if (!cands.ContainsKey(prefix)) cands[prefix] = new Dictionary<long, TouchEvent>();
                cands[prefix][ev.id] = ev;
            }
            if (points.Count == 0 || cands.Count == 0) return null;

            var need = new HashSet<long>(points.ConvertAll(p => p.eventId));
            Dictionary<long, TouchEvent> exact = null, fallback = null;
            foreach (var m in cands.Values)
            {
                bool ok = m.Count >= need.Count;
                if (ok) foreach (var id in need) if (!m.ContainsKey(id)) { ok = false; break; }
                if (!ok) continue;
                if (m.Count == need.Count) { if (exact == null || m.Count < exact.Count) exact = m; }
                else if (fallback == null || m.Count < fallback.Count) fallback = m;
            }
            var events = exact ?? fallback;
            if (events == null) return null;

            var byEvent = new Dictionary<long, List<TouchInfo>>();
            foreach (var pt in points)
            {
                if (!byEvent.ContainsKey(pt.eventId)) byEvent[pt.eventId] = new List<TouchInfo>();
                byEvent[pt.eventId].Add(pt);
            }
            var ids = new List<long>(events.Keys);
            ids.Sort();
            var segments = new List<(TouchEvent seg, List<TouchInfo> pts)>();
            foreach (var id in ids)
            {
                if (!byEvent.TryGetValue(id, out var pts)) continue;
                pts.Sort((a, b) => a.time.CompareTo(b.time));
                segments.Add((events[id], pts));
            }
            if (segments.Count == 0) return null;
            return EncodeTouchSource(segments);
        }

        private const int TYPE_BG_LINE_CONFIG = 0x18;
        private const int TYPE_HEADER = 0x06;

        public sealed class BgLinesResult { public int color; public float spacing; public float width; public bool cross; }

        private static BgLinesResult ReadBgLineFields(byte[] val)
        {
            var dv = new Fb(val);
            FbTable t = RootTable(dv); if (t == null) return null;
            int p1 = FbField(dv, t, 1), p2 = FbField(dv, t, 2), p3 = FbField(dv, t, 3), p4 = FbField(dv, t, 4);
            if (p1 < 0 || p2 < 0 || p4 < 0) return null;
            float spacing = dv.F32(p2), width = dv.F32(p4);
            if (!float.IsFinite(spacing) || spacing <= 0 || spacing > 4096) return null;
            if (!float.IsFinite(width) || width <= 0 || width > spacing) return null;
            return new BgLinesResult
            {
                color = dv.I32(p1),
                spacing = spacing,
                width = width,
                cross = p3 >= 0 ? dv.I32(p3) == 1 : true
            };
        }

        public static BgLinesResult ReadBgLineConfig(byte[] mdb)
        {
            BgLinesResult best = null;
            foreach (var (key, val) in ReadMdbEntries(mdb))
            {
                if (key.Length != 8 || val.Length < 16) continue;
                if ((((key[2] << 8) | key[3]) >> 2) != TYPE_BG_LINE_CONFIG) continue;
                var f = ReadBgLineFields(val);
                if (f == null) continue;
                uint id = (uint)((key[4] << 24) | (key[5] << 16) | (key[6] << 8) | key[7]) >>> 0;
                if (best == null || id < (uint)best.GetHashCode()) best = f;
            }
            return best;
        }

        public static int? ReadHeaderBgColor(byte[] mdb)
        {
            foreach (var (key, val) in ReadMdbEntries(mdb))
            {
                if (key.Length != 8 || val.Length < 16) continue;
                if ((((key[2] << 8) | key[3]) >> 2) != TYPE_HEADER) continue;
                var dv = new Fb(val);
                FbTable t = RootTable(dv); if (t == null) continue;
                foreach (int fid in new[] { 12, 11 })
                {
                    int p = FbField(dv, t, fid);
                    if (p < 0) continue;
                    uint u = dv.U32(p);
                    if ((u >> 24) == 0xff) return (int)u;
                }
            }
            return null;
        }
    }
}
