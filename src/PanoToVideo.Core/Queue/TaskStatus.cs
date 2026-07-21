namespace PanoToVideo.Core.Queue;

/// <summary>
/// 任务状态机（开发规划 §阶段2任务2）。
/// 待校验 -> 待处理 -> 处理中 -> 完成；异常->失败(可重试)；取消->已取消。
/// </summary>
public enum TaskStatus
{
    PendingValidation,
    Pending,
    Processing,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>状态机入口与常量。</summary>
public static class TaskState
{
    public static readonly TaskStatus Initial = TaskStatus.PendingValidation;
}
