using PanoToVideo.Core.Queue;
using TaskStatus = PanoToVideo.Core.Queue.TaskStatus;

namespace PanoToVideo.Tests.Queue;

/// <summary>
/// 任务状态机 TDD 测试。
/// 契约：开发规划 §阶段2任务2。待校验->待处理->处理中->完成；异常->失败(可重试)；取消->已取消。
/// </summary>
public class TaskStateTransitionsTests
{
    [Fact]
    public void 初始状态为待校验()
    {
        Assert.Equal(TaskStatus.PendingValidation, TaskState.Initial);
    }

    [Fact]
    public void 待校验_转_待处理_合法()
    {
        Assert.Equal(TaskStatus.Pending,
            TaskStateTransitions.Transition(TaskStatus.PendingValidation, TaskStatus.Pending));
    }

    [Fact]
    public void 待校验_转_失败_合法()
    {
        Assert.Equal(TaskStatus.Failed,
            TaskStateTransitions.Transition(TaskStatus.PendingValidation, TaskStatus.Failed));
    }

    [Fact]
    public void 待处理_转_处理中_合法()
    {
        Assert.Equal(TaskStatus.Processing,
            TaskStateTransitions.Transition(TaskStatus.Pending, TaskStatus.Processing));
    }

    [Fact]
    public void 处理中_转_完成_合法()
    {
        Assert.Equal(TaskStatus.Completed,
            TaskStateTransitions.Transition(TaskStatus.Processing, TaskStatus.Completed));
    }

    [Fact]
    public void 处理中_转_失败_合法()
    {
        Assert.Equal(TaskStatus.Failed,
            TaskStateTransitions.Transition(TaskStatus.Processing, TaskStatus.Failed));
    }

    [Fact]
    public void 处理中_转_已取消_合法()
    {
        Assert.Equal(TaskStatus.Cancelled,
            TaskStateTransitions.Transition(TaskStatus.Processing, TaskStatus.Cancelled));
    }

    [Fact]
    public void 失败_转_待处理_可重试()
    {
        Assert.Equal(TaskStatus.Pending,
            TaskStateTransitions.Transition(TaskStatus.Failed, TaskStatus.Pending));
    }

    [Fact]
    public void 完成_转_处理中_非法抛异常()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TaskStateTransitions.Transition(TaskStatus.Completed, TaskStatus.Processing));
    }

    [Fact]
    public void 已取消_转_待处理_不可重试抛异常()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TaskStateTransitions.Transition(TaskStatus.Cancelled, TaskStatus.Pending));
    }

    [Fact]
    public void 待处理_转_完成_非法抛异常()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TaskStateTransitions.Transition(TaskStatus.Pending, TaskStatus.Completed));
    }

    [Fact]
    public void CanTransition_合法返回true()
    {
        Assert.True(TaskStateTransitions.CanTransition(TaskStatus.PendingValidation, TaskStatus.Pending));
    }

    [Fact]
    public void CanTransition_非法返回false()
    {
        Assert.False(TaskStateTransitions.CanTransition(TaskStatus.Completed, TaskStatus.Processing));
    }
}
