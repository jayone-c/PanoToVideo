using System.Runtime.InteropServices;

namespace PanoToVideo.App;

/// <summary>读取 Windows 当前可用物理内存，避免将 GC 堆上限误当作系统空闲内存。</summary>
internal static class SystemMemoryProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public static long GetAvailablePhysicalBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status) && status.AvailPhys <= long.MaxValue)
            return (long)status.AvailPhys;
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }
}
