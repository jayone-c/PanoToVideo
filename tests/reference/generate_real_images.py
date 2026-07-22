"""为真实 6000x3000 ERP 图生成几何对照参考帧（阶段4补测，真实纹理质量验证）。
输入：720/玄关1.jpg、720/茶室空间.jpg（原生 6000x3000，非缩放）
输出：每张图 36 矩阵 + 4 接缝 = 40 参考帧，与 GPU RenderFrameToRgba 对照 PSNR ≥ 40dB。
"""
import os
import sys
import numpy as np
from PIL import Image
import py360convert

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FIX = os.path.join(ROOT, "fixtures")
REF = os.path.join(ROOT, "reference")
os.makedirs(FIX, exist_ok=True)
os.makedirs(REF, exist_ok=True)

IMAGES = {
    "xuanguan1": r"D:/work/720-to-video/720/玄关1.jpg",
    "chashi": r"D:/work/720-to-video/720/茶室空间.jpg",
}

OUT = 320
YAWS = [0, 90, 180, 270]
PITCHES = [-30, 0, 30]
FOVS = [45, 75, 100]
SEAM_CASES = [(359, 0), (361, 0), (0, 0), (1, 0)]

total = 0
for name, jpg in IMAGES.items():
    erp = np.array(Image.open(jpg).convert("RGB"))
    H, W = erp.shape[:2]
    assert (W, H) == (6000, 3000), f"{name} 期望 6000x3000，实际 {W}x{H}"
    print(f"{name}: {W}x{H}")
    erp.tofile(os.path.join(FIX, f"erp_{name}_6000x3000.bin"))

    for fov in FOVS:
        for yaw in YAWS:
            for pitch in PITCHES:
                out = py360convert.e2p(erp, fov_deg=(fov, fov), u_deg=yaw, v_deg=pitch,
                                        out_hw=(OUT, OUT), mode='bilinear')
                np.ascontiguousarray(out.astype(np.uint8)).tofile(
                    os.path.join(REF, f"{name}_ref_fov{fov}_yaw{yaw}_pitch{pitch}.bin"))
                total += 1
    for yaw, pitch in SEAM_CASES:
        out = py360convert.e2p(erp, fov_deg=(75, 75), u_deg=yaw, v_deg=pitch,
                                out_hw=(OUT, OUT), mode='bilinear')
        np.ascontiguousarray(out.astype(np.uint8)).tofile(
            os.path.join(REF, f"{name}_seam_yaw{yaw}_pitch{pitch}.bin"))
        total += 1

print(f"生成 {total} 个参考帧 -> {REF}")
