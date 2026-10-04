using System.Collections.Generic;

namespace EzyBoardViewer.Models
{
    /// <summary>图元基类。Transform 为 Android Matrix → SVG matrix(a b c d e f)，Children 保持层级（group 嵌套）。</summary>
    public abstract class Element
    {
        /// <summary>SVG matrix [a,b,c,d,e,f]，可为 null。</summary>
        public float[] Transform { get; set; }
        /// <summary>包围盒 [l, t, r, b]。</summary>
        public float[] Rect { get; set; } = new[] { 0f, 0f, 0f, 0f };
        public List<Element> Children { get; set; } = new();
        /// <summary>图元类型：stroke / image / text / geometry。</summary>
        public abstract string Kind { get; }
    }

    public class StrokeElement : Element
    {
        public List<(float X, float Y)> Points { get; set; } = new();
        public string Color { get; set; } = "#000000";
        public float Opacity { get; set; } = 1;
        public float StrokeWidth { get; set; } = 2;
        public float[] DashArray { get; set; }
        /// <summary>true=直线(BEELINE)，false=手绘笔迹(STROKE，中点二次贝塞尔平滑)。</summary>
        public bool IsBeeline { get; set; }
        public override string Kind => "stroke";
    }

    public class ImageElement : Element
    {
        /// <summary>资源文件名（res/image/ 下的名字）。</summary>
        public string ImageName { get; set; }
        public override string Kind => "image";
    }

    public class TextElement : Element
    {
        public string Text { get; set; } = "";
        public string Color { get; set; } = "#000000";
        public float Opacity { get; set; } = 1;
        public float FontSize { get; set; } = 40;
        public override string Kind => "text";
    }

    public class GeometryElement : Element
    {
        /// <summary>1=polygon，其它=ellipse。</summary>
        public int GeoType { get; set; }
        public List<(float X, float Y)> Points { get; set; } = new();
        public string Color { get; set; } = "#000000";
        public float Opacity { get; set; } = 1;
        public float StrokeWidth { get; set; } = 2;
        public override string Kind => "geometry";
    }
}
