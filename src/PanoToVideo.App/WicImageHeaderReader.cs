using System.Drawing;
using PanoToVideo.Core.Validation;

namespace PanoToVideo.App;

/// <summary>
/// IImageHeaderReader 的 System.Drawing 实现（P0-4：入队即校验）。
/// 仅读图像头（width/height），不解码像素，节省内存。
/// 无法解码时返回 IsCorrupt=true，由 QueueIntakeService 拒绝入队。
/// </summary>
public sealed class WicImageHeaderReader : IImageHeaderReader
{
    public ImageHeader ReadHeader(string path)
    {
        try
        {
            using var bmp = new Bitmap(path);
            return new ImageHeader(bmp.Width, bmp.Height, IsCorrupt: false);
        }
        catch
        {
            return new ImageHeader(0, 0, IsCorrupt: true);
        }
    }
}
