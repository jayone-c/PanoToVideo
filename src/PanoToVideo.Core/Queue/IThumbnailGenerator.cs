namespace PanoToVideo.Core.Queue;

/// <summary>
/// 缩略图生成抽象（P1：队列项缩略图，开发规划 §阶段3任务2）。
/// Core 仅定义契约，IO 实现放 App（System.Drawing 缩放 JPEG）。
/// 便于单测注入 fake，避免 Core 依赖图像库。
/// </summary>
public interface IThumbnailGenerator
{
    /// <summary>
    /// 由源 ERP 路径生成缩略图字节（JPEG）。
    /// </summary>
    /// <param name="sourcePath">源 ERP 图像路径。</param>
    /// <param name="maxEdge">缩略图最长边像素，默认 256。</param>
    /// <returns>JPEG 字节数组；源不可读时返回 null（不阻断入队）。</returns>
    byte[]? Generate(string sourcePath, int maxEdge = 256);
}
