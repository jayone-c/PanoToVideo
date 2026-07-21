namespace PanoToVideo.Core.Validation;

/// <summary>
/// 待校验图像的纯数据视图。IO 层（App/Render）负责从 JPEG/PNG 解码出头信息，
/// Core 只消费此模型，保证校验逻辑可单测、不依赖文件系统。
/// </summary>
public sealed record ImageInfo(int Width, int Height, bool IsCorrupt, string SourcePath);
