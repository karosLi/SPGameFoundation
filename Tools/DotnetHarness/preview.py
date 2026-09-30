#!/usr/bin/env python3
"""Draws the JSON frames exported by SnakePreviewExport into PNGs (top-down, same colours as the game)."""
import json, sys, glob, os
from PIL import Image, ImageDraw

PALETTE = [(255,107,107),(255,199,77),(140,230,102),(89,204,255),(191,140,255),(255,140,217),(102,255,217),(255,255,153)]
SIZE = 900

def render(path, out):
    f = json.load(open(path))
    fx, fy = f["focus"]; half = f["half"]
    scale = SIZE / (2 * half)
    def P(x, y): return ((x - fx + half) * scale, (half - (y - fy)) * scale)
    img = Image.new("RGB", (SIZE, SIZE), (18, 23, 33))
    d = ImageDraw.Draw(img, "RGBA")
    # background dots
    step = 6
    import math
    x0 = math.floor((fx - half) / step) * step; y0 = math.floor((fy - half) / step) * step
    for gx in range(int(2 * half / step) + 2):
        for gy in range(int(2 * half / step) + 2):
            px, py = P(x0 + gx * step, y0 + gy * step)
            d.ellipse([px - 2, py - 2, px + 2, py + 2], fill=(31, 38, 54))
    rx0, ry0, rx1, ry1 = f["region"]
    a, b = P(rx0, ry1); c, e = P(rx1, ry0)
    d.rectangle([a, b, c, e], outline=(230, 64, 77), width=4)
    for x, y, r in f["portals"]:
        cx, cy = P(x, y)
        d.ellipse([cx - r * 1.3 * scale, cy - r * 1.3 * scale, cx + r * 1.3 * scale, cy + r * 1.3 * scale], fill=(100, 80, 255, 70))
        d.ellipse([cx - r * scale, cy - r * scale, cx + r * scale, cy + r * scale], fill=(130, 150, 255, 140))
    for x, y, r, c in f["food"]:
        cx, cy = P(x, y); rr = max(r * scale, 1.5)
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=PALETTE[c & 7])
    snakes = sorted(f["snakes"], key=lambda s: (s["player"], s["mass"]))
    for s in snakes:
        nodes = s["nodes"]; r = s["r"]; n = len(nodes)
        alpha = int(255 * s["alpha"])
        layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
        ld = ImageDraw.Draw(layer)
        for j in range(n - 1, -1, -1):  # tail first so the head ends on top
            t = j / max(n - 1, 1)
            taper = 1.0 if t < 0.75 else 1.0 - (t - 0.75) / 0.25 * 0.45
            rr = r * taper * (1.12 if j == 0 else 1.0) * scale
            col = s["b"] if s["stripe"] > 0 and (j // s["stripe"]) % 2 == 1 else s["a"]
            cx, cy = P(*nodes[j])
            ld.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=tuple(col) + (255,))
        if alpha < 255:  # whole-snake translucency, no self-overlap darkening (like the equal-depth trick)
            layer.putalpha(layer.getchannel("A").point(lambda v: alpha if v else 0))
        img.paste(layer, (0, 0), layer)
        hx, hy = P(*nodes[0]); hdx, hdy = s["heading"]
        for side in (-1, 1):
            ex = hx + (hdx * 0.35 * r - hdy * 0.45 * r * side) * scale
            ey = hy - (hdy * 0.35 * r + hdx * 0.45 * r * side) * scale
            er = r * 0.38 * scale
            d.ellipse([ex - er, ey - er, ex + er, ey + er], fill=(255, 255, 255))
            px, py = ex + hdx * er * 0.35, ey - hdy * er * 0.35
            d.ellipse([px - er * 0.55, py - er * 0.55, px + er * 0.55, py + er * 0.55], fill=(20, 20, 30))
        if s["player"]:
            d.text((hx + 12, hy - 24), "YOU", fill=(255, 255, 255))
    d.text((10, 10), f"snakes in view: {len(snakes)}  food: {len(f['food'])}", fill=(230, 230, 230))
    img.save(out)

if __name__ == "__main__":
    src = sys.argv[1]; dst = sys.argv[2]
    os.makedirs(dst, exist_ok=True)
    for p in sorted(glob.glob(os.path.join(src, "frame*.json"))):
        out = os.path.join(dst, os.path.basename(p).replace(".json", ".png"))
        render(p, out); print(out)
