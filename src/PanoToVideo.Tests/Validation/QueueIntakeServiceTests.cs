using PanoToVideo.Core.Queue;
using PanoToVideo.Core.Validation;

namespace PanoToVideo.Tests.Validation;

/// <summary>
/// 入队校验服务 TDD 测试（P0-4：入队即校验，非 2:1/损坏图不进队列）。
/// 契约来源：PRD「输入与校验」+ 开发规划 §8 输入契约。
/// 通过 IImageHeaderReader 抽象注入 fake，避免真实文件 IO。
/// </summary>
public class QueueIntakeServiceTests
{
    private readonly QueueIntakeService _sut = new();

    /// <summary>测试用 fake：按路径字典返回预设图像头。</summary>
    private sealed class FakeHeaderReader : IImageHeaderReader
    {
        private readonly Dictionary<string, ImageHeader> _map;
        public FakeHeaderReader(Dictionary<string, ImageHeader> map) => _map = map;

        public ImageHeader ReadHeader(string path) =>
            _map.TryGetValue(path, out var h) ? h : new ImageHeader(0, 0, IsCorrupt: true);
    }

    private static FakeHeaderReader Reader(params (string path, int w, int h, bool corrupt)[] entries)
    {
        var map = entries.ToDictionary(
            e => e.path,
            e => new ImageHeader(e.w, e.h, e.corrupt));
        return new FakeHeaderReader(map);
    }

    [Fact]
    public void Intake_标准8192x4096_通过并建队列项()
    {
        var reader = Reader(("a.jpg", 8192, 4096, false));

        var r = _sut.Intake("a.jpg", reader);

        Assert.True(r.Accepted);
        Assert.NotNull(r.Item);
        Assert.Equal("a.jpg", r.Item!.SourceFileName);
        Assert.Equal(8192, r.Item.Width);
        Assert.Equal(4096, r.Item.Height);
        Assert.Null(r.RejectionReason);
        Assert.Equal(2.0, r.Ratio, 3);
    }

    [Fact]
    public void Intake_比例1p953_拒绝并含实际比例()
    {
        // 8000/4096 ≈ 1.953，低于 2.0±1% 下界 1.98
        var reader = Reader(("bad.jpg", 8000, 4096, false));

        var r = _sut.Intake("bad.jpg", reader);

        Assert.False(r.Accepted);
        Assert.Null(r.Item);
        Assert.NotNull(r.RejectionReason);
        Assert.Contains("bad.jpg", r.RejectionReason!);
        Assert.Contains("1.953", r.RejectionReason!);
        Assert.Contains("8000x4096", r.RejectionReason!);
    }

    [Fact]
    public void Intake_损坏文件_拒绝并提示无法解码()
    {
        var reader = Reader(("corrupt.jpg", 0, 0, true));

        var r = _sut.Intake("corrupt.jpg", reader);

        Assert.False(r.Accepted);
        Assert.Null(r.Item);
        Assert.NotNull(r.RejectionReason);
        Assert.Contains("corrupt.jpg", r.RejectionReason!);
        Assert.Contains("解码", r.RejectionReason!);
    }

    [Fact]
    public void Intake_低于5000x2500_拒绝并含最低尺寸()
    {
        var reader = Reader(("small.jpg", 4998, 2499, false));

        var r = _sut.Intake("small.jpg", reader);

        Assert.False(r.Accepted);
        Assert.NotNull(r.RejectionReason);
        Assert.Contains("5000x2500", r.RejectionReason!);
    }

    [Fact]
    public void Intake_5000x2500_通过()
    {
        var reader = Reader(("minimum.jpg", 5000, 2500, false));

        var r = _sut.Intake("minimum.jpg", reader);

        Assert.True(r.Accepted);
    }

    [Fact]
    public void Intake_宽大于16384_拒绝()
    {
        var reader = Reader(("huge.jpg", 20000, 10000, false));

        var r = _sut.Intake("huge.jpg", reader);

        Assert.False(r.Accepted);
        Assert.NotNull(r.RejectionReason);
        Assert.Contains("16384", r.RejectionReason!);
    }

    [Fact]
    public void IntakeMany_混合输入_部分通过部分拒绝互不影响()
    {
        var reader = Reader(
            ("ok1.jpg", 8192, 4096, false),
            ("bad_ratio.jpg", 8000, 4096, false),
            ("corrupt.jpg", 0, 0, true),
            ("ok2.jpg", 16000, 8000, false));

        var results = _sut.IntakeMany(
            new[] { "ok1.jpg", "bad_ratio.jpg", "corrupt.jpg", "ok2.jpg" },
            reader);

        Assert.Equal(4, results.Count);
        Assert.True(results[0].Accepted);
        Assert.False(results[1].Accepted);
        Assert.False(results[2].Accepted);
        Assert.True(results[3].Accepted);
        Assert.NotNull(results[0].Item);
        Assert.NotNull(results[3].Item);
        Assert.Equal("ok2.jpg", results[3].Item!.SourceFileName);
    }

    [Fact]
    public void Intake_未知路径_按损坏拒绝()
    {
        // fake 字典未包含路径 -> ReadHeader 返回 corrupt
        var reader = Reader(("known.jpg", 8192, 4096, false));

        var r = _sut.Intake("unknown.jpg", reader);

        Assert.False(r.Accepted);
        Assert.Contains("解码", r.RejectionReason!);
    }

    [Fact]
    public void Intake_容差下界附近_通过()
    {
        // 1.98 * 4096 = 8110.08，最小可通过宽 8111（8111/4096 = 1.9802）
        var reader = Reader(("edge.jpg", 8111, 4096, false));

        var r = _sut.Intake("edge.jpg", reader);

        Assert.True(r.Accepted);
        Assert.NotNull(r.Item);
    }
}
