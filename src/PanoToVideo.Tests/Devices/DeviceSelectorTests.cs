using PanoToVideo.Core.Devices;

namespace PanoToVideo.Tests.Devices;

/// <summary>
/// 设备选择规则 TDD 测试（红阶段先行）。
/// 契约：开发规划 §7、PRD #2。
/// 规则：过滤 IsSoftware；过滤无 MF 硬件编码器者（排除虚拟显示适配器）；
/// 不得仅凭 DedicatedVideoMemory==0 排除（核显可能为 0）；按显存降序选首选。
/// HasHardwareEncoder 由 Render 的 MF 探测填充，Core 只消费此字段做纯逻辑过滤。
/// </summary>
public class DeviceSelectorTests
{
    private static readonly AdapterCandidate Rtx4090D = new(
        Luid: 0x0001_0000_0000_0001L,
        DedicatedVideoMemoryBytes: 24L * 1024 * 1024 * 1024,
        IsSoftware: false,
        HasHardwareEncoder: true,
        Description: "NVIDIA GeForce RTX 4090 D");

    private static readonly AdapterCandidate AmdIgpu = new(
        Luid: 0x0001_0000_0000_0002L,
        DedicatedVideoMemoryBytes: 512L * 1024 * 1024,
        IsSoftware: false,
        HasHardwareEncoder: true,
        Description: "AMD Radeon Graphics");

    private static readonly AdapterCandidate AmdIgpuZeroVram = new(
        Luid: 0x0001_0000_0000_0003L,
        DedicatedVideoMemoryBytes: 0,
        IsSoftware: false,
        HasHardwareEncoder: true,
        Description: "AMD Radeon (zero reported vram)");

    private static readonly AdapterCandidate VirtualDisplay1 = new(
        Luid: 0x0001_0000_0000_0004L,
        DedicatedVideoMemoryBytes: 0,
        IsSoftware: false,
        HasHardwareEncoder: false,
        Description: "USB Mobile Monitor Virtual Display");

    private static readonly AdapterCandidate VirtualDisplay2 = new(
        Luid: 0x0001_0000_0000_0005L,
        IsSoftware: false,
        HasHardwareEncoder: false,
        Description: "Todesk Virtual Display Adapter",
        DedicatedVideoMemoryBytes: 0);

    private static readonly AdapterCandidate WarpSoftware = new(
        Luid: 0x0001_0000_0000_0006L,
        DedicatedVideoMemoryBytes: 0,
        IsSoftware: true,
        HasHardwareEncoder: true,
        Description: "Microsoft Basic Render Driver (WARP)");

    private readonly DeviceSelector _sut = new();

    [Fact]
    public void 混合环境_首选4090D_候选含核显_排除虚拟与WARP()
    {
        var candidates = new[]
        {
            VirtualDisplay1, AmdIgpu, Rtx4090D, VirtualDisplay2, WarpSoftware,
        };

        var selected = _sut.SelectEligible(candidates);
        var preferred = _sut.SelectPreferred(candidates);

        Assert.Equal(Rtx4090D, preferred);
        Assert.Contains(Rtx4090D, selected);
        Assert.Contains(AmdIgpu, selected);
        Assert.DoesNotContain(VirtualDisplay1, selected);
        Assert.DoesNotContain(VirtualDisplay2, selected);
        Assert.DoesNotContain(WarpSoftware, selected);
    }

    [Fact]
    public void 无独显_核显被选_即使显存为零()
    {
        var candidates = new[] { VirtualDisplay1, AmdIgpuZeroVram };

        var preferred = _sut.SelectPreferred(candidates);

        Assert.Equal(AmdIgpuZeroVram, preferred);
    }

    [Fact]
    public void 仅虚拟适配器_无可用设备()
    {
        var candidates = new[] { VirtualDisplay1, VirtualDisplay2 };

        var preferred = _sut.SelectPreferred(candidates);
        var selected = _sut.SelectEligible(candidates);

        Assert.Null(preferred);
        Assert.Empty(selected);
    }

    [Fact]
    public void WARP软件适配器_被过滤_即使有编码器()
    {
        var preferred = _sut.SelectPreferred(new[] { WarpSoftware });
        Assert.Null(preferred);
    }

    [Fact]
    public void 独显与核显共存_独显优先()
    {
        var candidates = new[] { AmdIgpu, Rtx4090D };

        var selected = _sut.SelectEligible(candidates);

        Assert.Equal(Rtx4090D, selected[0]);
        Assert.Equal(AmdIgpu, selected[1]);
    }

    [Fact]
    public void 选定设备必须是渲染与编码同LUID()
    {
        // 规划不变式：渲染与编码同 LUID。HasHardwareEncoder 已含同 LUID 探测通过语义，
        // 故入选候选天然满足"该 LUID 上有硬件编码器"。此处验证首选的 LUID 一致性字段。
        var preferred = _sut.SelectPreferred(new[] { Rtx4090D, AmdIgpu });

        Assert.NotNull(preferred);
        Assert.Equal(Rtx4090D.Luid, preferred!.Luid);
    }

    [Fact]
    public void 显存相同_保持输入顺序稳定()
    {
        var a = Rtx4090D with { Description = "A", Luid = 1 };
        var b = Rtx4090D with { Description = "B", Luid = 2 };

        var selected = _sut.SelectEligible(new[] { a, b });

        Assert.Equal("A", selected[0].Description);
        Assert.Equal("B", selected[1].Description);
    }
}
