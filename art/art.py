# Рисует кицунэ по частям (хвост, тело, голова, лица) для трёх возрастов.
import json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops

SS = 4                      # суперсэмплинг
OUT = sys.argv[1] if len(sys.argv) > 1 else 'out'
os.makedirs(OUT, exist_ok=True)

FUR = (255, 142, 44)
FUR_DEEP = (232, 98, 26)
FUR_SHADE = (190, 62, 20)
CREAM = (255, 236, 200)
LINE = (92, 30, 18)
EAR_IN = (74, 26, 20)
GLOW_A = (255, 232, 120)
GLOW_B = (255, 70, 24)
EYE_RING = (255, 196, 64)
EYE_RED = (214, 22, 18)
EYE_DARK = (40, 10, 8)
MARK = (46, 16, 12)
NOSE = (52, 20, 16)
BLUSH = (255, 96, 60)


def bez(p0, p1, p2, p3, n=40):
    pts = []
    for i in range(n + 1):
        t = i / n
        a = (1 - t) ** 3; b = 3 * (1 - t) ** 2 * t; c = 3 * (1 - t) * t ** 2; d = t ** 3
        pts.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return pts


def path(segs):
    pts = []
    for s in segs:
        pts += bez(*s)
    return pts


def new(w, h):
    return Image.new('RGBA', (int(w * SS), int(h * SS)), (0, 0, 0, 0))


def mask_of(w, h, polys=(), ellipses=()):
    m = Image.new('L', (int(w * SS), int(h * SS)), 0)
    d = ImageDraw.Draw(m)
    for p in polys:
        d.polygon([(x * SS, y * SS) for x, y in p], fill=255)
    for (cx, cy, rx, ry) in ellipses:
        d.ellipse(((cx - rx) * SS, (cy - ry) * SS, (cx + rx) * SS, (cy + ry) * SS), fill=255)
    return m


def vgrad(size, y0, y1, c0, c1):
    w, h = size
    den = (y1 - y0) if abs(y1 - y0) > 1e-6 else 1e-6
    ys = np.clip((np.arange(h) / SS - y0) / den, 0, 1)[:, None]
    c0 = np.array(c0, float); c1 = np.array(c1, float)
    arr = c0[None, None, :] * (1 - ys[..., None]) + c1[None, None, :] * ys[..., None]
    arr = np.broadcast_to(arr, (h, w, 3)).astype(np.uint8)
    return Image.fromarray(arr, 'RGB')


def paint(base, mask, fill):
    """Кладёт заливку (цвет или картинку RGB) по маске."""
    if isinstance(fill, tuple):
        fill = Image.new('RGB', base.size, fill)
    layer = fill.convert('RGBA')
    layer.putalpha(mask)
    base.alpha_composite(layer)


def outline(base, mask, width=2.2, color=LINE, alpha=235):
    big = mask.filter(ImageFilter.MaxFilter(int(width * SS) * 2 + 1))
    ring = ImageChops.subtract(big, mask)
    col = Image.new('RGBA', base.size, color + (0,))
    col.putalpha(ring.point(lambda v: v * alpha // 255))
    out = Image.new('RGBA', base.size, (0, 0, 0, 0))
    out.alpha_composite(col)
    out.alpha_composite(base)
    return out


def shade(base, mask, dx, dy, blur, color=FUR_SHADE, strength=1.0):
    """Мягкая тень внутри формы: со стороны, противоположной свету."""
    sh = ImageChops.offset(mask, int(dx * SS), int(dy * SS))
    inner = ImageChops.subtract(mask, sh).filter(ImageFilter.GaussianBlur(blur * SS))
    inner = ImageChops.multiply(inner, mask)
    col = Image.new('RGBA', base.size, color + (0,))
    col.putalpha(inner.point(lambda v: min(255, int(v * strength))))
    base.alpha_composite(col)


def soft(base, cx, cy, rx, ry, color, alpha, blur, clip=None):
    m = mask_of(base.width / SS, base.height / SS, ellipses=[(cx, cy, rx, ry)])
    m = m.filter(ImageFilter.GaussianBlur(blur * SS)).point(lambda v: v * alpha // 255)
    if clip is not None:
        m = ImageChops.multiply(m, clip)
    col = Image.new('RGBA', base.size, color + (0,))
    col.putalpha(m)
    base.alpha_composite(col)


def finish(img, name, anchor):
    """Уменьшает до итогового размера, обрезает и запоминает смещение относительно якоря."""
    w, h = img.width // SS, img.height // SS
    small = img.resize((w, h), Image.LANCZOS)
    bb = small.getbbox()
    small = small.crop(bb)
    small.save(f'{OUT}/{name}.png')
    return {'x': bb[0] - anchor[0], 'y': bb[1] - anchor[1], 'w': small.width, 'h': small.height}


# ---------------------------------------------------------------- хвост

def tail(L, W, glow):
    cw, ch = W * 2.8, L + 50
    px, py = cw / 2, ch - 14              # точка крепления
    img = new(cw, ch)
    # осевая линия хвоста: лёгкий изгиб, кончик загибается
    c = bez((px, py), (px - W * 0.15, py - L * 0.35), (px + W * 0.35, py - L * 0.7), (px + W * 0.28, py - L), n=80)
    def width(t):
        return W * (0.36 + 0.64 * math.sin(math.pi * min(1, t ** 0.85) * 0.78)) * max(0.0, 1 - t ** 3) ** 0.5
    left, right = [], []
    for i, (x, y) in enumerate(c):
        t = i / (len(c) - 1)
        j = min(len(c) - 1, i + 1); k = max(0, i - 1)
        dx, dy = c[j][0] - c[k][0], c[j][1] - c[k][1]
        ln = math.hypot(dx, dy) or 1
        nx, ny = -dy / ln, dx / ln
        w = width(t) / 2
        # пушистые пряди по краю
        tuft = 1 + 0.09 * max(0, math.sin(t * 7 * math.pi)) ** 3 * (t > 0.12)
        tuft2 = 1 + 0.09 * max(0, math.sin(t * 7 * math.pi + 1.6)) ** 3 * (t > 0.12)
        left.append((x + nx * w * tuft, y + ny * w * tuft))
        right.append((x - nx * w * tuft2, y - ny * w * tuft2))
    m = mask_of(cw, ch, polys=[left + right[::-1]])
    paint(img, m, FUR)
    shade(img, m, -W * 0.2, 0, W * 0.14, strength=1.2)
    # светящийся кончик: плавный переход
    ramp = np.clip((py - L * 0.48 - np.arange(img.height)[:, None] / SS) / (L * 0.32), 0, 1)
    ramp = (ramp * ramp * (3 - 2 * ramp) * 255).astype(np.uint8).repeat(img.width, 1)
    tipm = ImageChops.multiply(Image.fromarray(ramp, 'L'), m)
    paint(img, tipm, vgrad(img.size, py - L * 0.55, py - L * 1.0, GLOW_B, GLOW_A))
    # мягкий блик вдоль хвоста
    soft(img, px + W * 0.12, py - L * 0.5, W * 0.13, L * 0.22, (255, 196, 120), 150, W * 0.09, clip=m)
    img = outline(img, m, 2.0)
    halo = tipm.filter(ImageFilter.GaussianBlur(10 * SS)).point(lambda v: int(v * 0.6 * glow))
    hl = Image.new('RGBA', img.size, GLOW_B + (0,)); hl.putalpha(halo)
    out = Image.new('RGBA', img.size, (0, 0, 0, 0)); out.alpha_composite(hl); out.alpha_composite(img)
    return out, (px, py)


# ---------------------------------------------------------------- тело

def body(k):
    """k: 0 малыш, 1 подросток, 2 взрослая. Холст 400x400, центр x=200, земля y=372."""
    s = [0.7, 0.85, 1.0][k]
    cx, gy = 200, 372
    img = new(400, 400)
    bw, bh = [50, 56, 60][k], [84, 108, 128][k]
    top = gy - bh
    sil = path([
        ((cx - bw * 0.42, top), (cx - bw * 0.7, top + bh * 0.3), (cx - bw * 1.08, top + bh * 0.62), (cx - bw * 1.0, gy - bh * 0.1)),
        ((cx - bw * 1.0, gy - bh * 0.1), (cx - bw * 0.95, gy + 2), (cx - bw * 0.4, gy + 2), (cx, gy + 2)),
        ((cx, gy + 2), (cx + bw * 0.4, gy + 2), (cx + bw * 0.95, gy + 2), (cx + bw * 1.0, gy - bh * 0.1)),
        ((cx + bw * 1.0, gy - bh * 0.1), (cx + bw * 1.08, top + bh * 0.62), (cx + bw * 0.7, top + bh * 0.3), (cx + bw * 0.42, top)),
    ])
    m = mask_of(400, 400, polys=[sil])
    paint(img, m, vgrad(img.size, top, gy, FUR, FUR_DEEP))
    shade(img, m, -bw * 0.22, -6, bw * 0.16, strength=1.2)
    shade(img, m, 0, -bh * 0.12, bh * 0.08, strength=0.6)
    # пушистый воротник под головой
    ruff = []
    n = 9
    for i in range(n * 2 + 1):
        t = i / (n * 2)
        ang = math.pi * (0.06 + 0.88 * t)
        rr = bw * 0.78 * (1.0 if i % 2 == 0 else 1.16)
        ruff.append((cx - math.cos(ang) * rr * 1.05, top + bh * 0.08 + math.sin(ang) * rr * 0.62))
    ruff = [(cx - bw * 0.8, top - 6)] + ruff + [(cx + bw * 0.8, top - 6)]
    cm = ImageChops.multiply(mask_of(400, 400, polys=[ruff]), m)
    paint(img, cm, CREAM)
    shade(img, cm, 0, -6, 4, strength=0.7)
    # лапки
    pm = mask_of(400, 400, ellipses=[(cx - bw * 0.36, gy - 6 * s, 17 * s, 12 * s), (cx + bw * 0.36, gy - 6 * s, 17 * s, 12 * s)])
    paws = new(400, 400)
    paint(paws, pm, (70, 30, 24))
    shade(paws, pm, 0, -5 * s, 4 * s, color=(30, 10, 8), strength=0.8)
    d = ImageDraw.Draw(paws)
    for sx in (-1, 1):
        for j in (-1, 0, 1):
            x = cx + sx * bw * 0.36 + j * 6 * s
            d.line(((x * SS, (gy - 2 * s) * SS), (x * SS, (gy + 2 * s) * SS)), fill=(150, 90, 70, 200), width=int(1.6 * SS))
    img = outline(img, m, 2.2)
    paws = outline(paws, pm, 2.0)
    img.alpha_composite(paws)
    if False:
        # красный шнурок с золотым колокольчиком
        d = ImageDraw.Draw(img)
        cyy = top + bh * 0.2
        pts = bez((cx - bw * 0.62, cyy - 6), (cx - bw * 0.3, cyy + 12), (cx + bw * 0.3, cyy + 12), (cx + bw * 0.62, cyy - 6))
        d.line([(x * SS, y * SS) for x, y in pts], fill=(214, 52, 74, 255), width=int(4.2 * SS), joint='curve')
        br = [6.5, 8.5][k - 1]
        by = cyy + 10 + br
        bell = new(400, 400)
        bm = mask_of(400, 400, ellipses=[(cx, by, br, br)])
        paint(bell, bm, vgrad(bell.size, by - br, by + br, (255, 236, 150), (222, 156, 40)))
        soft(bell, cx - br * 0.35, by - br * 0.4, br * 0.3, br * 0.25, (255, 255, 255), 220, 1)
        bell = outline(bell, bm, 1.4, (150, 96, 30))
        db = ImageDraw.Draw(bell)
        db.line((((cx - br * 0.6) * SS, (by + br * 0.25) * SS), ((cx + br * 0.6) * SS, (by + br * 0.25) * SS)), fill=(150, 96, 30, 255), width=int(1.3 * SS))
        db.ellipse(((cx - 1.6) * SS, (by + br * 0.35) * SS, (cx + 1.6) * SS, (by + br * 0.35 + 3.2) * SS), fill=(110, 70, 20, 255))
        img.alpha_composite(bell)
    return img, top


# ---------------------------------------------------------------- голова

def head(k):
    s = [1.0, 1.0, 1.0][k]
    R = [64, 60, 56][k]
    cx, cy = 200, 200                     # центр головы в своём холсте
    img = new(400, 400)
    # уши
    eh = R * [1.05, 1.25, 1.4][k]
    ears = []
    for sx in (-1, 1):
        bx = cx + sx * R * 0.55
        tipx = cx + sx * R * 0.95
        ear = path([
            ((bx - sx * R * 0.42, cy - R * 0.55), (bx - sx * R * 0.3, cy - R * 0.9), (tipx - sx * R * 0.12, cy - R * 0.4 - eh), (tipx, cy - R * 0.35 - eh)),
            ((tipx, cy - R * 0.35 - eh), (tipx + sx * R * 0.08, cy - R * 0.3 - eh * 0.6), (bx + sx * R * 0.45, cy - R * 0.6), (bx + sx * R * 0.4, cy - R * 0.15)),
        ])
        ears.append(ear)
    em = mask_of(400, 400, polys=ears)
    layer = new(400, 400)
    paint(layer, em, FUR)
    shade(layer, em, 4, 4, 5, strength=1.0)
    # внутренняя часть уха
    for sx, ear in zip((-1, 1), ears):
        bx = cx + sx * R * 0.55
        tipx = cx + sx * R * 0.95
        inner = path([
            ((bx - sx * R * 0.2, cy - R * 0.6), (bx - sx * R * 0.1, cy - R * 0.9), (tipx - sx * R * 0.1, cy - R * 0.45 - eh * 0.85), (tipx - sx * R * 0.02, cy - R * 0.4 - eh * 0.85)),
            ((tipx - sx * R * 0.02, cy - R * 0.4 - eh * 0.85), (tipx + sx * R * 0.04, cy - R * 0.3 - eh * 0.5), (bx + sx * R * 0.3, cy - R * 0.6), (bx + sx * R * 0.22, cy - R * 0.3)),
        ])
        im = mask_of(400, 400, polys=[inner])
        paint(layer, im, vgrad(layer.size, cy - R * 0.4 - eh, cy - R * 0.4, (40, 12, 10), EAR_IN))
        # светящиеся кончики ушей
        tipm = mask_of(400, 400, ellipses=[(tipx, cy - R * 0.35 - eh, R * 0.22, R * 0.3)])
        paint(layer, ImageChops.multiply(tipm, em), vgrad(layer.size, cy - R * 0.35 - eh - R * 0.3, cy - R * 0.35 - eh + R * 0.3, (40, 12, 10), FUR))
    layer = outline(layer, em, 2.2)
    img.alpha_composite(layer)
    # голова с пушистыми щёчками
    face = []
    n = 60
    for i in range(n):
        a = 2 * math.pi * i / n
        r = R
        x = math.cos(a); y = math.sin(a)
        # щёки: пучки по бокам снизу
        if 0.15 < a < 1.3 or 1.84 < a < 2.99:
            spike = math.sin((a % 0.38) / 0.38 * math.pi)
            r = R * (1.0 + 0.16 * spike * (1 if y > 0 else 0))
        face.append((cx + x * r * 1.08, cy + y * r * 0.94))
    fm = mask_of(400, 400, polys=[face])
    hl = new(400, 400)
    paint(hl, fm, vgrad(hl.size, cy - R, cy + R, FUR, FUR_DEEP))
    shade(hl, fm, 0, -R * 0.2, R * 0.16, strength=0.9)
    shade(hl, fm, -R * 0.18, 0, R * 0.1, strength=0.5)
    # мордочка светлее
    soft(hl, cx, cy + R * 0.42, R * 0.6, R * 0.36, CREAM, 245, R * 0.08, clip=fm)
    # знак на лбу
    mark = mask_of(400, 400, polys=[path([
        ((cx, cy - R * 0.72), (cx + R * 0.12, cy - R * 0.55), (cx + R * 0.08, cy - R * 0.42), (cx, cy - R * 0.36)),
        ((cx, cy - R * 0.36), (cx - R * 0.08, cy - R * 0.42), (cx - R * 0.12, cy - R * 0.55), (cx, cy - R * 0.72)),
    ])])
    mark_alpha = [110, 190, 255][k]
    er = R * [0.27, 0.25, 0.235][k]; ex = R * 0.42; ey = cy + R * 0.05
    for sx in (-1, 1):
        x = cx + sx * ex
        wing = path([
            ((x - sx * er * 1.15, ey + er * 0.35), (x - sx * er * 0.2, ey + er * 1.9), (x + sx * er * 1.6, ey + er * 1.0), (x + sx * er * 2.25, ey - er * 1.25)),
            ((x + sx * er * 2.25, ey - er * 1.25), (x + sx * er * 1.15, ey - er * 0.2), (x + sx * er * 0.3, ey + er * 0.75), (x - sx * er * 1.15, ey + er * 0.35)),
        ])
        paint(hl, ImageChops.multiply(mask_of(400, 400, polys=[wing]), fm), MARK)
    hl2 = outline(hl, fm, 2.2)
    img.alpha_composite(hl2)
    mk = new(400, 400)
    paint(mk, mark.point(lambda v: v * mark_alpha // 255), vgrad(mk.size, cy - R * 0.72, cy - R * 0.36, GLOW_A, GLOW_B))
    glow = mark.filter(ImageFilter.GaussianBlur(5 * SS)).point(lambda v: v * mark_alpha // 400)
    gl = Image.new('RGBA', mk.size, GLOW_B + (0,)); gl.putalpha(glow)
    img.alpha_composite(gl); img.alpha_composite(mk)
    return img, R


# ---------------------------------------------------------------- лица

def faces(k, R):
    cx, cy = 200, 200
    er = R * [0.27, 0.25, 0.235][k]         # размер глаза
    ex = R * 0.42
    ey = cy + R * 0.05
    res = {}

    def base():
        return new(400, 400)

    def nose_mouth(img, mouth='w'):
        d = ImageDraw.Draw(img)
        ny = cy + R * 0.32
        d.polygon([((cx - R * 0.07) * SS, (ny - R * 0.03) * SS), ((cx + R * 0.07) * SS, (ny - R * 0.03) * SS), (cx * SS, (ny + R * 0.05) * SS)], fill=NOSE + (255,))
        lw = int(1.8 * SS)
        my = ny + R * 0.06
        if mouth == 'w':
            pts = bez((cx - R * 0.16, my + R * 0.02), (cx - R * 0.1, my + R * 0.1), (cx - R * 0.02, my + R * 0.08), (cx, my))
            pts2 = bez((cx, my), (cx + R * 0.02, my + R * 0.08), (cx + R * 0.1, my + R * 0.1), (cx + R * 0.16, my + R * 0.02))
            d.line([(x * SS, y * SS) for x, y in pts + pts2], fill=LINE + (230,), width=lw, joint='curve')
        elif mouth == 'open':
            d.ellipse(((cx - R * 0.1) * SS, (my) * SS, (cx + R * 0.1) * SS, (my + R * 0.16) * SS), fill=(140, 70, 110, 255), outline=LINE + (230,), width=lw)
        if mouth in ('open', 'smile'):
            fy = my + (R * 0.02 if mouth == 'open' else R * 0.06)
            for fx in (-1, 1):
                x0 = cx + fx * R * 0.085
                d.polygon([((x0 - R * 0.035) * SS, fy * SS), ((x0 + R * 0.035) * SS, fy * SS), (x0 * SS, (fy + R * 0.08) * SS)], fill=(255, 255, 255, 255))
        if mouth == 'sad':
            pts = bez((cx - R * 0.12, my + R * 0.1), (cx - R * 0.05, my + R * 0.02), (cx + R * 0.05, my + R * 0.02), (cx + R * 0.12, my + R * 0.1))
            d.line([(x * SS, y * SS) for x, y in pts], fill=LINE + (230,), width=lw, joint='curve')
        elif mouth == 'smile':
            pts = bez((cx - R * 0.16, my), (cx - R * 0.08, my + R * 0.16), (cx + R * 0.08, my + R * 0.16), (cx + R * 0.16, my))
            d.line([(x * SS, y * SS) for x, y in pts], fill=LINE + (230,), width=lw, joint='curve')

    def blush(img, a=110):
        for sx in (-1, 1):
            soft(img, cx + sx * R * 0.62, cy + R * 0.3, R * 0.17, R * 0.09, BLUSH, a, R * 0.05)

    def open_eyes(img, look=0.0):
        d = ImageDraw.Draw(img)
        for sx in (-1, 1):
            x = cx + sx * ex
            d.ellipse(((x - er) * SS, (ey - er * 1.12) * SS, (x + er) * SS, (ey + er * 1.12) * SS), fill=EYE_DARK + (255,))
            # радужка
            iris = new(400, 400)
            im = mask_of(400, 400, ellipses=[(x + look, ey + er * 0.1, er * 0.82, er * 0.95)])
            paint(iris, im, vgrad(iris.size, ey - er, ey + er, EYE_RED, EYE_RING))
            img.alpha_composite(iris)
            d = ImageDraw.Draw(img)
            d.ellipse(((x + look - er * 0.16) * SS, (ey - er * 0.78) * SS, (x + look + er * 0.16) * SS, (ey + er * 0.92) * SS), fill=EYE_DARK + (255,))
            # блики
            d.ellipse(((x - er * 0.55) * SS, (ey - er * 0.8) * SS, (x - er * 0.05) * SS, (ey - er * 0.3) * SS), fill=(255, 255, 255, 255))
            d.ellipse(((x + er * 0.25) * SS, (ey + er * 0.25) * SS, (x + er * 0.5) * SS, (ey + er * 0.5) * SS), fill=(255, 255, 255, 220))
            d.ellipse(((x - er) * SS, (ey - er * 1.12) * SS, (x + er) * SS, (ey + er * 1.12) * SS), outline=LINE + (255,), width=int(1.8 * SS))

    def arc_eyes(img, up=True):
        d = ImageDraw.Draw(img)
        for sx in (-1, 1):
            x = cx + sx * ex
            if up:   # счастливые ^ ^
                pts = bez((x - er, ey + er * 0.2), (x - er * 0.5, ey - er * 0.8), (x + er * 0.5, ey - er * 0.8), (x + er, ey + er * 0.2))
            else:    # закрытые (спит/моргает)
                pts = bez((x - er, ey), (x - er * 0.5, ey + er * 0.6), (x + er * 0.5, ey + er * 0.6), (x + er, ey))
            d.line([(px * SS, py * SS) for px, py in pts], fill=EYE_DARK + (255,), width=int(2.6 * SS), joint='curve')

    def sad_eyes(img):
        open_eyes(img)
        d = ImageDraw.Draw(img)
        for sx in (-1, 1):
            x = cx + sx * ex
            # приподнятые внутренние уголки бровей
            d.line((((x - sx * er * 0.9) * SS, (ey - er * 1.65) * SS), ((x + sx * er * 0.7) * SS, (ey - er * 1.3) * SS)), fill=LINE + (220,), width=int(2 * SS))
        # слезинка
        soft(img, cx + ex + er * 0.7, ey + er * 1.1, er * 0.18, er * 0.28, (150, 210, 255), 230, 0.5)

    f = base(); open_eyes(f); nose_mouth(f, 'w'); blush(f, 80); res['normal'] = f
    f = base(); open_eyes(f, -er * 0.25); nose_mouth(f, 'w'); blush(f, 80); res['lookl'] = f
    f = base(); open_eyes(f, er * 0.25); nose_mouth(f, 'w'); blush(f, 80); res['lookr'] = f
    f = base(); arc_eyes(f, False); nose_mouth(f, 'w'); blush(f, 80); res['blink'] = f
    f = base(); arc_eyes(f, True); nose_mouth(f, 'smile'); blush(f, 170); res['happy'] = f
    f = base(); sad_eyes(f); nose_mouth(f, 'sad'); res['sad'] = f
    f = base(); arc_eyes(f, False); nose_mouth(f, 'w'); blush(f, 60); res['sleep'] = f
    f = base(); arc_eyes(f, True); nose_mouth(f, 'open'); blush(f, 140); res['eat'] = f
    f = base(); open_eyes(f); nose_mouth(f, 'open'); blush(f, 100); res['talk'] = f
    return res


# ---------------------------------------------------------------- сборка

manifest = {}
for k, name in enumerate(['baby', 'teen', 'adult']):
    st = {}
    L = [112, 160, 190][k]; W = [62, 70, 72][k]
    t, piv = tail(L, W, [0.5, 0.8, 1.0][k])
    st['tail'] = finish(t, f'{name}_tail', piv)
    b, top = body(k)
    st['body'] = finish(b, f'{name}_body', (200, 372))
    h, R = head(k)
    st['head'] = finish(h, f'{name}_head', (200, 200))
    st['faces'] = {}
    for fn, img in faces(k, R).items():
        st['faces'][fn] = finish(img, f'{name}_face_{fn}', (200, 200))
    st['neckY'] = round(top - 372 - R * 0.62, 1)          # центр головы относительно земли
    st['tailBase'] = [0, -[26, 32, 38][k]]
    st['R'] = R
    manifest[name] = st
json.dump(manifest, open(f'{OUT}/manifest.json', 'w'), indent=1)
print('ok')
