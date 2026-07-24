namespace PanoToVideo.Core.Validation;

/// <summary>
/// ERP 输入校验器。校验顺序：损坏 -> 最小尺寸 -> 宽上限 -> 比例。
/// 任一失败即返回带实际值的用户可读原因。
/// </summary>
public sealed class EquirectValidator
{
    public const int MinWidth = 5000;
    public const int MinHeight = 2500;
    public const int MaxWidth = 16384;

    public ValidationResult Validate(ImageInfo info)
    {
        if (info.IsCorrupt)
            return ValidationResult.Fail(info.Width, info.Height, double.NaN, "图像无法解码");

        var ratio = AspectRatioRule.Ratio(info.Width, info.Height);

        if (info.Width < MinWidth || info.Height < MinHeight)
            return ValidationResult.Fail(info.Width, info.Height, ratio,
                $"图像尺寸 {info.Width}x{info.Height} 低于最低支持尺寸 {MinWidth}x{MinHeight}");

        if (info.Width > MaxWidth)
            return ValidationResult.Fail(info.Width, info.Height, ratio,
                $"图像宽度 {info.Width} 超出支持范围（最大 {MaxWidth}）");

        if (!AspectRatioRule.IsWithinTolerance(ratio))
            return ValidationResult.Fail(info.Width, info.Height, ratio,
                $"宽高比 {ratio:F3} 不符合 2.0 ± 1%（要求 [1.980, 2.020]）");

        return ValidationResult.Ok(info.Width, info.Height, ratio);
    }
}
