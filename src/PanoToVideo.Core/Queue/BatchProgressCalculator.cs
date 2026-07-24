namespace PanoToVideo.Core.Queue;

/// <summary>批量导出的整体进度计算，隔离 UI 回调与任务状态交错时的计数规则。</summary>
public static class BatchProgressCalculator
{
    public static double CalculatePercent(int totalItems, int completedItems, bool hasActiveItem, double activeProgressFraction)
    {
        if (totalItems <= 0) return 0;
        var completed = Math.Clamp(completedItems, 0, totalItems);
        var activeProgress = hasActiveItem ? Math.Clamp(activeProgressFraction, 0, 1) : 0;
        return Math.Clamp((completed + activeProgress) / totalItems * 100, 0, 100);
    }
}
