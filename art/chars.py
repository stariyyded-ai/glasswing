# Рисует других хвостатых питомцев по тем же частям, что и кицунэ (art.py):
# хвост, тело, голова, лица для трёх возрастов. Запуск: python3 chars.py <id> <папка>
import json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops

SS = 4
CHAR = sys.argv[1]
OUT = sys.argv[2] if len(sys.argv) > 2 else 'out_' + CHAR
os.makedirs(OUT, exist_ok=True)

CHARS = {
    # песчаный тануки: круглые уши, толстый хвост с кольцами, синие узоры
    'shukaku': dict(
        FUR=(222, 186, 124), FUR_DEEP=(196, 150, 88), FUR_SHADE=(140, 96, 52), CREAM=(246, 230, 196),
        LINE=(74, 48, 30), EAR_IN=(60, 44, 70), GLOW_A=(255, 236, 170), GLOW_B=(214, 166, 90),
        IRIS=[((250, 214, 60), (255, 246, 160)), ((250, 214, 60), (255, 246, 160))], EYE_DARK=(24, 20, 30),
        PUPIL='star', MARK=(52, 70, 128), NOSE=(54, 40, 40), BLUSH=(240, 130, 90), PAWS=(96, 70, 50),
        ears='round', tail='tanuki', forehead='swirl', stripes=True, wings='mask', cheeks=True, glow=0.0,
        tails={'baby': [52], 'teen': [46], 'adult': [42]},
        tailLW={'baby': (112, 84), 'teen': (156, 104), 'adult': (190, 120)}),
    # кошка синего пламени: два хвоста-пламени, разные глаза
    'matatabi': dict(
        FUR=(70, 146, 246), FUR_DEEP=(40, 98, 214), FUR_SHADE=(24, 56, 150), CREAM=(214, 236, 255),
        LINE=(18, 32, 84), EAR_IN=(20, 30, 70), GLOW_A=(214, 248, 255), GLOW_B=(60, 170, 255),
        IRIS=[((240, 196, 40), (255, 240, 140)), ((60, 190, 110), (180, 250, 170))], EYE_DARK=(14, 20, 44),
        PUPIL='slit', MARK=(20, 34, 90), NOSE=(26, 40, 96), BLUSH=(150, 170, 255), PAWS=(28, 50, 120),
        ears='cat', tail='flame', forehead='flame', stripes=False, wings='fox', cheeks=True, glow=1.0,
        tails={'baby': [52], 'teen': [-60, 40], 'adult': [-62, 38]},
        tailLW={'baby': (120, 46), 'teen': (176, 52), 'adult': (212, 56)}),
    # бык-осьминог: рога, гладкая голова, щупальца вместо хвостов
    'gyuki': dict(
        FUR=(150, 98, 138), FUR_DEEP=(118, 70, 112), FUR_SHADE=(74, 42, 76), CREAM=(238, 214, 226),
        LINE=(44, 22, 46), EAR_IN=(90, 50, 86), GLOW_A=(240, 200, 255), GLOW_B=(170, 90, 230),
        IRIS=[((250, 170, 50), (255, 230, 130)), ((250, 170, 50), (255, 230, 130))], EYE_DARK=(30, 14, 30),
        PUPIL='round', MARK=(70, 34, 70), NOSE=(70, 36, 60), BLUSH=(255, 120, 170), PAWS=(70, 40, 70),
        ears='horns', tail='tentacle', forehead='none', stripes=False, wings='none', cheeks=False, glow=0.0,
        tails={'baby': [-52, 58], 'teen': [-62, -30, 34, 66], 'adult': [-68, -50, -32, -12, 12, 32, 50, 68]},
        tailLW={'baby': (100, 40), 'teen': (150, 44), 'adult': (176, 46)}),
}
C = CHARS[CHAR]
globals().update({k: v for k, v in C.items() if k.isupper()})


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
    if isinstance(fill, tuple):
        fill = Image.new('RGB', base.size, fill)
    layer = fill.convert('RGBA')
    layer.putalpha(mask)
    base.alpha_composite(layer)


def outline(base, mask, width=2.2, color=None, alpha=235):
    color = color or LINE
    big = mask.filter(ImageFilter.MaxFilter(int(width * SS) * 2 + 1))
    ring = ImageChops.subtract(big, mask)
    col = Image.new('RGBA', base.size, color + (0,))
    col.putalpha(ring.point(lambda v: v * alpha // 255))
    out = Image.new('RGBA', base.size, (0, 0, 0, 0))
    out.alpha_composite(col)
    out.alpha_composite(base)
    return out


def shade(base, mask, dx, dy, blur, color=None, strength=1.0):
    color = color or FUR_SHADE
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


def stroke(d, pts, color, width):
    d.line([(x * SS, y * SS) for x, y in pts], fill=color, width=int(width * SS), joint='curve')


def ribbon(center, width_of, tuft=None):
    """Полоса вдоль осевой линии: возвращает левый и правый края."""
    left, right = [], []
    n = len(center)
    for i, (x, y) in enumerate(center):
        t = i / (n - 1)
        j = min(n - 1, i + 1); k = max(0, i - 1)
        dx, dy = center[j][0] - center[k][0], center[j][1] - center[k][1]
        ln = math.hypot(dx, dy) or 1
        nx, ny = -dy / ln, dx / ln
        w = width_of(t) / 2
        a, b = (tuft(t) if tuft else (1, 1))
        left.append((x + nx * w * a, y + ny * w * a))
        right.append((x - nx * w * b, y - ny * w * b))
    return left, right


def finish(img, name, anchor):
    w, h = img.width // SS, img.height // SS
    small = img.resize((w, h), Image.LANCZOS)
    bb = small.getbbox()
    small = small.crop(bb)
    small.save(f'{OUT}/{name}.png')
    return {'x': bb[0] - anchor[0], 'y': bb[1] - anchor[1], 'w': small.width, 'h': small.height}


# ---------------------------------------------------------------- хвост

def tail(L, W, glow):
    style = C['tail']
    cw, ch = W * (3.2 if C['tail'] != 'flame' else 4.4), L + 60
    px, py = cw / 2, ch - 14
    img = new(cw, ch)
    if style == 'tentacle':
        c = bez((px, py), (px - W * 0.5, py - L * 0.38), (px + W * 1.1, py - L * 0.72), (px + W * 0.15, py - L), n=90)
        width = lambda t: W * (1.0 - 0.9 * t ** 0.9)
        tuft = None
    elif style == 'tanuki':
        c = bez((px, py), (px - W * 0.12, py - L * 0.35), (px + W * 0.25, py - L * 0.7), (px + W * 0.18, py - L), n=80)
        width = lambda t: W * (0.5 + 0.5 * math.sin(math.pi * min(1, t ** 0.8) * 0.82)) * max(0.0, 1 - t ** 4) ** 0.5
        tuft = lambda t: (1 + 0.05 * max(0, math.sin(t * 6 * math.pi)) ** 3 * (t > 0.15),
                          1 + 0.05 * max(0, math.sin(t * 6 * math.pi + 1.6)) ** 3 * (t > 0.15))
    else:  # flame
        c = bez((px, py), (px - W * 0.6, py - L * 0.3), (px + W * 1.1, py - L * 0.62), (px + W * 0.35, py - L), n=110)
        width = lambda t: W * (0.34 + 0.66 * math.sin(math.pi * min(1, t ** 0.75) * 0.82)) * max(0.0, 1 - t ** 2.5) ** 0.6
        tuft = lambda t: (1 + 0.55 * t ** 1.5 * max(0, math.sin(t * 6 * math.pi)) ** 3 * (t > 0.35),
                          1 + 0.55 * t ** 1.5 * max(0, math.sin(t * 6 * math.pi + 2.4)) ** 3 * (t > 0.35))
    left, right = ribbon(c, width, tuft)
    m = mask_of(cw, ch, polys=[left + right[::-1]])
    paint(img, m, vgrad(img.size, py, py - L, FUR_DEEP, FUR))
    shade(img, m, -W * 0.2, 0, W * 0.14, strength=1.2)
    tipm = None
    if style == 'flame':
        ramp = np.clip((py - L * 0.42 - np.arange(img.height)[:, None] / SS) / (L * 0.34), 0, 1)
        ramp = (ramp * ramp * (3 - 2 * ramp) * 255).astype(np.uint8).repeat(img.width, 1)
        tipm = ImageChops.multiply(Image.fromarray(ramp, 'L'), m)
        paint(img, tipm, vgrad(img.size, py - L * 0.5, py - L * 1.0, GLOW_B, GLOW_A))
    if style == 'tanuki':
        # тёмные кольца и узор
        d = ImageDraw.Draw(img)
        n = len(c)
        rings = Image.new('L', img.size, 0); dr = ImageDraw.Draw(rings)
        for t0 in (0.42, 0.64, 0.84):
            a, b = int(n * (t0 - 0.045)), int(n * (t0 + 0.045))
            band = left[a:b] + right[a:b][::-1]
            dr.polygon([(x * SS, y * SS) for x, y in band], fill=255)
        rings = ImageChops.multiply(rings.filter(ImageFilter.GaussianBlur(1.2 * SS)), m)
        paint(img, rings, MARK)
        # кончик темнее
        ramp = np.clip((py - L * 0.9 - np.arange(img.height)[:, None] / SS) / (L * 0.08), 0, 1)
        tm = ImageChops.multiply(Image.fromarray((ramp * 255).astype(np.uint8).repeat(img.width, 1), 'L'), m)
        paint(img, tm, MARK)
    soft(img, px + W * 0.12, py - L * 0.5, W * 0.13, L * 0.22, tuple(min(255, v + 50) for v in FUR), 130, W * 0.09, clip=m)
    if style == 'tentacle':
        # присоски по внутренней стороне
        cups = new(cw, ch); dc = ImageDraw.Draw(cups)
        n = len(c)
        for i in range(6, n - 10, 9):
            t = i / (n - 1)
            x, y = right[i]
            cx_, cy_ = c[i]
            r = width(t) * 0.17
            ix, iy = x + (cx_ - x) * 0.32, y + (cy_ - y) * 0.32
            dc.ellipse(((ix - r) * SS, (iy - r) * SS, (ix + r) * SS, (iy + r) * SS), fill=CREAM + (255,), outline=FUR_SHADE + (255,), width=int(1.1 * SS))
            dc.ellipse(((ix - r * 0.4) * SS, (iy - r * 0.4) * SS, (ix + r * 0.4) * SS, (iy + r * 0.4) * SS), fill=FUR_SHADE + (200,))
        ca = ImageChops.multiply(cups.getchannel('A'), m); cups.putalpha(ca)
        img.alpha_composite(cups)
    img = outline(img, m, 2.0)
    if tipm is not None and glow > 0:
        halo = tipm.filter(ImageFilter.GaussianBlur(10 * SS)).point(lambda v: int(v * 0.7 * glow))
        hl = Image.new('RGBA', img.size, GLOW_B + (0,)); hl.putalpha(halo)
        out = Image.new('RGBA', img.size, (0, 0, 0, 0)); out.alpha_composite(hl); out.alpha_composite(img)
        img = out
    return img, (px, py)


# ---------------------------------------------------------------- тело

def body(k):
    s = [0.7, 0.85, 1.0][k]
    cx, gy = 200, 372
    img = new(400, 400)
    bw, bh = [50, 56, 60][k], [84, 108, 128][k]
    if CHAR == 'shukaku':
        bw *= 1.12; bh *= 0.96          # тануки покруглее
    if CHAR == 'gyuki':
        bw *= 1.1
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
    if C['cheeks']:
        ruff = []
        n = 9
        for i in range(n * 2 + 1):
            t = i / (n * 2)
            ang = math.pi * (0.06 + 0.88 * t)
            rr = bw * 0.78 * (1.0 if i % 2 == 0 else 1.16)
            ruff.append((cx - math.cos(ang) * rr * 1.05, top + bh * 0.08 + math.sin(ang) * rr * 0.62))
        ruff = [(cx - bw * 0.8, top - 6)] + ruff + [(cx + bw * 0.8, top - 6)]
        cm = ImageChops.multiply(mask_of(400, 400, polys=[ruff]), m)
    else:
        cm = ImageChops.multiply(mask_of(400, 400, ellipses=[(cx, top + bh * 0.5, bw * 0.55, bh * 0.42)]), m)
        cm = cm.filter(ImageFilter.GaussianBlur(2 * SS))
    paint(img, cm, CREAM)
    shade(img, cm, 0, -6, 4, strength=0.7)
    if CHAR == 'shukaku':
        # синие узоры-завитки по бокам
        d = ImageDraw.Draw(img)
        lay = new(400, 400); dl = ImageDraw.Draw(lay)
        for sx in (-1, 1):
            for j, f in enumerate((0.42, 0.66)):
                y = top + bh * f
                pts = bez((cx + sx * bw * 1.02, y - bh * 0.06), (cx + sx * bw * 0.72, y - bh * 0.02), (cx + sx * bw * 0.6, y + bh * 0.1), (cx + sx * bw * 0.74, y + bh * 0.16))
                stroke(dl, pts, MARK + (235,), 3.2 * s)
        la = ImageChops.multiply(lay.getchannel('A'), m); lay.putalpha(la)
        img.alpha_composite(lay)
    if CHAR == 'gyuki':
        # поперечные складки на груди, как у быка
        lay = new(400, 400); dl = ImageDraw.Draw(lay)
        for f in (0.35, 0.5, 0.65):
            y = top + bh * f
            stroke(dl, bez((cx - bw * 0.36, y), (cx - bw * 0.15, y + 5 * s), (cx + bw * 0.15, y + 5 * s), (cx + bw * 0.36, y)), FUR_SHADE + (120,), 1.6)
        la = ImageChops.multiply(lay.getchannel('A'), cm); lay.putalpha(la)
        img.alpha_composite(lay)
    pm = mask_of(400, 400, ellipses=[(cx - bw * 0.36, gy - 6 * s, 17 * s, 12 * s), (cx + bw * 0.36, gy - 6 * s, 17 * s, 12 * s)])
    paws = new(400, 400)
    paint(paws, pm, PAWS)
    shade(paws, pm, 0, -5 * s, 4 * s, color=tuple(v // 2 for v in PAWS), strength=0.8)
    d = ImageDraw.Draw(paws)
    for sx in (-1, 1):
        if CHAR == 'gyuki':   # копытца: одна щель посередине
            x = cx + sx * bw * 0.36
            d.line(((x * SS, (gy - 8 * s) * SS), (x * SS, (gy + 4 * s) * SS)), fill=tuple(v // 2 for v in PAWS) + (230,), width=int(2 * SS))
            continue
        for j in (-1, 0, 1):
            x = cx + sx * bw * 0.36 + j * 6 * s
            d.line(((x * SS, (gy - 2 * s) * SS), (x * SS, (gy + 2 * s) * SS)), fill=tuple(min(255, v + 70) for v in PAWS) + (200,), width=int(1.6 * SS))
    img = outline(img, m, 2.2)
    paws = outline(paws, pm, 2.0)
    img.alpha_composite(paws)
    return img, top


# ---------------------------------------------------------------- голова

def ears_layer(k, R, cx, cy):
    style = C['ears']
    layer = new(400, 400)
    if style == 'round':
        ell = [(cx + sx * R * 0.72, cy - R * 0.72, R * 0.34, R * 0.32) for sx in (-1, 1)]
        em = mask_of(400, 400, ellipses=ell)
        paint(layer, em, FUR)
        shade(layer, em, 3, 3, 4, strength=1.0)
        im = mask_of(400, 400, ellipses=[(x, y + R * 0.02, rx * 0.58, ry * 0.55) for x, y, rx, ry in ell])
        paint(layer, im, EAR_IN)
        return outline(layer, em, 2.2), em
    if style == 'horns':
        polys = []
        for sx in (-1, 1):
            hl = R * [0.6, 0.85, 1.05][k]
            base = (cx + sx * R * 0.58, cy - R * 0.62)
            c = bez(base, (base[0] + sx * hl * 0.9, base[1] - hl * 0.05), (base[0] + sx * hl * 1.25, base[1] - hl * 0.5), (base[0] + sx * hl * 1.0, base[1] - hl * 1.05), n=60)
            l, r = ribbon(c, lambda t: R * 0.34 * (1 - 0.88 * t ** 1.1))
            polys.append(l + r[::-1])
        em = mask_of(400, 400, polys=polys)
        paint(layer, em, vgrad(layer.size, cy - R * 1.8, cy - R * 0.5, (250, 240, 214), (206, 178, 130)))
        shade(layer, em, 3, 3, 3, color=(150, 120, 80), strength=1.0)
        # кольца на рогах
        d = ImageDraw.Draw(layer)
        for poly in polys:
            n = len(poly) // 2
            for t in (0.2, 0.35, 0.5):
                i = int(n * t)
                a, b = poly[i], poly[len(poly) - 1 - i]
                stroke(d, [a, b], (150, 120, 80, 150), 1.3)
        a = ImageChops.multiply(layer.getchannel('A'), em); layer.putalpha(a)
        # маленькие ушки под рогами
        ell = [(cx + sx * R * 0.98, cy - R * 0.3, R * 0.26, R * 0.13) for sx in (-1, 1)]
        um = mask_of(400, 400, ellipses=ell)
        ul = new(400, 400); paint(ul, um, FUR); shade(ul, um, 2, 2, 3, strength=1.0)
        paint(ul, mask_of(400, 400, ellipses=[(x, y, rx * 0.6, ry * 0.5) for x, y, rx, ry in ell]), EAR_IN)
        ul = outline(ul, um, 2.0)
        out = outline(layer, em, 2.0)
        ul.alpha_composite(out)
        return ul, ImageChops.lighter(em, um)
    # 'cat' — как у лисы, но ниже и шире
    eh = R * [0.72, 0.84, 0.95][k]
    ears = []
    for sx in (-1, 1):
        bx = cx + sx * R * 0.56
        tipx = cx + sx * R * 0.92
        ears.append(path([
            ((bx - sx * R * 0.46, cy - R * 0.55), (bx - sx * R * 0.34, cy - R * 0.9), (tipx - sx * R * 0.12, cy - R * 0.4 - eh), (tipx, cy - R * 0.35 - eh)),
            ((tipx, cy - R * 0.35 - eh), (tipx + sx * R * 0.1, cy - R * 0.3 - eh * 0.6), (bx + sx * R * 0.5, cy - R * 0.6), (bx + sx * R * 0.44, cy - R * 0.15)),
        ]))
    em = mask_of(400, 400, polys=ears)
    paint(layer, em, FUR)
    shade(layer, em, 4, 4, 5, strength=1.0)
    for sx in (-1, 1):
        bx = cx + sx * R * 0.56
        tipx = cx + sx * R * 0.92
        inner = path([
            ((bx - sx * R * 0.22, cy - R * 0.6), (bx - sx * R * 0.12, cy - R * 0.9), (tipx - sx * R * 0.1, cy - R * 0.45 - eh * 0.8), (tipx - sx * R * 0.02, cy - R * 0.4 - eh * 0.8)),
            ((tipx - sx * R * 0.02, cy - R * 0.4 - eh * 0.8), (tipx + sx * R * 0.04, cy - R * 0.3 - eh * 0.5), (bx + sx * R * 0.32, cy - R * 0.6), (bx + sx * R * 0.24, cy - R * 0.3)),
        ])
        paint(layer, mask_of(400, 400, polys=[inner]), vgrad(layer.size, cy - R * 0.4 - eh, cy - R * 0.4, GLOW_B, EAR_IN))
    return outline(layer, em, 2.2), em


def head(k):
    R = [64, 60, 56][k]
    if CHAR == 'gyuki':
        R = [62, 58, 54][k]
    cx, cy = 200, 200
    img = new(400, 400)
    el, _ = ears_layer(k, R, cx, cy)
    img.alpha_composite(el)
    face = []
    n = 60
    for i in range(n):
        a = 2 * math.pi * i / n
        r = R
        x = math.cos(a); y = math.sin(a)
        if C['cheeks'] and (0.15 < a < 1.3 or 1.84 < a < 2.99):
            spike = math.sin((a % 0.38) / 0.38 * math.pi)
            r = R * (1.0 + 0.16 * spike * (1 if y > 0 else 0))
        sx, sy = (1.08, 0.94) if CHAR != 'gyuki' else (1.12, 0.92)
        face.append((cx + x * r * sx, cy + y * r * sy))
    fm = mask_of(400, 400, polys=[face])
    hl = new(400, 400)
    paint(hl, fm, vgrad(hl.size, cy - R, cy + R, FUR, FUR_DEEP))
    shade(hl, fm, 0, -R * 0.2, R * 0.16, strength=0.9)
    shade(hl, fm, -R * 0.18, 0, R * 0.1, strength=0.5)
    if CHAR == 'gyuki':
        # широкая бычья морда
        soft(hl, cx, cy + R * 0.5, R * 0.62, R * 0.36, CREAM, 250, R * 0.05, clip=fm)
    else:
        soft(hl, cx, cy + R * 0.42, R * 0.6, R * 0.36, CREAM, 245, R * 0.08, clip=fm)
    er = R * [0.27, 0.25, 0.235][k]; ex = R * 0.42; ey = cy + R * 0.05
    if C['wings'] == 'fox':
        for sx in (-1, 1):
            x = cx + sx * ex
            wing = path([
                ((x - sx * er * 1.15, ey + er * 0.35), (x - sx * er * 0.2, ey + er * 1.9), (x + sx * er * 1.6, ey + er * 1.0), (x + sx * er * 2.25, ey - er * 1.25)),
                ((x + sx * er * 2.25, ey - er * 1.25), (x + sx * er * 1.15, ey - er * 0.2), (x + sx * er * 0.3, ey + er * 0.75), (x - sx * er * 1.15, ey + er * 0.35)),
            ])
            paint(hl, ImageChops.multiply(mask_of(400, 400, polys=[wing]), fm), MARK)
    elif C['wings'] == 'mask':
        # тёмная «маска» тануки вокруг глаз с синим узором
        for sx in (-1, 1):
            x = cx + sx * ex
            mk = mask_of(400, 400, ellipses=[(x + sx * er * 0.25, ey + er * 0.25, er * 1.75, er * 1.5)])
            mk = ImageChops.multiply(mk.filter(ImageFilter.GaussianBlur(1.5 * SS)), fm)
            paint(hl, mk, MARK)
    hl2 = outline(hl, fm, 2.2)
    img.alpha_composite(hl2)
    st = C['forehead']
    if st == 'flame':
        mark = mask_of(400, 400, polys=[path([
            ((cx, cy - R * 0.72), (cx + R * 0.12, cy - R * 0.55), (cx + R * 0.08, cy - R * 0.42), (cx, cy - R * 0.36)),
            ((cx, cy - R * 0.36), (cx - R * 0.08, cy - R * 0.42), (cx - R * 0.12, cy - R * 0.55), (cx, cy - R * 0.72)),
        ])])
        a = [110, 190, 255][k]
        mk = new(400, 400)
        paint(mk, mark.point(lambda v: v * a // 255), vgrad(mk.size, cy - R * 0.72, cy - R * 0.36, GLOW_A, GLOW_B))
        glow = mark.filter(ImageFilter.GaussianBlur(5 * SS)).point(lambda v: v * a // 400)
        gl = Image.new('RGBA', mk.size, GLOW_B + (0,)); gl.putalpha(glow)
        img.alpha_composite(gl); img.alpha_composite(mk)
    elif st == 'swirl':
        lay = new(400, 400); d = ImageDraw.Draw(lay)
        a = [150, 210, 255][k]
        # завиток на лбу и две дуги по бокам
        pts = []
        for i in range(40):
            t = i / 39
            ang = -math.pi / 2 + t * math.pi * 1.6
            rr = R * 0.16 * (1 - 0.7 * t)
            pts.append((cx + math.cos(ang) * rr, cy - R * 0.52 + math.sin(ang) * rr))
        stroke(d, pts, MARK + (a,), 2.6)
        for sx in (-1, 1):
            stroke(d, bez((cx + sx * R * 0.3, cy - R * 0.78), (cx + sx * R * 0.55, cy - R * 0.72), (cx + sx * R * 0.7, cy - R * 0.55), (cx + sx * R * 0.72, cy - R * 0.38)), MARK + (a,), 2.4)
        la = ImageChops.multiply(lay.getchannel('A'), fm); lay.putalpha(la)
        img.alpha_composite(lay)
    if CHAR == 'gyuki':
        # ноздри на морде
        d = ImageDraw.Draw(img)
        for sx in (-1, 1):
            x = cx + sx * R * 0.2; y = cy + R * 0.62
            d.ellipse(((x - R * 0.06) * SS, (y - R * 0.035) * SS, (x + R * 0.06) * SS, (y + R * 0.035) * SS), fill=NOSE + (200,))
    return img, R


# ---------------------------------------------------------------- лица

def faces(k, R):
    cx, cy = 200, 200
    er = R * [0.27, 0.25, 0.235][k]
    ex = R * 0.42
    ey = cy + R * 0.05
    res = {}
    gy = CHAR == 'gyuki'

    def base():
        return new(400, 400)

    def nose_mouth(img, mouth='w'):
        d = ImageDraw.Draw(img)
        ny = cy + R * (0.32 if not gy else 0.42)
        if not gy:
            d.polygon([((cx - R * 0.07) * SS, (ny - R * 0.03) * SS), ((cx + R * 0.07) * SS, (ny - R * 0.03) * SS), (cx * SS, (ny + R * 0.05) * SS)], fill=NOSE + (255,))
        lw = int(1.8 * SS)
        my = ny + R * (0.06 if not gy else 0.3)
        if mouth == 'w':
            if gy:
                pts = bez((cx - R * 0.18, my), (cx - R * 0.06, my + R * 0.07), (cx + R * 0.06, my + R * 0.07), (cx + R * 0.18, my))
                d.line([(x * SS, y * SS) for x, y in pts], fill=LINE + (230,), width=lw, joint='curve')
            else:
                pts = bez((cx - R * 0.16, my + R * 0.02), (cx - R * 0.1, my + R * 0.1), (cx - R * 0.02, my + R * 0.08), (cx, my))
                pts2 = bez((cx, my), (cx + R * 0.02, my + R * 0.08), (cx + R * 0.1, my + R * 0.1), (cx + R * 0.16, my + R * 0.02))
                d.line([(x * SS, y * SS) for x, y in pts + pts2], fill=LINE + (230,), width=lw, joint='curve')
        elif mouth == 'open':
            d.ellipse(((cx - R * 0.1) * SS, (my) * SS, (cx + R * 0.1) * SS, (my + R * 0.16) * SS), fill=(140, 70, 110, 255), outline=LINE + (230,), width=lw)
        if mouth in ('open', 'smile') and not gy:
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

    def pupil(d, x, look):
        p = C['PUPIL']
        if p == 'slit':
            d.ellipse(((x + look - er * 0.16) * SS, (ey - er * 0.78) * SS, (x + look + er * 0.16) * SS, (ey + er * 0.92) * SS), fill=EYE_DARK + (255,))
        elif p == 'round':
            d.ellipse(((x + look - er * 0.4) * SS, (ey - er * 0.32) * SS, (x + look + er * 0.4) * SS, (ey + er * 0.52) * SS), fill=EYE_DARK + (255,))
        else:   # star: четырёхлучевой зрачок
            pts = []
            for i in range(8):
                a = math.pi / 4 * i - math.pi / 2
                r = er * (0.62 if i % 2 == 0 else 0.2)
                pts.append(((x + look + math.cos(a) * r) * SS, (ey + er * 0.1 + math.sin(a) * r) * SS))
            d.polygon(pts, fill=EYE_DARK + (255,))

    def open_eyes(img, look=0.0):
        d = ImageDraw.Draw(img)
        for i, sx in enumerate((-1, 1)):
            x = cx + sx * ex
            d.ellipse(((x - er) * SS, (ey - er * 1.12) * SS, (x + er) * SS, (ey + er * 1.12) * SS), fill=EYE_DARK + (255,))
            iris = new(400, 400)
            im = mask_of(400, 400, ellipses=[(x + look, ey + er * 0.1, er * 0.82, er * 0.95)])
            c0, c1 = IRIS[i]
            paint(iris, im, vgrad(iris.size, ey - er, ey + er, c0, c1))
            img.alpha_composite(iris)
            d = ImageDraw.Draw(img)
            pupil(d, x, look)
            d.ellipse(((x - er * 0.55) * SS, (ey - er * 0.8) * SS, (x - er * 0.05) * SS, (ey - er * 0.3) * SS), fill=(255, 255, 255, 255))
            d.ellipse(((x + er * 0.25) * SS, (ey + er * 0.25) * SS, (x + er * 0.5) * SS, (ey + er * 0.5) * SS), fill=(255, 255, 255, 220))
            d.ellipse(((x - er) * SS, (ey - er * 1.12) * SS, (x + er) * SS, (ey + er * 1.12) * SS), outline=LINE + (255,), width=int(1.8 * SS))

    def arc_eyes(img, up=True):
        d = ImageDraw.Draw(img)
        col = EYE_DARK if C['wings'] != 'mask' else CREAM
        for sx in (-1, 1):
            x = cx + sx * ex
            if up:
                pts = bez((x - er, ey + er * 0.2), (x - er * 0.5, ey - er * 0.8), (x + er * 0.5, ey - er * 0.8), (x + er, ey + er * 0.2))
            else:
                pts = bez((x - er, ey), (x - er * 0.5, ey + er * 0.6), (x + er * 0.5, ey + er * 0.6), (x + er, ey))
            d.line([(px * SS, py * SS) for px, py in pts], fill=col + (255,), width=int(2.6 * SS), joint='curve')

    def sad_eyes(img):
        open_eyes(img)
        d = ImageDraw.Draw(img)
        col = LINE if C['wings'] != 'mask' else CREAM
        for sx in (-1, 1):
            x = cx + sx * ex
            d.line((((x - sx * er * 0.9) * SS, (ey - er * 1.65) * SS), ((x + sx * er * 0.7) * SS, (ey - er * 1.3) * SS)), fill=col + (220,), width=int(2 * SS))
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
    L, W = C['tailLW'][name]
    t, piv = tail(L, W, [0.5, 0.8, 1.0][k] * C['glow'])
    st['tail'] = finish(t, f'{name}_tail', piv)
    b, top = body(k)
    st['body'] = finish(b, f'{name}_body', (200, 372))
    h, R = head(k)
    st['head'] = finish(h, f'{name}_head', (200, 200))
    st['faces'] = {}
    for fn, img in faces(k, R).items():
        st['faces'][fn] = finish(img, f'{name}_face_{fn}', (200, 200))
    st['neckY'] = round(top - 372 - R * 0.62, 1)
    st['tailBase'] = [0, -[26, 32, 38][k]]
    st['R'] = R
    st['tailAngles'] = C['tails'][name]
    manifest[name] = st
json.dump(manifest, open(f'{OUT}/manifest.json', 'w'), indent=1)
print('ok', CHAR)
