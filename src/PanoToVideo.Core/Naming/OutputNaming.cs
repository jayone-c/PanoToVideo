namespace PanoToVideo.Core.Naming;

/// <summary>
/// 输出命名与重名递增（开发规划 §阶段2任务4、§8）。
/// 格式：{原文件名}_{宽}x{高}_{时长}s_{旋转度}deg.mp4
/// 重名追加 _1/_2... 最小可用序号，不覆盖。默认与原图保存在同一目录。
/// </summary>
public static class OutputNaming
{
    public static string BuildFileName(
        string sourceNameNoExt, int width, int height, int durationSeconds, int rotationDegrees) =>
        $"{sourceNameNoExt}_{width}x{height}_{durationSeconds}s_{rotationDegrees}deg.mp4";

    public static string CombineExportsDir(string baseDir) => baseDir;

    /// <summary>
    /// 在 dir 下为 baseName 解析不冲突的唯一路径。existingFiles 为 dir 下已存在的文件名集合。
    /// 存在重名时追加最小可用序号 _N（大小写不敏感），不覆盖既有文件。
    /// </summary>
    public static string ResolveUniquePath(
        string dir, string baseName, IReadOnlyCollection<string> existingFiles)
    {
        var existing = new HashSet<string>(existingFiles, StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(baseName))
            return Path.Combine(dir, baseName);

        var (stem, ext) = SplitExtension(baseName);
        for (int i = 1; ; i++)
        {
            var candidate = $"{stem}_{i}{ext}";
            if (!existing.Contains(candidate))
                return Path.Combine(dir, candidate);
        }
    }

    private static (string stem, string ext) SplitExtension(string fileName)
    {
        var idx = fileName.LastIndexOf('.');
        return idx <= 0 ? (fileName, string.Empty) : (fileName[..idx], fileName[idx..]);
    }
}
