# Превью всех персонажей: python3 preview2.py out.png dir1 dir2 ...
import json, math, sys
from PIL import Image


def compose(D, M, name, face='normal'):
    st = M[name]
    W, H = 520, 440; gx, gy = 260, 420
    can = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    part = lambda key: Image.open(f'{D}/{name}_{key}.png')
    t = part('tail'); ti = st['tail']
    bx, by = gx + st['tailBase'][0], gy + st['tailBase'][1]
    for a in st['tailAngles']:
        pad = Image.new('RGBA', (t.width * 5, t.height * 5)); px, py = -ti['x'] + 2 * t.width, -ti['y'] + 2 * t.height
        pad.alpha_composite(t, (2 * t.width, 2 * t.height))
        r = pad.rotate(-a, resample=Image.BICUBIC, center=(px, py))
        can.alpha_composite(r, (int(bx - px), int(by - py)))
    b = part('body'); bi = st['body']
    can.alpha_composite(b, (gx + bi['x'], gy + bi['y']))
    hx, hy = gx, gy + st['neckY']
    h = part('head'); hi = st['head']
    can.alpha_composite(h, (int(hx + hi['x']), int(hy + hi['y'])))
    f = part('face_' + face); fi = st['faces'][face]
    can.alpha_composite(f, (int(hx + fi['x']), int(hy + fi['y'])))
    return can


dirs = sys.argv[2:]
row = Image.new('RGBA', (520 * 3, 440 * len(dirs)), (60, 70, 80, 255))
for j, D in enumerate(dirs):
    M = json.load(open(f'{D}/manifest.json'))
    for i, n in enumerate(['baby', 'teen', 'adult']):
        row.alpha_composite(compose(D, M, n), (520 * i, 440 * j))
row.convert('RGB').resize((row.width // 2, row.height // 2), Image.LANCZOS).save(sys.argv[1])
