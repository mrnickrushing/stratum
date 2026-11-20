"""Draws Stratum's icon: one block seen from above a corner, its sides cut through the layers of the world
(grass, soil, stone, slate with a seam of ember ore). Writes Assets/Art/Icon.png (1024) and Tools/brand/stratum.ico."""
import random, os
from PIL import Image, ImageDraw, ImageFilter

S = 1024
root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
rng = random.Random(7)

def jitter(c, k=14):
    return tuple(max(0, min(255, v + rng.randint(-k, k))) for v in c)

# Texel colours on a 16x16 face: top is grass, sides run grass lip, soil, stone, slate, with ore.
def top_texel(i, j):
    return jitter((92, 168, 64) if rng.random() > 0.15 else (110, 186, 74), 16)

def side_texel(i, row):
    if row < 2 or (row == 2 and rng.random() < 0.5): return jitter((84, 156, 58), 14)
    if row < 7: return jitter((128, 88, 56), 12)
    if row < 12:
        return jitter((132, 134, 140), 12) if rng.random() > 0.1 else jitter((100, 102, 108), 8)
    c = jitter((70, 68, 80), 8)
    return c

ore = {(5, 13), (6, 13), (6, 14), (11, 12), (12, 13)}

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
# A rounded dark tile behind, so the block reads on any desktop.
bg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(bg)
for k in range(60):
    t = k / 59
    col = (int(26 + 30 * (1 - t)), int(30 + 26 * (1 - t)), int(46 + 34 * (1 - t)), 255)
    m = int(40 + k * 0)
    d.rounded_rectangle([m, m + k * 0, S - m, S - m], radius=200, fill=col) if k == 0 else None
grad = Image.new("RGBA", (S, S))
gd = ImageDraw.Draw(grad)
for y in range(S):
    t = y / S
    gd.line([(0, y), (S, y)], fill=(int(40 + 40 * (1 - t)), int(52 + 50 * (1 - t)), int(78 + 70 * (1 - t)), 255))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], radius=210, fill=255)
img.paste(grad, (0, 0), mask)
# A warm glow under the block.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([200, 640, 824, 900], fill=(255, 120, 40, 120))
img = Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(60)))

# Isometric cube: centre top vertex, edge length in px.
cx, cy, e = S / 2, 190, 360
dx, dy = e * 0.866, e * 0.5
top = [(cx, cy), (cx + dx, cy + dy), (cx, cy + 2 * dy), (cx - dx, cy + dy)]
h = e
draw = ImageDraw.Draw(img)

def quad(p0, u, v, i, j, n, colour, shade):
    a = (p0[0] + u[0] * i / n + v[0] * j / n, p0[1] + u[1] * i / n + v[1] * j / n)
    pts = [a,
           (a[0] + u[0] / n, a[1] + u[1] / n),
           (a[0] + u[0] / n + v[0] / n, a[1] + u[1] / n + v[1] / n),
           (a[0] + v[0] / n, a[1] + v[1] / n)]
    c = tuple(int(x * shade) for x in colour[:3]) + (255,)
    draw.polygon(pts, fill=c)

N = 16
# Top: from the back vertex, u toward right vertex, v toward left vertex.
for i in range(N):
    for j in range(N):
        quad(top[0], (dx, dy), (-dx, dy), i, j, N, top_texel(i, j), 1.0)
# Left face: from left vertex, u toward front vertex, v down.
for i in range(N):
    for r in range(N):
        c = (255, 150, 50) if (i, r) in ore else side_texel(i, r)
        quad(top[3], (dx, dy), (0, h), i, r, N, c, 1.0 if (i, r) in ore else 0.82)
# Right face: from front vertex, u toward right vertex, v down.
for i in range(N):
    for r in range(N):
        c = (255, 170, 60) if (N - 1 - i, r) in ore else side_texel(i, r)
        quad(top[2], (dx, -dy), (0, h), i, r, N, c, 1.0 if (N - 1 - i, r) in ore else 0.62)
# Crisp edges.
edge = (20, 22, 30, 255)
draw.line([top[3], top[0], top[1]], fill=(240, 255, 220, 120), width=4)
draw.line([top[3], top[2], top[1]], fill=edge, width=5)
draw.line([top[2], (top[2][0], top[2][1] + h)], fill=edge, width=5)
draw.line([top[3], (top[3][0], top[3][1] + h), (top[2][0], top[2][1] + h), (top[1][0], top[1][1] + h), top[1]], fill=edge, width=6)

os.makedirs(os.path.join(root, "Assets/Art"), exist_ok=True)
img.save(os.path.join(root, "Assets/Art/Icon.png"))
img.save(os.path.join(root, "Tools/brand/stratum.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
img.resize((256, 256), Image.LANCZOS).save(os.path.join(root, "Tools/brand/stratum-256.png"))
print("icon written")
