using System.Collections.Generic;

namespace EzyBoardViewer.Models
{
    /// <summary>背景线配置（横线 / 网格）。</summary>
    public class BackgroundLine
    {
        public int Color { get; set; }        // 0xAARRGGBB
        public float Spacing { get; set; }
        public int Type { get; set; }         // 1 = 交叉网格
        public float Width { get; set; }
    }

    /// <summary>
    /// 一页内容。
    /// Elements 为根节点的直接子图元；嵌套 group 通过 Element.Children 表达。
    /// IsV1 / IsV2 / IsRecord 表示该页（通常指整个包）的格式与录制状态。
    /// </summary>
    public class Page
    {
        public int Width { get; set; }
        public int Height { get; set; }
        /// <summary>背景色，hex "#RRGGBB"。</summary>
        public string Background { get; set; } = "#ffffff";
        public List<Element> Elements { get; set; } = new();
        public BackgroundLine BackgroundLine { get; set; }
        public bool IsV1 { get; set; }
        public bool IsV2 { get; set; }
        public bool IsRecord { get; set; }
    }
}
