using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using EzyBoardViewer.Models;

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 把云笔记资源按需组装成 IBoardSource：
    ///   - 旧笔记：每页 header.bin/snapshot.bin + 每段的 *_touch.bin + 共享 res/image/*
    ///   - 新笔记（V2）：不上传 *_touch.bin，笔触在 page_mdb/data.mdb（LMDB+ObjectBox）里，
    ///     按 snapshot 引用的 touch 文件名现场合成 TouchSource 返回。
    /// 背景（底色 / 背景线）来源：新版从 page_mdb/data.mdb 读，老格式回退到 header.bin protobuf。
    /// 对齐 EzyBoardViewer-Vue/src/utils/noteVfs.ts 的 createNoteVfs。
    /// </summary>
    public sealed class NoteVfsSource : IBoardSource
    {
        private sealed class MdbCfg { public int? bg; public MdbReader.BgLinesResult bgLines; }

        private readonly NoteDescriptor _desc;
        private readonly HttpClient _http;
        private readonly Dictionary<string, NotePageDescriptor> _pageDirMap = new();
        private readonly Dictionary<string, NoteImageDescriptor> _imageMap = new();
        private readonly Dictionary<string, byte[]> _cache = new();
        private readonly Dictionary<string, Task<byte[]>> _inflight = new();
        private readonly Dictionary<string, Task<MdbCfg>> _mdbCfg = new();

        public IReadOnlyList<string> Names { get; }

        public NoteVfsSource(NoteDescriptor desc, HttpClient http = null)
        {
            _desc = desc;
            _http = http ?? new HttpClient();
            var pages = desc.pages.OrderBy(p => p.pageKey).ToList();
            var names = new List<string>();
            foreach (var p in pages)
            {
                string dir = p.pageKey + "/";
                _pageDirMap[dir] = p;
                names.Add(dir + "header.bin");
                names.Add(dir + "snapshot.bin");
                if (!string.IsNullOrEmpty(p.mdbUrl)) names.Add(dir + "page_mdb/data.mdb");
                foreach (var tu in p.touchUrls ?? new string[0])
                    names.Add(dir + tu.Split('/').Last());
            }
            foreach (var img in desc.images ?? new List<NoteImageDescriptor>())
            {
                string key = "res/image/" + img.fileName;
                _imageMap[key] = img;
                names.Add(key);
            }
            Names = names;
        }

        private Task<byte[]> Fetch(string url)
        {
            lock (_inflight)
            {
                if (_cache.TryGetValue(url, out var hit)) return Task.FromResult(hit);
                if (_inflight.TryGetValue(url, out var inf)) return inf;
                var t = _http.GetByteArrayAsync(url);
                _inflight[url] = t;
                return t;
            }
        }

        /// <summary>
        /// 该页画布背景配置（底色 + 背景线）。
        /// - 新版笔记：mdbUrl 不为空，背景存在 page_mdb/data.mdb（HeaderEntity + BackgroundLineConfigEntity）。
        /// - 老格式笔记：无 mdb，背景存在 header.bin（protobuf 字段 11 / 13）。
        /// mdb 优先；缺失或解析失败时回退到 header.bin。同 URL 只下载解析一次。
        /// </summary>
        private Task<MdbCfg> MdbConfig(NotePageDescriptor p)
        {
            string mdbUrl = p?.mdbUrl;
            string headerUrl = p?.headerUrl;
            if (string.IsNullOrEmpty(mdbUrl) && string.IsNullOrEmpty(headerUrl)) return Task.FromResult(new MdbCfg());
            string key = !string.IsNullOrEmpty(mdbUrl) ? mdbUrl : headerUrl;
            lock (_mdbCfg)
            {
                if (_mdbCfg.TryGetValue(key, out var t)) return t;
                t = Task.Run(async () =>
                {
                    // 1. 新版：mdb（最权威，命中就以它为准）
                    if (!string.IsNullOrEmpty(mdbUrl))
                    {
                        try
                        {
                            var u8 = await _http.GetByteArrayAsync(mdbUrl);
                            var cfg = new MdbCfg { bg = MdbReader.ReadHeaderBgColor(u8), bgLines = MdbReader.ReadBgLineConfig(u8) };
                            if (cfg.bg != null || cfg.bgLines != null) return cfg;
                        }
                        catch { /* mdb 拉取或解析失败，继续回退到 header.bin */ }
                    }
                    // 2. 老格式：protobuf header.bin（老 Android 端无 mdb，背景就在这里）
                    if (!string.IsNullOrEmpty(headerUrl))
                    {
                        try
                        {
                            var u8 = await _http.GetByteArrayAsync(headerUrl);
                            var r = PbHeaderReader.ReadHeaderBlobBg(u8);
                            return new MdbCfg { bg = r.bgColor, bgLines = r.bgLines };
                        }
                        catch { /* ignore */ }
                    }
                    return new MdbCfg();
                });
                _mdbCfg[key] = t;
                return t;
            }
        }

        private static byte[] MakeHeader(int w, int h, int bgcolor, BgLines bl)
        {
            var bytes = new List<byte>();
            bytes.AddRange(PbWriter.I32(2, w));
            bytes.AddRange(PbWriter.I32(3, h));
            bytes.AddRange(PbWriter.I32(11, bgcolor));
            if (bl != null && bl.spacing > 0)
            {
                var body = new List<byte>();
                body.AddRange(PbWriter.I32(1, bl.color));
                body.AddRange(PbWriter.F32(2, bl.spacing));
                body.AddRange(PbWriter.I32(3, bl.cross ? 1 : 0));
                body.AddRange(PbWriter.F32(4, bl.width));
                bytes.AddRange(PbWriter.Msg(13, body.ToArray()));
            }
            return bytes.ToArray();
        }

        public async Task<byte[]> ReadAsync(string name)
        {
            var seg = name.Split('/');
            string baseName = seg[seg.Length - 1];

            if (baseName == "header.bin")
            {
                string dir = seg[0] + "/";
                var p = _pageDirMap.TryGetValue(dir, out var pp) ? pp : null;
                int w = p?.width ?? 1080, h = p?.height ?? 1920;
                var cfg = await MdbConfig(p);
                int bg = cfg.bg ?? (p != null && p.bgcolor != -1 ? (int?)p.bgcolor : null) ?? -1;
                BgLines bl = cfg.bgLines != null
                    ? new BgLines { color = cfg.bgLines.color, spacing = cfg.bgLines.spacing, width = cfg.bgLines.width, cross = cfg.bgLines.cross }
                    : p?.bgLines;
                return MakeHeader(w, h, bg, bl);
            }

            if (seg.Length >= 2)
            {
                string dir = seg[0] + "/";
                if (_pageDirMap.TryGetValue(dir, out var p))
                {
                    if (baseName == "snapshot.bin") return await Fetch(p.snapshotUrl);
                    if (baseName == "data.mdb") return await Fetch(p.mdbUrl);
                    if (baseName.EndsWith("_touch.bin"))
                    {
                        var tu = (p.touchUrls ?? new string[0]).FirstOrDefault(u => u.Split('/').Last() == baseName);
                        if (tu != null) return await Fetch(tu);
                        if (!string.IsNullOrEmpty(p.mdbUrl))
                        {
                            var u8 = await Fetch(p.mdbUrl);
                            return MdbReader.BuildTouchSource(u8);
                        }
                    }
                }
            }

            if (name.StartsWith("res/image/") && _imageMap.TryGetValue(name, out var img))
                return await Fetch(img.url);

            return null;
        }
    }
}
