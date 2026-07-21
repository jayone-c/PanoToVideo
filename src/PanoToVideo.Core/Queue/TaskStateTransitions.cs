namespace PanoToVideo.Core.Queue;

/// <summary>
/// 任务状态转换规则。非法转换抛 <see cref="InvalidOperationException"/>。
/// 规则对齐开发规划 §阶段2任务2。
/// </summary>
public static class TaskStateTransitions
{
    private static readonly Dictionary<TaskStatus, HashSet<TaskStatus>> Allowed = new()
    {
        [TaskStatus.PendingValidation] = new() { TaskStatus.Pending, TaskStatus.Failed },
        [TaskStatus.Pending] = new() { TaskStatus.Processing, TaskStatus.Cancelled },
        [TaskStatus.Processing] = new() { TaskStatus.Completed, TaskStatus.Failed, TaskStatus.Cancelled },
        [TaskStatus.Failed] = new() { TaskStatus.Pending }, // 可重试
        [TaskStatus.Completed] = new(),
        [TaskStatus.Cancelled] = new(), // 已取消不可重试
    };

    public static bool CanTransition(TaskStatus from, TaskStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static TaskStatus Transition(TaskStatus from, TaskStatus to)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException($"非法状态转换：{from} -> {to}");
        return to;
    }
}
