namespace PanoToVideo.Core.Projection;

/// <summary>按目标视频比例计算预览尺寸，最长边固定以控制 UI 渲染成本。</summary>
public static class PreviewSizing
{
    public static (int Width, int Height) Fit(int targetWidth, int targetHeight, int maxSide = 320)
    {
        if (targetWidth <= 0 || targetHeight <= 0 || maxSide <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "预览尺寸必须为正数。");

        var scale = Math.Min((double)maxSide / targetWidth, (double)maxSide / targetHeight);
        return (Math.Max(1, (int)Math.Round(targetWidth * scale)), Math.Max(1, (int)Math.Round(targetHeight * scale)));
    }
}
