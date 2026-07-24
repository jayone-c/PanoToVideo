namespace PanoToVideo.Core.Validation;

/// <summary>
/// 图像头信息（仅尺寸与可读性，不解码像素，节省内存）。
/// </summary>
public sealed record ImageHeader(int Width, int Height, bool IsCorrupt);

/// <summary>
/// 图像头读取抽象（IO 层实现：App 用 System.Drawing.Bitmap 仅读 width/height）。
/// Core 通过此抽象做入队校验，不依赖文件系统，便于单测注入 fake。
/// </summary>
public interface IImageHeaderReader
{
    /// <summary>读取图像头：返回尺寸；无法解码时 IsCorrupt=true。</summary>
    ImageHeader ReadHeader(string path);
}
