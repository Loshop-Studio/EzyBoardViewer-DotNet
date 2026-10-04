namespace EzyBoardViewer.Models
{
    /// <summary>容器/分组节点（ROOT、GROUP）。本身不绘制，仅承载 Children 与可能的 transform。</summary>
    public class GroupElement : Element
    {
        public override string Kind => "group";
    }
}
