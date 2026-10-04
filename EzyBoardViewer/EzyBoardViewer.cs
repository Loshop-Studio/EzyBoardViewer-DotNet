using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EzyBoardViewer.Core;
using EzyBoardViewer.Models;

namespace EzyBoardViewer
{
    /// <summary>视频画质（对应 board.js 的 MP4_QUALITY：long=长边像素，kbps=码率）。</summary>
    public enum VideoQuality
    {
        Low = 0,   // 960p, 1500kbps
        Mid = 1,   // 1280p, 3000kbps
        High = 2   // 1920p, 8000kbps
    }

    /// <summary>随身答(zip) 解析结果。</summary>
    public class SuibianPackage
    {
        private readonly IBoardSource _src;
        internal IBoardSource Source => _src;
        public bool IsRecord { get; }
        public bool IsV2 { get; }
        public IReadOnlyList<Page> Pages { get; }

        internal SuibianPackage(IBoardSource src, List<Page> pages, bool isRecord, bool isV2)
        {
            _src = src; Pages = pages; IsRecord = isRecord; IsV2 = isV2;
        }

        /// <summary>导出某一页为 SVG 字符串。</summary>
        public Task<string> ExportToSvgAsync(int pageIndex) => SvgRenderer.RenderAsync(Pages[pageIndex], _src);

        /// <summary>导出某一页 SVG 到文件。</summary>
        public async Task ExportToSvgAsync(int pageIndex, string filePath)
            => await File.WriteAllTextAsync(filePath, await ExportToSvgAsync(pageIndex));

        /// <summary>导出某一页为 PDF（矢量）。fontPath 指向 CJK 字体 .ttf（如 HarmonyOS Sans SC），否则中文缺字。</summary>
        public async Task ExportToPdfAsync(int pageIndex, string filePath, string fontPath = null)
        {
            using var fs = File.Create(filePath);
            await PdfExporter.ExportPagesAsync(new[] { Pages[pageIndex] }, _src, fs, fontPath);
        }

        /// <summary>渲染视频（MP4）。需要本机可用 FFmpeg。</summary>
        public void RenderVideo(string outputPath, VideoQuality quality = VideoQuality.Mid)
            => VideoRenderer.Render(this, outputPath, quality);
    }

    /// <summary>云笔记解析结果。</summary>
    public class NotePackage
    {
        private readonly IBoardSource _src;
        public int PagesCount { get; }
        public IReadOnlyList<Page> Pages { get; }
        internal NotePackage(List<Page> pages, IBoardSource src) { Pages = pages; PagesCount = pages.Count; _src = src; }

        /// <summary>导出全部页为 PDF（每页一页）。fontPath 指向 CJK 字体 .ttf。</summary>
        public async Task ExportToPdfAsync(string filePath, string fontPath = null)
        {
            using var fs = File.Create(filePath);
            await PdfExporter.ExportPagesAsync(Pages, _src, fs, fontPath);
        }
    }

    /// <summary>EzyBoardViewer 入口。</summary>
    public static class BoardViewer
    {
        /// <summary>从 zip 字节打开随身答包。</summary>
        public static async Task<SuibianPackage> OpenZipAsync(byte[] zipBytes)
        {
            var src = new ZipBoardSource(zipBytes);
            var parser = new BoardParser(src);
            var (pages, isRecord, isV2) = await parser.ParseAllAsync();
            return new SuibianPackage(src, pages, isRecord, isV2);
        }

        /// <summary>从云笔记 URL（+ 可选本地路径）打开笔记。TODO: 后续阶段实现（需要云 API 元数据）。</summary>
        public static Task<NotePackage> OpenNoteAsync(string noteUrl, string localPath = null)
            => throw new NotImplementedException("OpenNote(url) 尚未实现：云笔记需要先调用其云 API 解析出每页 snapshot/mdb/image 的元数据。" +
                "请改用 OpenNoteAsync(NoteDescriptor)，直接传入已从云 API 取出的页面资源描述。");

        /// <summary>用显式资源描述打开云笔记（推荐）。内部按需拉取并合成 header / TouchSource。</summary>
        public static async Task<NotePackage> OpenNoteAsync(NoteDescriptor descriptor)
        {
            var src = new NoteVfsSource(descriptor);
            var parser = new BoardParser(src);
            var (pages, isRecord, isV2) = await parser.ParseAllAsync();
            return new NotePackage(pages, src);
        }
    }
}
