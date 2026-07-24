using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Tests.Precheck;

public sealed class VramPrecheckTests
{
    [Fact]
    public void EstimateRequiredBytes_包含输入与输出工作缓冲()
    {
        var bytes = VramPrecheck.EstimateRequiredBytes(8192, 4096, 1080, 1920);

        Assert.True(bytes > 8192L * 4096 * 4);
    }

    [Fact]
    public void Check_显存充足_允许开始()
    {
        var result = VramPrecheck.Check(8192, 4096, 1080, 1920, 24L * 1024 * 1024 * 1024);

        Assert.True(result.CanProceed);
        Assert.NotNull(result.AvailableVramBytes);
    }

    [Fact]
    public void Check_显存不足_阻断开始()
    {
        var result = VramPrecheck.Check(16384, 8192, 1080, 1920, 256L * 1024 * 1024);

        Assert.False(result.CanProceed);
        Assert.Contains("显存不足", result.Reason);
    }

    [Fact]
    public void Check_共享显存未知_不做错误阻断()
    {
        var result = VramPrecheck.Check(8192, 4096, 1080, 1920, 0);

        Assert.True(result.CanProceed);
        Assert.Null(result.AvailableVramBytes);
    }
}
