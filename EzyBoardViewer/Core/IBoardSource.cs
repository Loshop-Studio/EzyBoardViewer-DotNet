using System.Collections.Generic;
using System.Threading.Tasks;

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 统一的虚拟文件系统接口：zip、文件列表、云笔记按需拉取都实现它。
    /// 与 EzyBoardViewer-Vue 的 { names, read } 对应。
    /// </summary>
    public interface IBoardSource
    {
        IReadOnlyList<string> Names { get; }
        Task<byte[]> ReadAsync(string name);
    }
}
