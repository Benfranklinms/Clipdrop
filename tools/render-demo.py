import math, os, random
from PIL import Image, ImageDraw, ImageFont, ImageFilter

W, H, FPS = 1280, 720, 30
OUT = "/workspace/work/demo/frames"
os.makedirs(OUT, exist_ok=True)
F = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FB = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FM = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"
def font(p, s): return ImageFont.truetype(p, s)

# ---------- desktop ----------
def make_desktop():
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    for y in range(H):
        t = y / H
        d.line([(0, y), (W, y)], fill=(int(40 + 60 * t), int(70 + 40 * t), int(140 - 30 * t)))
    # soft blobs
    blob = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    bd = ImageDraw.Draw(blob)
    bd.ellipse([700, 300, 1300, 800], fill=(255, 140, 90, 70))
    bd.ellipse([-200, 380, 500, 900], fill=(120, 200, 255, 60))
    img = Image.alpha_composite(img.convert("RGBA"), blob.filter(ImageFilter.GaussianBlur(80))).convert("RGB")
    d = ImageDraw.Draw(img)
    # taskbar
    d.rectangle([0, H - 44, W, H], fill=(32, 32, 36))
    for i, c in enumerate([(0, 120, 215), (240, 180, 40), (60, 180, 90), (220, 70, 70), (150, 100, 220)]):
        x = W // 2 - 110 + i * 46
        d.rounded_rectangle([x, H - 36, x + 30, H - 8], 6, fill=c)
    d.text((W - 90, H - 32), "16:24", font=font(F, 14), fill=(230, 230, 230))
    # window 1: a weather/news style card
    d.rounded_rectangle([90, 120, 640, 560], 10, fill=(250, 250, 252))
    d.rounded_rectangle([90, 120, 640, 156], 10, fill=(232, 234, 240))
    d.rectangle([90, 140, 640, 156], fill=(232, 234, 240))
    for i, c in enumerate([(255, 95, 86), (255, 189, 46), (39, 201, 63)]):
        d.ellipse([604 - i * 22, 131, 616 - i * 22, 143], fill=c)
    d.text((110, 129), "Trip notes - Munnar", font=font(F, 14), fill=(60, 60, 70))
    d.text((120, 180), "Munnar weekend", font=font(FB, 30), fill=(30, 40, 60))
    d.text((120, 222), "Sat 10 Oct  -  2 nights  -  hill station", font=font(F, 15), fill=(110, 110, 120))
    d.rounded_rectangle([120, 260, 610, 420], 12, fill=(70, 150, 110))
    scene = Image.new("RGB", (490, 160), (70, 150, 110))
    sd = ImageDraw.Draw(scene)
    sd.ellipse([400, 20, 450, 70], fill=(255, 220, 120))
    for k in range(7):
        x0 = k * 70 - 20
        sd.polygon([(x0, 160), (x0 + 60, 40 + (k % 3) * 25), (x0 + 120, 160)], fill=(40 + k * 8, 110 + k * 6, 80))
    sm = Image.new("L", scene.size, 0)
    ImageDraw.Draw(sm).rounded_rectangle([0, 0, 489, 159], 12, fill=255)
    img.paste(scene, (120, 260), sm)
    d = ImageDraw.Draw(img)
    rows = ["Bus 06:30 from Vyttila hub", "Tea museum opens 09:00", "Booking ref  MNR-48213"]
    for i, r in enumerate(rows):
        d.text((124, 440 + i * 34), r, font=font(F, 17), fill=(50, 55, 65))
    # window 2: a chart
    d.rounded_rectangle([700, 170, 1190, 520], 10, fill=(28, 30, 38))
    d.text((724, 188), "Placement prep - hours / week", font=font(F, 15), fill=(200, 205, 215))
    vals = [6, 9, 7, 12, 10, 14, 16]
    for i, v in enumerate(vals):
        x = 740 + i * 62
        d.rounded_rectangle([x, 480 - v * 17, x + 38, 480], 6, fill=(90 + i * 18, 160, 255 - i * 15))
        d.text((x + 8, 488), "W%d" % (i + 1), font=font(F, 12), fill=(150, 155, 165))
    return img

DESK = make_desktop()

# ---------- card sprites ----------
CARD_W = 150
def card_sprite(photo, chrome=1.0):
    pw = CARD_W - 14
    s = min(pw / photo.width, 104 / photo.height)
    ph = photo.resize((max(1, int(photo.width * s)), max(1, int(photo.height * s))), Image.LANCZOS)
    cw, ch = ph.width + 10, ph.height + 10
    pad = 30
    spr = Image.new("RGBA", (cw + pad * 2, ch + pad * 2 + 20), (0, 0, 0, 0))
    sh = Image.new("RGBA", spr.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle([pad, pad + 20 + 5, pad + cw, pad + 20 + ch + 5], 16, fill=(0, 0, 0, 70))
    spr = Image.alpha_composite(spr, sh.filter(ImageFilter.GaussianBlur(8)))
    d = ImageDraw.Draw(spr)
    d.rounded_rectangle([pad, pad + 20, pad + cw, pad + 20 + ch], 16, fill=(244, 244, 247, 235), outline=(255, 255, 255, 220))
    m = Image.new("L", ph.size, 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, ph.width - 1, ph.height - 1], 12, fill=255)
    spr.paste(ph, (pad + 5, pad + 25), m)
    # clip
    cx = pad + cw // 2
    d.rounded_rectangle([cx - 5, pad + 20 - 13, cx + 5, pad + 20 + 14], 4, fill=(205, 205, 210), outline=(150, 150, 155))
    d.line([(cx - 2, pad + 20 - 10), (cx - 2, pad + 20 + 10)], fill=(240, 240, 242))
    return spr, (cx, pad + 20 - 13)  # pivot = top of clip

def paste_rot(base, spr, pivot, at, angle, alpha=1.0):
    # rotate sprite about pivot and place pivot at 'at'
    px, py = pivot
    big = max(spr.width, spr.height) * 2
    canvas = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    ox, oy = int(round(big // 2 - px)), int(round(big // 2 - py))
    canvas.paste(spr, (ox, oy))
    r = canvas.rotate(-angle, resample=Image.BICUBIC, center=(big // 2, big // 2))
    if alpha < 1:
        a = r.split()[3].point(lambda v: int(v * alpha))
        r.putalpha(a)
    base.alpha_composite(r, (int(at[0] - big // 2), int(at[1] - big // 2)))

# ---------- line geometry ----------
ROPE_TOP = 10
SAG = min(30, W * 0.018)
def rope_y(x): f = x / W; return ROPE_TOP + 4 * SAG * f * (1 - f)
def slot_x(i, n): return W / 2 - (n - 1) * 174 / 2 + i * 174

def draw_rope(base, slide):
    lay = Image.new("RGBA", (W, 60), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    pts = [(x, rope_y(x)) for x in range(-20, W + 21, 8)]
    d.line([(x, y + 1.5) for x, y in pts], fill=(0, 0, 0, 60), width=3)
    d.line(pts, fill=(150, 150, 150, 255), width=2)
    d.line([(x, y - 0.6) for x, y in pts], fill=(255, 255, 255, 110), width=1)
    # fade ends
    m = Image.new("L", (W, 60), 255)
    md = ImageDraw.Draw(m)
    edge = int(W * 0.08)
    for x in range(edge):
        v = int(255 * x / edge)
        md.line([(x, 0), (x, 60)], fill=v)
        md.line([(W - 1 - x, 0), (W - 1 - x, 60)], fill=v)
    a = lay.split()[3]
    from PIL import ImageChops
    lay.putalpha(ImageChops.multiply(a, m))
    base.alpha_composite(lay, (0, int(slide)))

# ---------- helpers ----------
def ease_io(x): return 4 * x ** 3 if x < 0.5 else 1 - (-2 * x + 2) ** 3 / 2
def back_out(x, a=1.4): x -= 1; return 1 + (a + 1) * x ** 3 + a * x ** 2
def smooth(x, a, b):
    t = max(0, min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t)
def elastic_swing(t, amp, dur=1.8):
    if t < 0 or t > dur: return 0
    return amp * math.exp(-3.2 * t) * math.cos(2 * math.pi * 2.2 * t)

def cursor(base, x, y, hand=False):
    d = ImageDraw.Draw(base)
    if hand:
        d.ellipse([x - 7, y - 7, x + 7, y + 7], fill=(255, 255, 255, 230), outline=(0, 0, 0))
        return
    pts = [(x, y), (x, y + 18), (x + 5, y + 14), (x + 9, y + 22), (x + 12, y + 20), (x + 8, y + 13), (x + 14, y + 13)]
    d.polygon(pts, fill=(255, 255, 255), outline=(0, 0, 0))

def caption(base, text, alpha=1.0):
    f = font(F, 18)
    d0 = ImageDraw.Draw(base)
    tw = d0.textlength(text, font=f)
    lay = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    x0 = W / 2 - tw / 2 - 18
    d.rounded_rectangle([x0, H - 98, W / 2 + tw / 2 + 18, H - 62], 18, fill=(15, 15, 20, int(190 * alpha)))
    d.text((W / 2 - tw / 2, H - 90), text, font=f, fill=(255, 255, 255, int(255 * alpha)))
    base.alpha_composite(lay)

def badge(base, text):
    lay = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.rounded_rectangle([W - 210, 14 + 640, W - 14, 46 + 640], 10, fill=(0, 0, 0, 0))
    base.alpha_composite(lay)

# ---------- shots ----------
SNIP1 = (130, 255, 615, 425)   # landscape photo area
SNIP2 = (712, 200, 1180, 512)  # chart
P1 = DESK.crop(SNIP1)
P2 = DESK.crop(SNIP2)
S1, PV1 = card_sprite(P1)
S2, PV2 = card_sprite(P2)

TOTAL = 15.0
frames = int(TOTAL * FPS) if not os.environ.get("TEST") else 0
random.seed(3)
TILT1, TILT2 = 1.8, -1.4

def card_frame_px(spr, pivot):
    # where the card's frame (excluding clip/shadow pad) sits relative to pivot
    pad = 30
    return pad, pad + 20, spr.width - pad, spr.height - pad

for fi in range(frames):
    t = fi / FPS
    base = DESK.convert("RGBA")
    cap = None

    # timeline
    # line slide: hidden until 2.6, shown until 13.0
    if t < 2.55: slide = -230
    elif t < 3.0: slide = -230 + 230 * back_out((t - 2.55) / 0.45, 0.6)
    elif t < 13.0: slide = 0
    elif t < 13.25: slide = -230 * ((t - 13.0) / 0.25) ** 2
    else: slide = -230

    # cards state
    n_live = 0
    c1_vis = t >= 3.25 and t < 11.3
    c2_vis = t >= 7.3

    # snipping overlay 1 (0.8 - 2.4)
    def snip(t0, rect, start_pt):
        global base
        t1 = t - t0
        if 0 <= t1 < 1.6:
            dim = Image.new("RGBA", base.size, (0, 0, 0, 110))
            if t1 > 0.5:
                k = min(1, (t1 - 0.5) / 0.9)
                k = ease_io(k)
                x1 = rect[0] + (rect[2] - rect[0]) * k
                y1 = rect[1] + (rect[3] - rect[1]) * k
                region = DESK.crop((rect[0], rect[1], int(max(rect[0] + 1, x1)), int(max(rect[1] + 1, y1))))
                ov = base.copy()
                ov.alpha_composite(dim)
                ov.paste(region, (rect[0], rect[1]))
                d = ImageDraw.Draw(ov)
                d.rectangle([rect[0], rect[1], x1, y1], outline=(255, 255, 255), width=2)
                base = ov
                cursor(base, x1, y1)
            else:
                ov = base.copy(); ov.alpha_composite(dim); base = ov
                # snip toolbar
                d = ImageDraw.Draw(base)
                d.rounded_rectangle([W / 2 - 110, 20 + 0, W / 2 + 110, 64], 10, fill=(43, 43, 48))
                for i in range(5):
                    d.rounded_rectangle([W / 2 - 96 + i * 40, 30, W / 2 - 70 + i * 40, 54], 5, fill=(80, 80, 88) if i else (0, 120, 215))
                mx = start_pt[0] + (rect[0] - start_pt[0]) * smooth(t1, 0, 0.45)
                my = start_pt[1] + (rect[1] - start_pt[1]) * smooth(t1, 0, 0.45)
                cursor(base, mx, my)
            return True
        return False

    snipping = snip(0.8, SNIP1, (500, 600)) or snip(5.0, SNIP2, (660, 560))

    if slide > -229:
        draw_rope(base, slide)

    n = (1 if t >= 3.25 else 0) + (1 if t >= 7.3 else 0)
    if t >= 11.3: n = 1 if c2_vis else 0
    # slot positions (animated shift when 2nd card arrives / 1st leaves)
    def pos_for(card):
        if card == 1:
            if t < 6.9: return slot_x(0, 1)
            k = back_out(min(1, (t - 6.9) / 0.55), 0.6)
            return slot_x(0, 1) + (slot_x(0, 2) - slot_x(0, 1)) * k
        else:
            if t < 11.6: return slot_x(1, 2)
            k = back_out(min(1, (t - 11.6) / 0.55), 0.6)
            return slot_x(1, 2) + (slot_x(0, 1) - slot_x(1, 2)) * k

    def breeze(phase): return 0.6 * math.sin(t * 1.3 + phase)

    # flight helper
    def flight(t0, rect, spr, pivot, target_x, tilt):
        k_raw = (t - t0) / 0.65
        if not (0 <= k_raw <= 1.0): return False
        k = ease_io(k_raw)
        chrome = smooth(k, 0.35, 1)
        fl, ft, fr, fb = card_frame_px(spr, pivot)
        to_w, to_h = fr - fl, fb - ft
        tx = target_x
        ty = rope_y(tx) - 9.5 + 13 + slide  # frame top below clip
        fw = rect[2] - rect[0]; fh = rect[3] - rect[1]
        w = fw + (to_w - fw) * k; h = fh + (to_h - fh) * k
        cx = (rect[0] + fw / 2) + (tx - (rect[0] + fw / 2)) * k
        top = rect[1] + (ty - rect[1]) * k - math.sin(math.pi * k) * 30
        photo = DESK.crop(rect).resize((max(2, int(w)), max(2, int(h))), Image.LANCZOS).convert("RGBA")
        r = int(16 * k)
        lay = Image.new("RGBA", (int(w) + 60, int(h) + 60), (0, 0, 0, 0))
        sh = Image.new("RGBA", lay.size, (0, 0, 0, 0))
        ImageDraw.Draw(sh).rounded_rectangle([30, 35, 30 + w, 35 + h], r, fill=(0, 0, 0, int(80 * smooth(k, 0, 0.3))))
        lay = Image.alpha_composite(lay, sh.filter(ImageFilter.GaussianBlur(9)))
        ImageDraw.Draw(lay).rounded_rectangle([30, 30, 30 + w, 30 + h], r, fill=(244, 244, 247, int(235 * chrome)))
        inset = int(5 * k)
        pin = photo.resize((max(2, int(w) - 2 * inset), max(2, int(h) - 2 * inset)))
        m = Image.new("L", pin.size, 0)
        ImageDraw.Draw(m).rounded_rectangle([0, 0, pin.width - 1, pin.height - 1], max(0, r - inset), fill=255)
        lay.paste(pin, (30 + inset, 30 + inset), m)
        paste_rot(base, lay, (30 + w / 2, 30), (cx, top), tilt * k)
        return True

    flying1 = flight(2.6, SNIP1, S1, PV1, slot_x(0, 1), TILT1)
    flying2 = flight(6.6, SNIP2, S2, PV2, slot_x(1, 2), TILT2)

    # hanging cards
    def hang(spr, pivot, x, tilt, t_land, extra=0, fall=None, alpha=1.0):
        ang = tilt + breeze(x / 200) + elastic_swing(t - t_land, 6) + extra
        y = rope_y(x) - 9.5 + slide
        if fall is not None:
            ft = t - fall
            ang += (22 * (ft / 0.6) ** 2) if ft > 0 else 0
            y += 300 * (max(0, ft) / 0.6) ** 2
            alpha = max(0, 1 - max(0, ft - 0.1) / 0.5)
        paste_rot(base, spr, pivot, (x, y), ang, alpha)

    if t >= 3.25 and not flying1:
        fall = 10.7 if t >= 10.7 else None
        if not (fall is not None and t > 11.3):
            nud = 0
            if 8.6 <= t <= 10.5:
                nud = 3 * math.exp(-3 * (t - 8.6)) * math.cos(2 * math.pi * 1.8 * (t - 8.6))
            hang(S1, PV1, pos_for(1), TILT1, 3.25, nud, fall)
    if t >= 7.25 and not flying2:
        hang(S2, PV2, pos_for(2), TILT2, 7.25)

    # copied badge under card 1 (8.6 - 9.9)
    if 8.6 <= t <= 10.0:
        a = min(1, (t - 8.6) / 0.18) * (1 - smooth(t, 9.8, 10.0))
        x = pos_for(1)
        lay = Image.new("RGBA", base.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(lay)
        d.rounded_rectangle([x - 50, 158, x + 50, 182], 12, fill=(250, 250, 250, int(240 * a)), outline=(0, 0, 0, int(50 * a)))
        d.text((x - 36, 162), "\u2713  Copied", font=font(FB, 12), fill=(30, 30, 30, int(255 * a)))
        base.alpha_composite(lay)

    # discard cross on hover (10.2 - 10.7)
    if 10.0 <= t < 10.7:
        x = pos_for(1)
        d = ImageDraw.Draw(base)
        d.ellipse([x - 70, 26, x - 50, 46], fill=(250, 250, 250), outline=(120, 120, 120))
        d.text((x - 64, 28), "\u2715", font=font(FB, 11), fill=(40, 40, 40))

    # cursor outside snips
    if not snipping:
        if t < 0.8: cursor(base, 500, 600)
        elif 2.4 <= t < 5.0: cursor(base, SNIP1[2], SNIP1[3])
        elif 6.6 <= t < 8.0:
            k = smooth(t, 7.6, 8.4); cursor(base, SNIP2[2], SNIP2[3])
        elif 8.0 <= t < 10.0:
            k = smooth(t, 8.0, 8.5)
            cursor(base, SNIP2[2] + (pos_for(1) - SNIP2[2]) * k, SNIP2[3] + (100 - SNIP2[3]) * k, hand=k > 0.95)
        elif 10.0 <= t < 10.8:
            k = smooth(t, 10.0, 10.4)
            x = pos_for(1)
            cursor(base, x + (x - 60 - x) * k, 100 + (36 - 100) * k, hand=True)
        elif 10.8 <= t < 13.0:
            cursor(base, pos_for(1) - 60 + 300 * smooth(t, 11.5, 12.6), 36 + 450 * smooth(t, 11.5, 12.6))

    # captions
    if t < 0.8: cap = "Clipdrop runs quietly in the tray"
    elif t < 2.5: cap = "Win + Shift + S  -  snip anything"
    elif t < 5.0: cap = "It flies up and hangs on the line"
    elif t < 8.4: cap = "Snip again - the line makes room"
    elif t < 10.0: cap = "Click a photo to copy it"
    elif t < 12.9: cap = "The cross takes it down"
    elif t < 13.3: cap = None
    if cap: caption(base, cap)

    if t >= 13.3:
        k = smooth(t, 13.3, 13.9)
        lay = Image.new("RGBA", base.size, (12, 12, 16, int(225 * k)))
        d = ImageDraw.Draw(lay)
        f1 = font(FB, 56); f2 = font(F, 22)
        tx = "Clipdrop"; tw = d.textlength(tx, font=f1)
        d.text((W / 2 - tw / 2, H / 2 - 60), tx, font=f1, fill=(255, 255, 255, int(255 * k)))
        s2 = "Your screenshots, hung on a line. For Windows."; tw2 = d.textlength(s2, font=f2)
        d.text((W / 2 - tw2 / 2, H / 2 + 14), s2, font=f2, fill=(200, 200, 210, int(255 * k)))
        base.alpha_composite(lay)

    # simulated label
    d = ImageDraw.Draw(base)
    d.text((14, H - 70), "Simulated demo", font=font(F, 12), fill=(255, 255, 255, 150))

    base.convert("RGB").save(f"{OUT}/f{fi:04d}.png")
print("frames", frames)
