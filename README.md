# EzyBoardViewer (.NET)

.NET 版 [EzyBoardViewer](https://github.com/Loshop-Studio/EzyBoardViewer-Vue)：解析**随身答画板 zip** 与**云笔记**，导出 **SVG / PDF**，并渲染 **MP4** 视频。

## 安装

```bash
dotnet add package EzyBoardViewer
```

## 快速开始

### 1. 随身答 zip

```csharp
using EzyBoardViewer;
using EzyBoardViewer.Models;

var pkg = await BoardViewer.OpenZipAsync(await File.ReadAllBytesAsync("board.zip"));

Console.WriteLine($"录制：{pkg.IsRecord}，V2：{pkg.IsV2}，页数：{pkg.Pages.Count}");

string svg = await pkg.ExportToSvgAsync(0);                 // 取第 1 页 SVG 字符串
await pkg.ExportToSvgAsync(0, "page1.svg");                 // 或直接写文件
await pkg.ExportToPdfAsync(0, "page1.pdf", fontPath: "HarmonyOS_Sans_SC_Regular.ttf"); // PDF 中的文字按 Unicode 子集嵌入字体
pkg.RenderVideo("out.mp4", VideoQuality.Mid);               // 渲染视频（需本机 FFmpeg）
```

### 2. 云笔记

```csharp
var note = await BoardViewer.OpenNoteAsync(new NoteDescriptor
{
    pages = new List<NotePageDescriptor>
    {
        new()
        {
            pageKey = 1,
            snapshotUrl = "https://.../snapshot.bin",
            mdbUrl = "https://.../data.mdb",     // 新版笔记笔触存这里
            headerUrl = "https://.../header.bin", // 老格式笔记背景在这里（可与 mdbUrl 同存，以 mdb 为准）
            width = 1080, height = 1920
        }
    },
    images = new List<NoteImageDescriptor>()
});

Console.WriteLine(note.PagesCount);
await note.ExportToPdfAsync("note.pdf");
```

> 云笔记的「分享链接 → 每页 snapshot/mdb/header/图片 元数据」由调用方从业务云 API 取得后填入 `NoteDescriptor`；
> 库内负责**按需拉取**并合成 `header.bin`、以及从 `data.mdb` 现场合成 `TouchSource`。

**底色与背景网格不需要手动传**：`NoteVfsSource` 会按页自动读背景——

- 新版笔记：优先从该页 `data.mdb`（`HeaderEntity.defaultBackgroundColor` + `BackgroundLineConfigEntity`）。
- 老格式笔记（无 mdb）：回退到该页 `header.bin` 的 protobuf 字段 11/13（`PbHeaderReader.ReadHeaderBlobBg` 解码）。

这是云笔记 App 端背景的唯一权威来源，不做任何截图采样 / 图像推断。

### 3. 数据模型

```csharp
Page    : Width, Height, Background, Elements, BackgroundLine, IsV1, IsV2, IsRecord
Element : StrokeElement | ImageElement | TextElement | GeometryElement   // 嵌套分组见 Element.Children
```

- `IsV1` = 随身答（不含 mdb）；`IsV2` = 含 mdb（LMDB + ObjectBox FlatBuffers）
- 图元坐标与 `EzyBoardViewer-Vue` 完全一致（含手绘笔迹的中点二次贝塞尔平滑）

## 依赖与要求

| 能力 | 说明                                                                                   |
| ---- | -------------------------------------------------------------------------------------- |
| SVG  | 纯算法生成，无额外依赖                                                                 |
| PDF  | PdfSharp 矢量重绘 + **CJK 字体按 Unicode 子集嵌入**（仅打包实际用到的字形）。传 `fontPath` 指向 `.ttf`（如 HarmonyOS Sans SC）；不传时退到系统 Arial，**中文会缺字** |
| 视频 | SkiaSharp 逐帧渲染 + 外部**FFmpeg** 编码（PATH 或 `C:\ffmpeg\bin\ffmpeg.exe`） |
| zip  | 内置最小 zip reader（stored / deflate-raw，不支持 zip64）                              |
| mdb  | 内置 LMDB + FlatBuffers 解析，不依赖额外包                                             |

## 许可

MIT
