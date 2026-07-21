namespace PanoToVideo.Core.Validation;

/// <summary>
/// 等距柱状投影输入的比例规则：目标 2.0，容差 ±1%（PRD 输入契约）。
/// </summary>
public static class AspectRatioRule
{
    public const double Target = 2.0;
    public const double Tolerance = 0.01; // ±1%

    public static double Ratio(int width, int height) =>
        height == 0 ? double.NaN : (double)width / height;

    public static bool IsWithinTolerance(double ratio) =>
        !double.IsNaN(ratio) &&
        ratio >= Target * (1.0 - Tolerance) &&
        ratio <= Target * (1.0 + Tolerance);
}
