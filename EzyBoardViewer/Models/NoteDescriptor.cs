namespace EzyBoardViewer.Models
{
    /// <summary>背景线（与 board.js 的 BackgroundLineConfigEntity 一致）。</summary>
    public class BgLines
    {
        public int color;          // 0xAARRGGBB
        public float spacing;
        public float width;
        public bool cross;         // true = 横竖交错网格
    }

    /// <summary>共享图片资源（res/image/*）。</summary>
    public class NoteImageDescriptor
    {
        public string fileName;    // 如 "abc.png"，会拼成 res/image/abc.png
        public string url;         // OSS 完整 URL
    }

    /// <summary>云笔记的一页资源指针。</summary>
    public class NotePageDescriptor
    {
        public int pageKey;                 // 页号（1-based），用作虚拟目录名
        public string snapshotUrl;          // 该页 snapshot.bin 的 URL
        public string[] touchUrls;          // 旧笔记：每段笔触一个独立文件
        public string mdbUrl;               // 新笔记：page_mdb/data.mdb 的 URL
        // 老格式笔记（无 mdb）：底色与背景线的唯一来源。新版可省——背景从 mdb 读。
        // 与 mdbUrl 同存时以 mdb 为准；mdb 缺失 / 解析失败时回退到它（protobuf 字段 11/13）。
        public string headerUrl;
        public int width = 1080;
        public int height = 1920;
        public int bgcolor = -1;            // -1 = 不透明白
        public BgLines bgLines;             // 背景线（无则只画纯色底）
    }

    /// <summary>一篇云笔记的全部资源描述（由调用方从云 API 解析后传入）。</summary>
    public class NoteDescriptor
    {
        public System.Collections.Generic.List<NotePageDescriptor> pages = new();
        public System.Collections.Generic.List<NoteImageDescriptor> images = new();
    }
}
