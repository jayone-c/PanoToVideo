"""生成 36 组合几何对照参考帧（开发规划阶段1任务5）。
Yaw {0,90,180,270} × Pitch {-30,0,30} × FOV {45,75,100} = 36 组合 + 接缝专项。
输出 320×320（正方形，排除宽高比干扰），与 GPU RenderFrameToRgba 对照 PSNR ≥ 40dB。
"""
import os
import numpy as np
from PIL import Image
import py360convert

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FIX = os.path.join(ROOT, "fixtures")
REF = os.path.join(ROOT, "reference")
os.makedirs(REF, exist_ok=True)

ERP_JPG = r"D:/work/720download/_diagnose_justeasy_faces/scene001_equirect_seam_blend_8192x4096.jpg"
erp = np.array(Image.open(ERP_JPG).convert("RGB"))
H, W = erp.shape[:2]
assert (W, H) == (8192, 4096)
erp.tofile(os.path.join(FIX, "erp_8192x4096.bin"))

OUT = 320
YAWS = [0, 90, 180, 270]
PITCHES = [-30, 0, 30]
FOVS = [45, 75, 100]

count = 0
for fov in FOVS:
    for yaw in YAWS:
        for pitch in PITCHES:
            out = py360convert.e2p(erp, fov_deg=(fov, fov), u_deg=yaw, v_deg=pitch,
                                    out_hw=(OUT, OUT), mode='bilinear')
            np.ascontiguousarray(out.astype(np.uint8)).tofile(
                os.path.join(REF, f"ref_fov{fov}_yaw{yaw}_pitch{pitch}.bin"))
            count += 1

# 接缝专项：Yaw 接近 0/360 接缝两侧 + 背面
SEAM_CASES = [(359, 0), (361, 0), (0, 0), (1, 0)]
for yaw, pitch in SEAM_CASES:
    out = py360convert.e2p(erp, fov_deg=(75, 75), u_deg=yaw, v_deg=pitch,
                            out_hw=(OUT, OUT), mode='bilinear')
    np.ascontiguousarray(out.astype(np.uint8)).tofile(
        os.path.join(REF, f"seam_yaw{yaw}_pitch{pitch}.bin"))
    count += 1

print(f"生成 {count} 个参考帧 -> {REF}")
