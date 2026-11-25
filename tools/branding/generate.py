"""Render MixSonic's Windows 8.1 / Phone 8.1 branding from scalable geometry.

Requires Pillow and the Segoe WP fonts supplied by Windows. Run from any folder:
    python tools/branding/generate.py
    python tools/branding/generate.py --font-directory C:/Windows/Fonts

The palette, wordmark and supported platform asset sizes live in brand.json.
Generated package resources stay PNG; the optional review sheet is WebP.
"""

import argparse
import json
import math
import os
import re
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
CONFIG = json.loads(Path(__file__).with_name('brand.json').read_text(encoding='utf-8'))
PALETTE = CONFIG['palette']
SUPERSAMPLE = 4


class Canvas:
    def __init__(self, width, height, background):
        self.width, self.height = width, height
        self.image = Image.new('RGBA', (width * SUPERSAMPLE, height * SUPERSAMPLE), background)
        self.draw = ImageDraw.Draw(self.image)

    @staticmethod
    def box(values):
        return tuple(round(value * SUPERSAMPLE) for value in values)

    def ellipse(self, values, fill):
        self.draw.ellipse(self.box(values), fill=fill)

    def rectangle(self, values, fill):
        self.draw.rectangle(self.box(values), fill=fill)

    def polygon(self, points, fill):
        self.draw.polygon([self.box(point) for point in points], fill=fill)

    def round_rectangle(self, values, radius, fill):
        self.draw.rounded_rectangle(self.box(values), radius=round(radius * SUPERSAMPLE), fill=fill)

    def result(self):
        return self.image.resize((self.width, self.height), Image.Resampling.LANCZOS)


def mark(canvas, left, top, diameter):
    """Headphones, two note eyes and a smile, in a 1000-unit white disc."""
    factor = diameter / 1000

    def bounds(values):
        return tuple((left if index % 2 == 0 else top) + value * factor
                     for index, value in enumerate(values))

    canvas.ellipse(bounds((0, 0, 1000, 1000)), PALETTE['white'])
    canvas.draw.arc(canvas.box(bounds((175, 165, 825, 815))), start=180, end=360,
                    fill=PALETTE['accent'], width=max(1, round(56 * factor * SUPERSAMPLE)))
    for values in ((128, 435, 268, 690), (732, 435, 872, 690)):
        canvas.round_rectangle(bounds(values), 68 * factor, PALETTE['accent'])
    # Symmetrical note eyes centered around x=500 with equal spacing to the earcups.
    canvas.ellipse(bounds((360, 480, 476, 568)), PALETTE['accent'])
    canvas.polygon([bounds(point) for point in ((444, 524), (476, 524), (476, 333), (444, 345))],
                   PALETTE['accent'])
    canvas.ellipse(bounds((524, 480, 640, 568)), PALETTE['accent'])
    canvas.polygon([bounds(point) for point in ((608, 524), (640, 524), (640, 375),
                                               (688, 395), (688, 355), (608, 333))],
                   PALETTE['accent'])
    canvas.draw.chord(canvas.box(bounds((355, 505, 645, 765))), start=0, end=180,
                      fill=PALETTE['accent'])


MIXSONIC_ORIGIN_X = 24.115
MIXSONIC_BASELINE_Y = 79.855
MIXSONIC_CAP_HEIGHT = 47.505
MIXSONIC_WIDTH = 261.0 - MIXSONIC_ORIGIN_X
MIXSONIC_PATHS = [
    "F1 M 46.281,57.893 L 33.984,32.350 L 24.115,32.350 L 24.115,79.855 L 32.699,79.855 L 32.699,48.703 L 42.498,68.300 L 49.525,68.300 L 59.862,47.893 L 59.862,79.855 L 68.445,79.855 L 68.445,32.350 L 58.579,32.350 L 46.281,57.893 Z",
    "F1 M 78.785,29.918 C 77.205,29.918 75.957,30.447 75.033,31.505 C 74.110,32.565 73.648,33.859 73.648,35.391 C 73.648,36.922 74.110,38.219 75.033,39.277 C 75.957,40.336 77.205,40.863 78.785,40.863 C 80.359,40.863 81.609,40.348 82.533,39.311 C 83.457,38.273 83.920,36.969 83.920,35.391 C 83.920,33.814 83.457,32.508 82.533,31.472 C 81.609,30.436 80.359,29.918 78.785,29.918 Z",
    "F1 M 74.256,79.855 L 83.311,79.855 L 83.311,45.797 L 74.256,45.797 L 74.256,79.855 Z",
    "F1 M 119.801,45.797 L 110.205,45.797 L 104.527,55.865 L 98.311,45.797 L 87.838,45.797 L 98.852,62.219 L 87.297,79.855 L 96.893,79.855 L 103.582,68.771 L 110.543,79.855 L 121.150,79.855 L 109.326,62.285 L 119.801,45.797 Z",
    "F1 M 150.3,35.2 C 147.8,32.9 144.0,31.7 139.2,31.7 C 131.0,31.7 125.6,36.2 125.6,43.0 C 125.6,50.0 130.6,53.2 138.8,56.0 C 145.6,58.3 148.3,60.7 148.3,66.2 C 148.3,72.7 143.5,76.9 136.8,76.9 C 132.4,76.9 128.6,75.4 125.9,72.8 L 124.2,76.1 C 127.3,79.0 131.6,80.6 136.9,80.6 C 146.1,80.6 152.4,74.8 152.4,66.0 C 152.4,58.6 147.6,54.9 139.5,52.2 C 132.6,49.8 129.7,47.6 129.7,42.8 C 129.7,37.8 133.5,35.3 139.3,35.3 C 143.1,35.3 146.2,36.4 148.5,38.4 Z",
    "F1 M 180.188,73.199 C 178.341,75.698 175.479,76.949 171.607,76.949 C 167.778,76.949 164.938,75.698 163.091,73.199 C 161.244,70.697 160.321,67.309 160.321,63.029 C 160.321,58.839 161.255,55.473 163.126,52.926 C 164.995,50.382 167.823,49.108 171.607,49.108 C 175.434,49.108 178.286,50.382 180.155,52.926 C 182.023,55.473 182.958,58.839 182.958,63.029 C 182.958,67.309 182.036,70.697 180.188,73.199 Z M 186.032,55.967 C 185.380,53.828 184.413,51.979 183.126,50.426 C 181.843,48.871 180.245,47.656 178.331,46.777 C 176.415,45.898 174.175,45.459 171.607,45.459 C 169.083,45.459 166.866,45.898 164.950,46.777 C 163.036,47.656 161.438,48.871 160.153,50.426 C 158.869,51.979 157.887,53.828 157.214,55.967 C 156.538,58.107 156.200,60.461 156.200,63.029 C 156.200,65.596 156.526,67.960 157.179,70.123 C 157.833,72.287 158.800,74.145 160.085,75.698 C 161.370,77.253 162.979,78.459 164.917,79.313 C 166.854,80.170 169.083,80.598 171.607,80.598 C 174.083,80.598 176.292,80.170 178.229,79.313 C 180.167,78.459 181.776,77.253 183.059,75.698 C 184.345,74.145 185.325,72.287 186.001,70.123 C 186.677,67.960 187.013,65.596 187.013,63.029 C 187.013,60.461 186.686,58.107 186.032,55.967 Z",
    "F1 M 193.0,79.855 L 196.985,79.855 L 196.985,56.8 C 198.2,52.0 201.9,49.108 206.4,49.108 C 211.7,49.108 214.5,52.3 214.5,58.4 L 214.5,79.855 L 218.485,79.855 L 218.485,57.8 C 218.485,49.6 214.0,45.459 207.1,45.459 C 202.3,45.459 198.7,47.7 196.985,51.4 L 196.85,46.338 L 193.0,46.338 Z",
    "F1 M 225.916,79.855 L 229.901,79.855 L 229.901,46.338 L 225.916,46.338 L 225.916,79.855 Z",
    "F1 M 227.875,31.810 C 226.973,31.810 226.242,32.113 225.678,32.722 C 225.113,33.329 224.834,34.131 224.834,35.121 C 224.834,36.111 225.113,36.910 225.678,37.520 C 226.242,38.129 226.973,38.432 227.875,38.432 C 228.774,38.432 229.518,38.139 230.104,37.553 C 230.689,36.969 230.982,36.158 230.982,35.121 C 230.982,34.131 230.689,33.329 230.104,32.722 C 229.518,32.113 228.774,31.810 227.875,31.810 Z",
    "F1 M 259.5,50.8 C 256.8,47.2 253.0,45.459 248.4,45.459 C 239.2,45.459 233.6,52.3 233.6,63.029 C 233.6,73.8 239.2,80.598 248.4,80.598 C 253.2,80.598 257.1,78.8 259.8,75.0 L 257.2,72.4 C 255.0,75.4 252.0,76.949 248.5,76.949 C 241.5,76.949 237.7,71.4 237.7,63.029 C 237.7,54.6 241.5,49.108 248.5,49.108 C 252.0,49.108 254.9,50.6 257.0,53.5 Z",
]


def _eval_cubic(p0, p1, p2, p3, steps=24):
    pts = []
    for i in range(1, steps + 1):
        t = i / steps
        mt = 1 - t
        pts.append((
            mt**3 * p0[0] + 3 * mt**2 * t * p1[0] + 3 * mt * t**2 * p2[0] + t**3 * p3[0],
            mt**3 * p0[1] + 3 * mt**2 * t * p1[1] + 3 * mt * t**2 * p2[1] + t**3 * p3[1],
        ))
    return pts


def _parse_path(data):
    tokens = re.findall(r'[MLCZF]|-?\d+(?:\.\d+)?', data)
    contours, cur, pos, i, cmd = [], [], (0.0, 0.0), 0, None
    while i < len(tokens):
        t = tokens[i]
        if t in 'MLCZF':
            if t == 'F':
                i += 2
                continue
            cmd = t
            i += 1
            if cmd == 'Z':
                if cur:
                    contours.append(cur)
                    cur = []
                continue
        if cmd == 'M':
            pos = (float(tokens[i]), float(tokens[i + 1]))
            i += 2
            cur = [pos]
            cmd = 'L'
        elif cmd == 'L':
            pos = (float(tokens[i]), float(tokens[i + 1]))
            i += 2
            cur.append(pos)
        elif cmd == 'C':
            p1 = (float(tokens[i]), float(tokens[i + 1]))
            p2 = (float(tokens[i + 2]), float(tokens[i + 3]))
            p3 = (float(tokens[i + 4]), float(tokens[i + 5]))
            i += 6
            cur.extend(_eval_cubic(pos, p1, p2, p3))
            pos = p3
    if cur:
        contours.append(cur)
    return contours


def wordmark(canvas, center_x, baseline, font_size, font_directory):
    cap_h = font_size * SUPERSAMPLE * 0.72
    scale = cap_h / MIXSONIC_CAP_HEIGHT
    total_w = MIXSONIC_WIDTH * scale
    left = center_x * SUPERSAMPLE - total_w / 2
    base_y = baseline * SUPERSAMPLE

    def tx(x):
        return left + (x - MIXSONIC_ORIGIN_X) * scale

    def ty(y):
        return base_y + (y - MIXSONIC_BASELINE_Y) * scale

    mask = Image.new('L', canvas.image.size, 0)
    mdraw = ImageDraw.Draw(mask)
    for data in MIXSONIC_PATHS:
        contours = _parse_path(data)
        if len(contours) == 1:
            mdraw.polygon([(tx(x), ty(y)) for x, y in contours[0]], fill=255)
        else:
            ordered = sorted(
                contours,
                key=lambda c: (max(p[0] for p in c) - min(p[0] for p in c)) * (max(p[1] for p in c) - min(p[1] for p in c)),
                reverse=True,
            )
            mdraw.polygon([(tx(x), ty(y)) for x, y in ordered[0]], fill=255)
            for hole in ordered[1:]:
                mdraw.polygon([(tx(x), ty(y)) for x, y in hole], fill=0)
    fill_layer = Image.new('RGBA', canvas.image.size, PALETTE['white'])
    canvas.image.paste(fill_layer, (0, 0), mask)


def corner_ribbons(canvas, width, height):
    # Three flat bands echo the supplied Metro design board without gradients.
    start = width * .66
    for color, rise in ((PALETTE['yellow'], .26), (PALETTE['orange'], .19),
                        (PALETTE['pink'], .12)):
        canvas.polygon([(start, height), (width, height * (1 - rise)), (width, height)], color)


def splash_accents(canvas, width, height):
    unit = min(width, height) * .15
    canvas.rectangle((0, unit * .65, unit * .75, unit * 1.75), PALETTE['yellow'])
    canvas.rectangle((unit * .65, unit * .45, unit * 1.85, unit * 1.4), PALETTE['orange'])
    canvas.rectangle((0, 0, unit * 1.05, unit * 1.05), PALETTE['pink'])
    canvas.rectangle((width - unit * .9, height - unit * 1.85, width, height), PALETTE['yellow'])
    canvas.rectangle((width - unit * 1.6, height - unit * 1.15, width, height), PALETTE['orange'])
    canvas.rectangle((width - unit * 2.3, height - unit * .5, width, height), PALETTE['pink'])


def render(width, height, kind, font_directory):
    background = (0, 0, 0, 0) if kind == 'mark' else PALETTE['black'] if kind == 'splash' else PALETTE['accent']
    canvas = Canvas(width, height, background)
    if kind == 'mark':
        diameter = width * .94
        mark(canvas, (width - diameter) / 2, (height - diameter) / 2, diameter)
    elif kind == 'small-tile':
        diameter = width * .80
        mark(canvas, (width - diameter) / 2, (height - diameter) / 2, diameter)
    elif kind == 'square':
        corner_ribbons(canvas, width, height)
        diameter = width * .62
        mark(canvas, (width - diameter) / 2, height * .105, diameter)
        wordmark(canvas, width * .47, height * .875, width * .174, font_directory)
    elif kind == 'wide':
        corner_ribbons(canvas, width, height)
        diameter = height * .66
        mark(canvas, height * .17, (height - diameter) / 2, diameter)
        wordmark(canvas, width * .69, height * .575, height * .212, font_directory)
    elif kind == 'splash':
        splash_accents(canvas, width, height)
        if width > height:
            diameter = height * .64
            mark(canvas, width * .115, (height - diameter) / 2, diameter)
            wordmark(canvas, width * .70, height * .58, height * .205, font_directory)
        else:
            diameter = width * .65
            mark(canvas, (width - diameter) / 2, height * .245, diameter)
            wordmark(canvas, width * .5, height * .745, width * .19, font_directory)
    else:
        raise ValueError(f'Unsupported asset kind: {kind}')
    return canvas.result()


def scaled_size(base_size, scale):
    # Windows resource dimensions round the fractional physical pixel count.
    return tuple(math.floor(value * scale / 100 + .5) for value in base_size)


def review_sheet(output, font_directory):
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet = Image.new('RGB', (1280, 860), '#141414')
    draw = ImageDraw.Draw(sheet)
    title = ImageFont.truetype(str(font_directory / 'SegoeWP-Light.ttf'), 36)
    label = ImageFont.truetype(str(font_directory / 'SegoeWP.ttf'), 18)
    draw.text((34, 20), ''.join(part['text'] for part in CONFIG['wordmark']), font=title, fill='white')
    panels = [(34, 92, 280, 280, 'square', 'Square tile'),
              (338, 92, 578, 280, 'wide', 'Wide tile'),
              (958, 92, 92, 92, 'mark', 'Store / app icon'),
              (1092, 92, 92, 92, 'small-tile', 'Small tile'),
              (34, 416, 620, 300, 'splash', 'Desktop splash'),
              (800, 416, 240, 400, 'splash', 'Phone splash')]
    for x, y, width, height, kind, text in panels:
        art = render(width, height, kind, font_directory)
        sheet.paste(art, (x, y), art)
        draw.text((x, y + height + 9), text, font=label, fill='#BBBBBB')
    for index, key in enumerate(('accent', 'pink', 'orange', 'yellow', 'black')):
        x = 1060 + index * 36
        draw.rectangle((x, 738, x + 26, 764), fill=PALETTE[key])
    sheet.save(output, 'WEBP', quality=86, method=6)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--font-directory', type=Path,
                        default=Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Fonts')
    parser.add_argument('--preview', type=Path, default=ROOT / '.tmp/branding/mixdrome-preview.webp')
    args = parser.parse_args()
    for part in CONFIG['wordmark']:
        if not (args.font_directory / part['font']).is_file():
            parser.error(f"Missing Segoe WP font: {args.font_directory / part['font']}")
    count = 0
    for platform, data in CONFIG['platforms'].items():
        folder = ROOT / platform / 'Assets'
        for name, asset in data['assets'].items():
            for scale in data['scales']:
                size = scaled_size(asset['size'], scale)
                result = render(*size, asset['kind'], args.font_directory)
                result.save(folder / f'{name}.scale-{scale}.png', optimize=True)
                count += 1
    review_sheet(args.preview.resolve(), args.font_directory)
    print(f'Rendered {count} PNG resources; WebP review sheet: {args.preview.resolve()}')


if __name__ == '__main__':
    main()
