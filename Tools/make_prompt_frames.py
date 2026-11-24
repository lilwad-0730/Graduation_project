# -*- coding: utf-8 -*-
"""
產生操作提示的「動畫逐格圖」（Assets/Resources/Prompts/）。

 ・stick_left / stick_right / stick_up / stick_down
     3D 搖桿（從斜上方看：橢圓底座、桿身、蕈菇狀搖桿帽）。每一格都是用真正的 3D 幾何算出來再投影，
     所以往任何方向傾斜都自然（往左右＝帽子橫移，往上＝遠離畫面、往下＝靠近畫面）。
     動畫：中立 → 推到底 → 停一下 → 回中立。
 ・key_<名稱>   鍵盤按鍵帽（會被按下去）＋上面三個往下的小箭頭
 ・btn_A / btn_X / btn_Y / btn_B   手把按鈕（會被按下去）

 每張是橫向的逐格圖（sprite sheet），每格大小相同。索引寫在 index.json（格數、格寬高、每秒幾格）。
 兩種畫法：
   sticker（貼紙）：有米白底色＋深色外框，浮在任何背景上都看得清楚（給「浮在物件上方」的提示用）
   line（線稿）：透明底，只有線（給背景線條畫的搖桿用，可指定線色，深色／淺色背景各一份）

用法：python Tools/make_prompt_frames.py
需要 Pillow。
"""
import os, math, json
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'Resources', 'Prompts')
SS = 4
FR = 192                       # 每格 192x192
INK = (40, 34, 50)
INK_LIGHT = (232, 224, 210)    # 淺色線（深色背景用）
PAPER = (250, 246, 236)
PAPER_SIDE = (222, 214, 198)
CLEAR = (0, 0, 0, 0)

def font(sz):
    for f in ('arialbd.ttf', 'segoeuib.ttf'):
        p = os.path.join(os.environ.get('WINDIR', 'C:\\Windows'), 'Fonts', f)
        if os.path.exists(p):
            return ImageFont.truetype(p, sz)
    return ImageFont.load_default()

def ease(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)

# ─────────────────────────── 3D 小工具 ───────────────────────────
ALPHA = math.radians(36)   # 鏡頭俯角

def proj(p, cx, cy, sc):
    x, y, z = p
    return (cx + x * sc, cy - (y * math.cos(ALPHA) - z * math.sin(ALPHA)) * sc)

def norm(v):
    l = math.sqrt(sum(c * c for c in v)) or 1.0
    return tuple(c / l for c in v)

def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])

def circle3d(center, axis, r, n=72):
    """在「過 center、垂直於 axis」的平面上的圓，回傳 3D 點列。"""
    a = norm(axis)
    helper = (0, 0, 1) if abs(a[2]) < 0.9 else (1, 0, 0)
    u = norm(cross(a, helper))
    v = cross(a, u)
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        pts.append((center[0] + r * (math.cos(t) * u[0] + math.sin(t) * v[0]),
                    center[1] + r * (math.cos(t) * u[1] + math.sin(t) * v[1]),
                    center[2] + r * (math.cos(t) * u[2] + math.sin(t) * v[2])))
    return pts

def hull(points):
    pts = sorted(set((round(x, 3), round(y, 3)) for x, y in points))
    if len(pts) <= 2:
        return pts
    def cr(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo = []
    for p in pts:
        while len(lo) >= 2 and cr(lo[-2], lo[-1], p) <= 0: lo.pop()
        lo.append(p)
    up = []
    for p in reversed(pts):
        while len(up) >= 2 and cr(up[-2], up[-1], p) <= 0: up.pop()
        up.append(p)
    return lo[:-1] + up[:-1]

class Painter:
    """畫在超取樣的畫布上。fill=None＝透明（線稿：用透明蓋掉後面的線）。"""
    def __init__(self, size, ink, fill, side_fill):
        self.im = Image.new('RGBA', (size * SS, size * SS), CLEAR)
        self.d = ImageDraw.Draw(self.im)
        self.ink = ink + (255,)
        self.soft = ink + (140,)
        self.fill = (fill + (255,)) if fill else CLEAR
        self.side = (side_fill + (255,)) if side_fill else CLEAR
        self.w = max(2, int(3.4 * SS))      # 外框線寬
        self.size = size

    def pts(self, pl):
        return [(x * SS, y * SS) for x, y in pl]

    def poly(self, pl, fill, outline=True, width=None):
        p = self.pts(pl)
        self.d.polygon(p, fill=fill)
        if outline:
            self.d.line(p + [p[0]], fill=self.ink, width=width or self.w, joint='curve')

    def line(self, pl, color=None, width=None, closed=False, halo=False):
        p = self.pts(pl)
        if closed: p = p + [p[0]]
        if halo and self.fill != CLEAR:      # 貼紙版：先畫一圈淺色粗線當描邊
            a = (color or self.ink)[3] if len(color or self.ink) > 3 else 255
            self.d.line(p, fill=self.fill[:3] + (a,), width=(width or self.w) + int(3.2 * SS), joint='curve')
        self.d.line(p, fill=color or self.ink, width=width or self.w, joint='curve')

    def dot(self, x, y, r, color=None):
        x, y, r = x * SS, y * SS, r * SS
        self.d.ellipse([x - r, y - r, x + r, y + r], fill=color or self.ink)

    def done(self):
        return self.im.resize((self.size, self.size), Image.LANCZOS)

# ─────────────────────────── 3D 搖桿 ───────────────────────────
DIRS = {'left': (-1, 0), 'right': (1, 0), 'up': (0, -1), 'down': (0, 1)}   # (x, z)：z 負＝遠離畫面（往上推）

def stick_frame(dirn, tilt, ink, fill, side_fill, arrow=True):
    """tilt：0（中立）～1（推到底）。"""
    P = Painter(FR, ink, fill, side_fill)
    dx, dz = DIRS[dirn]
    sc = 52.0
    cx, cy = FR / 2, FR * 0.60
    phi = math.radians(34) * tilt
    axis = (math.sin(phi) * dx, math.cos(phi), math.sin(phi) * dz)

    # 底座：圓柱（上圓＋下圓的外輪廓）＋上面一圈凹槽
    rb, hb = 1.0, 0.20
    top = circle3d((0, 0, 0), (0, 1, 0), rb)
    bot = circle3d((0, -hb, 0), (0, 1, 0), rb)
    top2 = [proj(p, cx, cy, sc) for p in top]
    bot2 = [proj(p, cx, cy, sc) for p in bot]
    P.poly(hull(top2 + bot2), P.side)
    P.poly(top2, P.fill)
    ring = [proj(p, cx, cy, sc) for p in circle3d((0, 0, 0), (0, 1, 0), 0.62)]
    P.line(ring, P.soft, max(2, P.w // 2), closed=True)

    # 桿身
    L = 0.62
    r_s = 0.15
    base_c = (0, 0, 0)
    cap_c = tuple(axis[i] * L for i in range(3))
    s0 = [proj(p, cx, cy, sc) for p in circle3d(base_c, axis, r_s)]
    s1 = [proj(p, cx, cy, sc) for p in circle3d(cap_c, axis, r_s)]
    P.poly(hull(s0 + s1), P.side)

    # 搖桿帽（圓台：下圓 0.36、上圓 0.52，厚 0.2）
    hc = 0.24
    c_bot = tuple(axis[i] * (L - 0.02) for i in range(3))
    c_top = tuple(axis[i] * (L - 0.02 + hc) for i in range(3))
    b2 = [proj(p, cx, cy, sc) for p in circle3d(c_bot, axis, 0.50)]
    t2 = [proj(p, cx, cy, sc) for p in circle3d(c_top, axis, 0.74)]
    P.poly(hull(b2 + t2), P.side)
    P.poly(t2, P.fill)
    dish = [proj(p, cx, cy, sc) for p in circle3d(tuple(axis[i] * (L - 0.02 + hc + 0.01) for i in range(3)), axis, 0.50)]
    P.line(dish, P.soft, max(2, P.w // 2), closed=True)

    # 方向箭頭（推到一半以上才出現）：左右＝底座前面橫向；上下＝右側縱向
    if arrow:
        a = ease((tilt - 0.35) / 0.4)
        if a > 0.02:
            col = ink + (int(255 * a),)
            if dirn in ('left', 'right'):
                ay = FR * 0.93
                x0, x1 = cx - dx * 18, cx + dx * 46
                head = 11
                P.line([(x0, ay), (x1, ay)], col, P.w, halo=True)
                for s2 in (-1, 1):
                    ang = math.atan2(0, x1 - x0) + math.pi + s2 * math.radians(40)
                    P.line([(x1, ay), (x1 + head * math.cos(ang), ay + head * math.sin(ang))], col, P.w, halo=True)
            else:
                ax = cx + 70
                y0, y1 = (cy + 4, cy - 52) if dirn == 'up' else (cy - 52, cy + 4)
                head = 11
                P.line([(ax, y0), (ax, y1)], col, P.w, halo=True)
                for s2 in (-1, 1):
                    ang = math.atan2(y1 - y0, 0) + math.pi + s2 * math.radians(40)
                    P.line([(ax, y1), (ax + head * math.cos(ang), y1 + head * math.sin(ang))], col, P.w, halo=True)
    return P.done()

def stick_sheet(dirn, ink, fill, side_fill, n=16):
    """中立停 3 格 → 推出去 5 格 → 推到底停 4 格 → 回中立 4 格。"""
    tilts = [0] * 3 + [ease((i + 1) / 5) for i in range(5)] + [1] * 4 + [1 - ease((i + 1) / 4) for i in range(4)]
    assert len(tilts) == n
    return [stick_frame(dirn, t, ink, fill, side_fill) for t in tilts]

# ─────────────────────────── 鍵帽／按鈕 ───────────────────────────
def chevrons(P, cx, y_top, phase):
    """三個往下的小箭頭，依序亮起（跟背景線條畫的「按下去」箭頭同一個意思）。"""
    for i in range(3):
        a = 0.25 + 0.75 * max(0.0, math.cos((phase - i * 0.16) * 2 * math.pi) * 0.5 + 0.5)
        col = INK + (int(255 * a),) if P.ink[:3] == INK else P.ink[:3] + (int(255 * a),)
        y = y_top + i * 9
        P.line([(cx - 9, y), (cx, y + 8), (cx + 9, y)], col, max(2, int(P.w * 0.8)), halo=True)

def keycap_frame(label, press, phase, ink, fill, side_fill, wide=False):
    P = Painter(FR, ink, fill, side_fill)
    w = 120 if wide else 78
    h = 78
    cx = FR / 2
    depth = 14 - 9 * press                   # 按下去：厚度變小、整體下沉
    x0, x1 = cx - w / 2, cx + w / 2
    y1 = FR * 0.80
    ytop_face = y1 - h - depth
    r = 14
    def rr(a, b, c, d_, rad):
        pts = []
        for (ccx, ccy, a0) in ((c - rad, b + rad, -90), (c - rad, d_ - rad, 0), (a + rad, d_ - rad, 90), (a + rad, b + rad, 180)):
            for k in range(10):
                t = math.radians(a0 + 90 * k / 9)
                pts.append((ccx + rad * math.cos(t), ccy + rad * math.sin(t)))
        return pts
    # 側面（厚度）→ 頂面
    P.poly(rr(x0, ytop_face + depth, x1, y1, r), P.side)
    P.poly(rr(x0, ytop_face, x1, y1 - depth, r), P.fill)
    inset = 8
    P.line(rr(x0 + inset, ytop_face + inset, x1 - inset, y1 - depth - inset, r - 6), P.soft, max(2, P.w // 2), closed=True)
    # 字
    f = font(40 if not wide else 34)
    # 用 ImageDraw 在超取樣畫布上寫字
    tx, ty = cx * SS, ((ytop_face + y1 - depth) / 2) * SS
    bb = P.d.textbbox((0, 0), label, font=ImageFont.truetype(f.path, int((40 if not wide else 34) * SS)) if hasattr(f, 'path') else f)
    ff = ImageFont.truetype(f.path, int((40 if not wide else 34) * SS)) if hasattr(f, 'path') else f
    bb = P.d.textbbox((0, 0), label, font=ff)
    P.d.text((tx - (bb[0] + bb[2]) / 2, ty - (bb[1] + bb[3]) / 2), label, font=ff, fill=P.ink)
    chevrons(P, cx, 14, phase)
    return P.done()

def button_frame(label, press, phase, ink, fill, side_fill):
    P = Painter(FR, ink, fill, side_fill)
    cx = FR / 2
    R = 40
    depth = 10 - 7 * press
    cy = FR * 0.60 + (10 - depth)
    def circ(ccx, ccy, r, n=64):
        return [(ccx + r * math.cos(2 * math.pi * i / n), ccy + r * math.sin(2 * math.pi * i / n)) for i in range(n)]
    P.poly(hull(circ(cx, cy + depth, R) + circ(cx, cy, R)), P.side)
    P.poly(circ(cx, cy, R), P.fill)
    P.line(circ(cx, cy, R - 8), P.soft, max(2, P.w // 2), closed=True)
    ff = font(int(44 * SS))
    bb = P.d.textbbox((0, 0), label, font=ff)
    P.d.text((cx * SS - (bb[0] + bb[2]) / 2, cy * SS - (bb[1] + bb[3]) / 2), label, font=ff, fill=P.ink)
    chevrons(P, cx, 6, phase)
    return P.done()

def press_sheet(maker, n=12):
    """12 格：放著 → 按下去 → 按住 → 放開；小箭頭一路往下流動。"""
    presses = [0, 0, ease(1 / 3), ease(2 / 3), 1, 1, 1, 1, 1 - ease(1 / 2), 0, 0, 0]
    frames = []
    for i in range(n):
        frames.append(maker(presses[i], i / n))
    return frames

# ─────────────────────────── 輸出 ───────────────────────────
def save_sheet(name, frames, fps, index):
    sheet = Image.new('RGBA', (FR * len(frames), FR), CLEAR)
    for i, f in enumerate(frames):
        sheet.paste(f, (i * FR, 0))
    sheet.save(os.path.join(ROOT, name + '.png'))
    index.append({'name': name, 'frames': len(frames), 'w': FR, 'h': FR, 'fps': fps})
    print('  wrote', name, len(frames), 'frames')

def plus_frame(ink, fill):
    P = Painter(FR, ink, fill, fill)
    c = FR / 2
    P.line([(c - 22, c), (c + 22, c)], None, int(P.w * 1.6), halo=True)
    P.line([(c, c - 22), (c, c + 22)], None, int(P.w * 1.6), halo=True)
    return P.done()

def main():
    os.makedirs(ROOT, exist_ok=True)
    index = []
    # 貼紙版（浮在物件上方用）
    for dname in DIRS:
        save_sheet('stick_' + dname, stick_sheet(dname, INK, PAPER, PAPER_SIDE), 10, index)
    for label, nm, wide in (('Shift', 'Shift', True), ('S', 'S', False), ('D', 'D', False), ('A', 'A', False),
                            ('W', 'W', False), ('Space', 'Space', True), ('E', 'E', False)):
        save_sheet('key_' + nm, press_sheet(lambda p, ph, L=label, w=wide: keycap_frame(L, p, ph, INK, PAPER, PAPER_SIDE, w)), 9, index)
    for letter in ('A', 'X', 'Y', 'B'):
        save_sheet('btn_' + letter, press_sheet(lambda p, ph, L=letter: button_frame(L, p, ph, INK, PAPER, PAPER_SIDE)), 9, index)
    save_sheet('plus', [plus_frame(INK, PAPER)], 1, index)
    # 線稿版（背景線條畫的搖桿用：透明底）
    for dname in DIRS:
        save_sheet('line_stick_' + dname, stick_sheet(dname, INK, None, None), 10, index)
        save_sheet('line_light_stick_' + dname, stick_sheet(dname, INK_LIGHT, None, None), 10, index)
    with open(os.path.join(ROOT, 'index.json'), 'w', encoding='utf-8') as fp:
        json.dump({'sheets': index}, fp, ensure_ascii=False, indent=1)
    print('index.json', len(index), 'sheets')

if __name__ == '__main__':
    main()
