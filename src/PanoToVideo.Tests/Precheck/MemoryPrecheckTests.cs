using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Tests.Precheck;

/// <summary>
/// 内存预检 TDD 测试（P0-5：100 张批量输入内存安全闸门）。
/// 契约来源：PRD「100 张批量输入需控制内存峰值」+ 开发规划 §阶段2任务4。
/// 纯算式：单帧 RGBA = w*h*4；×安全系数后与可用内存比较。
/// </summary>
public class MemoryPrecheckTests
{
    [Fact]
    public void EstimateRgbaBytes_8192x4096_等于134217728字节()
    {
        // 8192 * 4096 * 4 = 134,217,728 (128 MB)
        var bytes = MemoryPrecheck.EstimateRgbaBytes(8192, 4096);

        Assert.Equal(134_217_728L, bytes);
    }

    [Fact]
    public void EstimateRgbaBytes_16000x8000_等于512MB()
    {
        // 16000 * 8000 * 4 = 512,000,000
        var bytes = MemoryPrecheck.EstimateRgbaBytes(16000, 8000);

        Assert.Equal(512_000_000L, bytes);
    }

    [Fact]
    public void Check_单张128MB_可用16GB_通过()
    {
        var single = MemoryPrecheck.EstimateRgbaBytes(8192, 4096); // 128MB
        var available = 16L * 1024 * 1024 * 1024; // 16GB

        var r = MemoryPrecheck.Check(single, available);

        Assert.True(r.CanProceed);
        Assert.Equal(string.Empty, r.Reason);
    }

    [Fact]
    public void Check_单张预估超过可用内存_拒绝并含实际值()
    {
        var single = MemoryPrecheck.EstimateRgbaBytes(8192, 4096); // 128MB
        var available = 100L * 1024 * 1024; // 100MB，远小于 128*1.5=192MB

        var r = MemoryPrecheck.Check(single, available);

        Assert.False(r.CanProceed);
        Assert.NotNull(r.Reason);
        Assert.Contains("内存不足", r.Reason);
        Assert.Contains("1.5", r.Reason);
    }

    [Fact]
    public void Check_默认安全系数1p5_放大预估()
    {
        var single = 100L; // 100 字节占位
        var available = 149L; // 100*1.5=150 > 149 -> 拒绝

        var r = MemoryPrecheck.Check(single, available);

        Assert.False(r.CanProceed);
        Assert.Equal(150L, r.EstimatedRgbaBytes);
    }

    [Fact]
    public void Check_自定义安全系数_按系数放大()
    {
        var single = 100L;
        var available = 200L;

        var r2x = MemoryPrecheck.Check(single, available, safetyFactor: 2.0);

        // 100*2=200 == 200，临界通过（estimated > available 才拒绝）
        Assert.True(r2x.CanProceed);
        Assert.Equal(200L, r2x.EstimatedRgbaBytes);
    }

    [Fact]
    public void Check_可用内存为零_拒绝()
    {
        var single = MemoryPrecheck.EstimateRgbaBytes(8192, 4096);

        var r = MemoryPrecheck.Check(single, availableBytes: 0);

        Assert.False(r.CanProceed);
        Assert.Contains("无法启动", r.Reason);
    }

    [Fact]
    public void Check_可用内存为负_拒绝()
    {
        var single = MemoryPrecheck.EstimateRgbaBytes(8192, 4096);

        var r = MemoryPrecheck.Check(single, availableBytes: -1);

        Assert.False(r.CanProceed);
    }

    [Fact]
    public void Check_安全系数非正_抛异常()
    {
        var single = 100L;
        var available = 1000L;

        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryPrecheck.Check(single, available, safetyFactor: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryPrecheck.Check(single, available, safetyFactor: -1));
    }

    [Fact]
    public void Check_100张批量场景_单张128MB_可用16GB_通过()
    {
        // P0-5 核心场景：100 张 ERP，最大单张 8192x4096 (128MB RGBA)
        // 按需解码模式下，峰值 ≈ 单张 RGBA + 渲染缓冲，safetyFactor=1.5 兜底
        var largest = MemoryPrecheck.EstimateRgbaBytes(8192, 4096);
        var available = 16L * 1024 * 1024 * 1024;

        var r = MemoryPrecheck.Check(largest, available);

        Assert.True(r.CanProceed);
    }
}
