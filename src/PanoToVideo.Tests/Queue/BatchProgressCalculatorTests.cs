using PanoToVideo.Core.Queue;

namespace PanoToVideo.Tests.Queue;

public class BatchProgressCalculatorTests
{
    [Fact]
    public void 已完成任务的延迟末帧回调_不可与完成数重复计入()
    {
        // 14 项都已完成，但最后一帧的 UI 回调才执行：不能计算为 (14 + 1) / 14。
        var progress = BatchProgressCalculator.CalculatePercent(
            totalItems: 14, completedItems: 14, hasActiveItem: false, activeProgressFraction: 1);

        Assert.Equal(100, progress);
    }
}
