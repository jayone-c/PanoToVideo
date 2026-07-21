namespace PanoToVideo.Core.Parameters;

/// <summary>
/// 参数校验结果。收集所有违规以便 UI 一次性展示。
/// </summary>
public sealed record ParameterValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ParameterValidationResult Ok() => new(true, Array.Empty<string>());

    public static ParameterValidationResult Fail(IReadOnlyList<string> errors) => new(false, errors);
}
