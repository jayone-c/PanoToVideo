using PanoToVideo.Core.Queue;

namespace PanoToVideo.Core.Validation;

/// <summary>
/// 入队校验结果（P0-4：入队即校验，无效图不进队列）。
/// Accepted=true 时 Item 为新建队列项；false 时 RejectionReason 含实际尺寸/比例。
/// </summary>
public sealed record IntakeResult(
    bool Accepted,
    string SourcePath,
    QueueItem? Item,
    int Width,
    int Height,
    double Ratio,
    string? RejectionReason)
{
    public static IntakeResult Accepted_(string path, QueueItem item, int w, int h, double ratio) =>
        new(true, path, item, w, h, ratio, null);

    public static IntakeResult Rejected(string path, int w, int h, double ratio, string reason) =>
        new(false, path, null, w, h, ratio, reason);
}

/// <summary>
/// 队列入队校验服务（P0-4：PRD 要求非 2:1/损坏图在入队或开跑前尽早报告，不进入队列）。
/// 纯逻辑：通过 IImageHeaderReader 读头 -> EquirectValidator 校验 -> 通过建 QueueItem，拒绝返回含实际尺寸/比例的原因。
/// </summary>
public sealed class QueueIntakeService
{
    private readonly EquirectValidator _validator = new();

    /// <summary>校验单张图并构建队列项（通过）或拒绝原因（含实际尺寸与比例）。</summary>
    public IntakeResult Intake(string path, IImageHeaderReader headerReader)
    {
        var header = headerReader.ReadHeader(path);
        var info = new ImageInfo(header.Width, header.Height, header.IsCorrupt, path);
        var v = _validator.Validate(info);

        if (!v.IsValid)
        {
            var reason = header.IsCorrupt
                ? $"{Path.GetFileName(path)}: 无法解码（损坏或非支持格式）"
                : $"{Path.GetFileName(path)}: {v.Reason}（实际 {v.Width}x{v.Height}，比例 {v.Ratio:F3}）";
            return IntakeResult.Rejected(path, v.Width, v.Height, v.Ratio, reason);
        }

        var item = new QueueItem(Path.GetFileName(path), v.Width, v.Height);
        return IntakeResult.Accepted_(path, item, v.Width, v.Height, v.Ratio);
    }

    /// <summary>批量校验，返回每张图的入队结果（部分通过部分拒绝互不影响）。</summary>
    public IReadOnlyList<IntakeResult> IntakeMany(IEnumerable<string> paths, IImageHeaderReader headerReader) =>
        paths.Select(p => Intake(p, headerReader)).ToList();
}
