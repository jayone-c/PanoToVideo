using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PanoToVideo.Core.Queue;

namespace PanoToVideo.App;

/// <summary>
/// IErpLoader 的按需解码实现（P0-5：100 张批量输入内存控制）。
/// 每项 Load 时解码 RGBA，Dispose 时由 LoadedErp 释放引用（GC 回收）。
/// 替代旧 _rgbaCache 预解码字典：100 张不再常驻 RGBA 内存，峰值≈单张+缓冲。
/// </summary>
public sealed class OnDemandErpLoader : IErpLoader
{
    private readonly Dictionary<QueueItem, string> _paths;

    public OnDemandErpLoader(Dictionary<QueueItem, string> paths) => _paths = paths;

    public LoadedErp Load(QueueItem item)
    {
        var path = _paths[item];
        var (rgba, w, h) = DecodeImage(path);
        return new LoadedErp(rgba, w, h);
    }

    /// <summary>解码图像为 RGBA 字节（BGRA→RGBA 交换，alpha=255）。</summary>
    private static (byte[] rgba, int w, int h) DecodeImage(string path)
    {
        using var bmp = new Bitmap(path);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var rgba = new byte[bmp.Width * bmp.Height * 4];
            for (int y = 0; y < bmp.Height; y++)
            {
                var row = new byte[bmp.Width * 4];
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, bmp.Width * 4);
                for (int x = 0; x < bmp.Width; x++)
                {
                    rgba[(y * bmp.Width + x) * 4] = row[x * 4 + 2];     // R
                    rgba[(y * bmp.Width + x) * 4 + 1] = row[x * 4 + 1]; // G
                    rgba[(y * bmp.Width + x) * 4 + 2] = row[x * 4];     // B
                    rgba[(y * bmp.Width + x) * 4 + 3] = 255;            // A
                }
            }
            return (rgba, bmp.Width, bmp.Height);
        }
        finally { bmp.UnlockBits(data); }
    }
}
