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


MIX_ORIGIN_X = 24.115
MIX_BASELINE_Y = 79.855
MIX_CAP_HEIGHT = 47.505
MIX_WIDTH = 121.150 - MIX_ORIGIN_X
MIX_M_POLY = [
    (46.281, 57.893), (33.984, 32.350), (24.115, 32.350), (24.115, 79.855),
    (32.699, 79.855), (32.699, 48.703), (42.498, 68.300), (49.525, 68.300),
    (59.862, 47.893), (59.862, 79.855), (68.445, 79.855), (68.445, 32.350),
    (58.579, 32.350),
]
MIX_I_DOT = (73.648, 29.918, 83.920, 40.863)
MIX_I_STEM = (74.256, 45.797, 83.311, 79.855)
MIX_X_POLY = [
    (119.801, 45.797), (110.205, 45.797), (104.527, 55.865), (98.311, 45.797),
    (87.838, 45.797), (98.852, 62.219), (87.297, 79.855), (96.893, 79.855),
    (103.582, 68.771), (110.543, 79.855), (121.150, 79.855), (109.326, 62.285),
]


def wordmark(canvas, center_x, baseline, font_size, font_directory):
    suffix_part = CONFIG['wordmark'][1]
    suffix_font = ImageFont.truetype(str(font_directory / suffix_part['font']),
                                     round(font_size * SUPERSAMPLE))
    cap_top = suffix_font.getbbox('S', anchor='ls')[1]
    cap_h = -cap_top
    mix_scale = cap_h / MIX_CAP_HEIGHT
    mix_w = MIX_WIDTH * mix_scale
    gap = cap_h * 0.08
    condense = suffix_part.get('condense', 1.0)
    sb_left, _, sb_right, _ = suffix_font.getbbox(suffix_part['text'], anchor='ls')
    suffix_ink_w = (sb_right - sb_left) * condense
    total_w = mix_w + gap + suffix_ink_w
    left = center_x * SUPERSAMPLE - total_w / 2
    base_y = baseline * SUPERSAMPLE

    def tx(x):
        return left + (x - MIX_ORIGIN_X) * mix_scale

    def ty(y):
        return base_y + (y - MIX_BASELINE_Y) * mix_scale

    canvas.draw.polygon([(tx(x), ty(y)) for x, y in MIX_M_POLY], fill=PALETTE['white'])
    canvas.draw.ellipse((tx(MIX_I_DOT[0]), ty(MIX_I_DOT[1]),
                         tx(MIX_I_DOT[2]), ty(MIX_I_DOT[3])), fill=PALETTE['white'])
    canvas.draw.rectangle((tx(MIX_I_STEM[0]), ty(MIX_I_STEM[1]),
                           tx(MIX_I_STEM[2]), ty(MIX_I_STEM[3])), fill=PALETTE['white'])
    canvas.draw.polygon([(tx(x), ty(y)) for x, y in MIX_X_POLY], fill=PALETTE['white'])

    suffix_h = round(font_size * SUPERSAMPLE * 1.5)
    suffix_w = round(sb_right - sb_left + 8)
    suffix_base = round(font_size * SUPERSAMPLE * 1.1)
    tmp = Image.new('RGBA', (suffix_w, suffix_h), (0, 0, 0, 0))
    ImageDraw.Draw(tmp).text((-sb_left, suffix_base), suffix_part['text'],
                             font=suffix_font, fill=PALETTE['white'], anchor='ls')
    scaled = tmp.resize((round(suffix_w * condense), suffix_h), Image.Resampling.LANCZOS)
    canvas.image.paste(scaled, (round(left + mix_w + gap), round(base_y - suffix_base)), scaled)


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
