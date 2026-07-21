"""诊断 C# EquirectProjection 与 py360convert 的几何差异。
对比两种像素归一化：像素中心 (px+0.5)/W vs linspace 端点 px/(W-1)。
"""
import numpy as np
from PIL import Image
import py360convert

img = Image.open(r"D:/work/720download/_diagnose_justeasy_faces/scene001_equirect_seam_blend_8192x4096.jpg").convert("RGB")
erp = np.array(img)
EH, EW = erp.shape[:2]
print(f"ERP: {EW}x{EH}")

W, H = 320, 320
HFOV = 75


def render(norm_mode, yaw, pitch):
    px = np.arange(W); py = np.arange(H)
    Px, Py = np.meshgrid(px, py)
    if norm_mode == "center":
        xNdc = (Px + 0.5) / W * 2 - 1
        yNdc = 1 - (Py + 0.5) / H * 2
    else:  # linspace 端点对齐
        xNdc = Px / (W - 1) * 2 - 1
        yNdc = 1 - Py / (H - 1) * 2
    hRad = np.radians(HFOV) / 2
    dx = xNdc * np.tan(hRad); dy = yNdc * np.tan(hRad); dz = np.ones_like(xNdc)
    yr = np.radians(yaw); pr = np.radians(pitch)
    cp, sp = np.cos(pr), np.sin(pr); cy, sy = np.cos(yr), np.sin(yr)
    ax = dx; ay = dy * cp + dz * sp; az = -dy * sp + dz * cp
    wx = ax * cy + az * sy; wy = ay; wz = -ax * sy + az * cy
    lon = np.arctan2(wx, wz)
    length = np.sqrt(wx ** 2 + wy ** 2 + wz ** 2)
    lat = np.arcsin(wy / length)
    u = lon / (2 * np.pi) + 0.5; u = u - np.floor(u)
    v = 0.5 - lat / np.pi
    fx = u * EW - 0.5; fy = v * EH - 0.5
    x0 = np.floor(fx).astype(int); y0 = np.floor(fy).astype(int)
    tx = fx - x0; ty = fy - y0
    x0w = x0 % EW; x1w = (x0 + 1) % EW
    y0c = np.clip(y0, 0, EH - 1); y1c = np.clip(y0 + 1, 0, EH - 1)
    p00 = erp[y0c, x0w].astype(float); p10 = erp[y0c, x1w].astype(float)
    p01 = erp[y1c, x0w].astype(float); p11 = erp[y1c, x1w].astype(float)
    tx3 = tx[..., None]; ty3 = ty[..., None]
    top = p00 + (p10 - p00) * tx3; bot = p01 + (p11 - p01) * tx3
    val = top + (bot - top) * ty3
    return np.clip(np.round(val), 0, 255).astype(np.uint8)


def psnr(a, b):
    mse = np.mean((a.astype(float) - b.astype(float)) ** 2)
    return 10 * np.log10(255 * 255 / mse) if mse > 1e-10 else 100.0


for yaw, pitch in [(0, 0), (90, 0), (0, 30), (45, 15)]:
    ref = py360convert.e2p(erp, fov_deg=(HFOV, HFOV), u_deg=yaw, v_deg=pitch,
                            out_hw=(H, W), mode='bilinear').astype(np.uint8)
    mc = render("center", yaw, pitch)
    ml = render("linspace", yaw, pitch)
    print(f"yaw={yaw} pitch={pitch}: center={psnr(mc, ref):.2f}dB  linspace={psnr(ml, ref):.2f}dB")
