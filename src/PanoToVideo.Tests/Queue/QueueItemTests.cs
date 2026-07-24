using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Queue;
using TaskStatus = PanoToVideo.Core.Queue.TaskStatus;

namespace PanoToVideo.Tests.Queue;

/// <summary>
/// 队列项模型 TDD 测试（开发规划阶段2任务1、任务2）。
/// QueueItem：缩略图、源文件名、尺寸、状态、进度、实际FPS、ETA、输出路径、错误原因。
/// 封装状态转换（委托 TaskStateTransitions，非法转换抛异常）。
/// </summary>
public class QueueItemTests
{
    private static QueueItem NewItem(string name = "scene_equirectangular_8192x4096.jpg", int w = 8192, int h = 4096) =>
        new(name, w, h);

    [Fact]
    public void 新建项_初始状态为待校验_进度为零()
    {
        var item = NewItem();
        Assert.Equal(TaskStatus.PendingValidation, item.Status);
        Assert.Equal(0, item.Progress.FramesDone);
        Assert.Equal(0, item.Progress.ProgressFraction);
        Assert.Null(item.OutputPath);
        Assert.Null(item.ErrorMessage);
    }

    [Fact]
    public void 待校验转待处理_记录缩略图与尺寸()
    {
        var item = NewItem();
        var thumb = new byte[] { 1, 2, 3 };

        item.TransitionTo(TaskStatus.Pending);
        item.SetThumbnail(thumb);

        Assert.Equal(TaskStatus.Pending, item.Status);
        Assert.Equal(thumb, item.Thumbnail);
        Assert.Equal(8192, item.Width);
        Assert.Equal(4096, item.Height);
        Assert.Equal("等待导出", item.StatusDisplay);
        Assert.Equal("—", item.ProgressText);
    }

    [Fact]
    public void 处理中_更新帧进度与FPS()
    {
        var item = NewItem();
        item.TransitionTo(TaskStatus.Pending);
        item.TransitionTo(TaskStatus.Processing);

        item.UpdateProgress(framesDone: 90, totalFrames: 180, projectionFps: 110, encodingFps: 115, elapsed: TimeSpan.FromSeconds(1));

        Assert.Equal(90, item.Progress.FramesDone);
        Assert.Equal(180, item.Progress.TotalFrames);
        Assert.Equal(0.5, item.Progress.ProgressFraction, 3);
        Assert.Equal(110, item.Progress.ProjectionFps);
        Assert.Equal(115, item.Progress.EncodingFps);
    }

    [Fact]
    public void 处理中_ETA由剩余帧与FPS推导()
    {
        var item = NewItem();
        item.TransitionTo(TaskStatus.Pending);
        item.TransitionTo(TaskStatus.Processing);
        // 90/180完成，已耗时1s -> 剩余90帧，按min(投影,编码)=110fps -> ETA≈0.818s
        item.UpdateProgress(90, 180, 110, 115, TimeSpan.FromSeconds(1));

        Assert.True(item.Progress.Eta.HasValue);
        Assert.True(item.Progress.Eta!.Value.TotalSeconds < 1.0); // 0.818s
        Assert.True(item.Progress.Eta!.Value.TotalSeconds > 0.7);
    }

    [Fact]
    public void 完成态_记录输出路径与平均FPS()
    {
        var item = NewItem();
        item.TransitionTo(TaskStatus.Pending);
        item.TransitionTo(TaskStatus.Processing);
        item.UpdateProgress(180, 180, 110, 115, TimeSpan.FromSeconds(1.6));

        item.TransitionTo(TaskStatus.Completed);
        item.SetOutput("/out/exports/scene.mp4", averageFps: 112.5);

        Assert.Equal(TaskStatus.Completed, item.Status);
        Assert.Equal("/out/exports/scene.mp4", item.OutputPath);
        Assert.Equal(112.5, item.AverageFps);
    }

    [Fact]
    public void 失败态_记录错误原因_可重试回待处理()
    {
        var item = NewItem();
        item.TransitionTo(TaskStatus.Pending);
        item.TransitionTo(TaskStatus.Processing);

        item.TransitionTo(TaskStatus.Failed);
        item.SetError("硬件编码器不可用");

        Assert.Equal(TaskStatus.Failed, item.Status);
        Assert.Equal("硬件编码器不可用", item.ErrorMessage);

        item.TransitionTo(TaskStatus.Pending); // 重试
        Assert.Equal(TaskStatus.Pending, item.Status);
    }

    [Fact]
    public void 已取消态_不可重试抛异常()
    {
        var item = NewItem();
        item.TransitionTo(TaskStatus.Pending);
        item.TransitionTo(TaskStatus.Cancelled);

        Assert.Throws<InvalidOperationException>(() => item.TransitionTo(TaskStatus.Pending));
    }

    [Fact]
    public void 非法转换_抛异常()
    {
        var item = NewItem();
        // 待校验不能直接到完成
        Assert.Throws<InvalidOperationException>(() => item.TransitionTo(TaskStatus.Completed));
    }
}
