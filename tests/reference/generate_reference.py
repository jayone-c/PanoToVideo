"""生成 py360convert 参考帧，供 C# EquirectRenderer 几何对照（PRD Testing、开发规划 §5）。

用法：python tests/reference/generate_reference.py
前置：pip install py360convert numpy pillow
产出：
  tests/fixtures/erp_8192x4096.bin    原始 ERP 像素（行优先 RGB）
  tests/reference/ref_<yaw>_<pitch>.bin  各视角参考帧（行优先 RGB，320×320）

对照在 C# 侧 EquirectProjectionComparisonTests 中以 PSNR ≥ 40dB 验证。
"""
import os
import numpy as np
from PIL import Image
import py360convert

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FIX = os.path.join(ROOT, "fixtures")
REF = os.path.join(ROOT, "reference")
os.makedirs(FIX, exist_ok=True)
os.makedirs(REF, exist_ok=True)

# 源 ERP 图（8192×4096，2:1）
ERP_JPG = r"D:/work/720download/_diagnose_justeasy_faces/scene001_equirect_seam_blend_8192x4096.jpg"
img = Image.open(ERP_JPG).convert("RGB")
erp = np.array(img)
H, W = erp.shape[:2]
assert (W, H) == (8192, 4096), f"期望 8192x4096，实际 {W}x{H}"
print(f"ERP: {W}x{H}")

# 存 ERP 为 raw RGB（行优先，与 C# Rgb[] 字节序一致）
erp.tofile(os.path.join(FIX, "erp_8192x4096.bin"))

# 正方形输出，排除宽高比推导干扰，聚焦投影方向正确性
OUT_W, OUT_H = 320, 320
HFOV = 75.0  # 正方形下 vFov = hFov = 75

cases = [
    (0, 0),
    (90, 0),
    (180, 0),
    (270, 0),
    (0, 30),
    (0, -30),
    (45, 15),
]

for (yaw, pitch) in cases:
    # e2p: u_deg=yaw（水平角，向东为正），v_deg=pitch（垂直角，向上为正）
    out = py360convert.e2p(
        erp, fov_deg=(HFOV, HFOV),
        u_deg=yaw, v_deg=pitch,
        out_hw=(OUT_H, OUT_W), mode='bilinear')
    out = np.ascontiguousarray(out.astype(np.uint8))
    out.tofile(os.path.join(REF, f"ref_{yaw}_{pitch}.bin"))
    print(f"ref_{yaw}_{pitch}.bin {out.shape}")
