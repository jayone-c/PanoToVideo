namespace PanoToVideo.Core.Projection;

/// <summary>8 位 RGB 像素（行优先，无 alpha）。</summary>
public readonly record struct Rgb(byte R, byte G, byte B);
