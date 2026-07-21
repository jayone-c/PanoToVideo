namespace PanoToVideo.Core.Validation;

/// <summary>
/// ERP 校验结果。IsValid=false 时 Reason 给出用户可读原因（含实际尺寸/比例）。
/// </summary>
public sealed record ValidationResult(
    bool IsValid,
    string Reason,
    int Width,
    int Height,
    double Ratio)
{
    public static ValidationResult Ok(int width, int height, double ratio) =>
        new(true, string.Empty, width, height, ratio);

    public static ValidationResult Fail(int width, int height, double ratio, string reason) =>
        new(false, reason, width, height, ratio);
}
