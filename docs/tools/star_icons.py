"""Holo star icons for the favourite toggle: Resources/CIC/Ui/Star.png (filled) and StarEmpty.png (outline).

White on transparent (tinted by the UI Image colour), soft outer glow, anti-aliased from a signed distance
to the five-point star. Pure stdlib: python3 docs/tools/star_icons.py
"""
import math
import os
import struct
import zlib

SIZE = 128
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Core", "Resources", "CIC", "Ui")


def star_points(cx, cy, r_out, r_in):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        r = r_out if i % 2 == 0 else r_in
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    qx, qy = ax + t * dx - px, ay + t * dy - py
    return math.hypot(qx, qy)


def inside(px, py, pts):
    c = False
    j = len(pts) - 1
    for i in range(len(pts)):
        xi, yi = pts[i]
        xj, yj = pts[j]
        if (yi > py) != (yj > py) and px < (xj - xi) * (py - yi) / (yj - yi) + xi:
            c = not c
        j = i
    return c


def sdf(px, py, pts):
    d = min(seg_dist(px, py, *pts[i], *pts[(i + 1) % len(pts)]) for i in range(len(pts)))
    return -d if inside(px, py, pts) else d


def render(filled):
    pts = star_points(SIZE / 2, SIZE / 2 + 4, SIZE * 0.40, SIZE * 0.17)
    rows = []
    for y in range(SIZE):
        row = bytearray([0])
        for x in range(SIZE):
            d = sdf(x + 0.5, y + 0.5, pts)
            if filled:
                core = max(0.0, min(1.0, 0.5 - d))
                # Brighter rim just inside the edge reads as a lit holo plate.
                rim = max(0.0, 1.0 - abs(d + 3.0) / 3.0) * 0.35
                body = core * (0.78 + rim)
            else:
                stroke = 4.0
                body = max(0.0, min(1.0, stroke * 0.5 + 0.5 - abs(d + stroke * 0.5)))
            glow = math.exp(-max(0.0, d) / 7.0) * 0.32 if d > 0 else 0.0
            a = max(body, glow)
            lum = 1.0 if body >= glow else 0.85
            v = int(round(255 * lum))
            row += bytes([v, v, v, int(round(255 * min(1.0, a)))])
        rows.append(bytes(row))
    raw = b"".join(rows)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    return png


if __name__ == "__main__":
    for name, filled in (("Star.png", True), ("StarEmpty.png", False)):
        with open(os.path.join(OUT, name), "wb") as f:
            f.write(render(filled))
        print("wrote", name)
