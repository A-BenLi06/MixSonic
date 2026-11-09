"""Generate disposable, tagged music for real Navidrome integration.

Run using a task-local mutagen installation. No external music is copied.
"""
import argparse
import math
from pathlib import Path
import struct
import wave
import zlib
from mutagen.wave import WAVE
from mutagen.id3 import TIT2, TPE1, TPE2, TALB, TCON, TDRC, TRCK, TPOS, USLT

parser = argparse.ArgumentParser()
parser.add_argument('music', type=Path)
args = parser.parse_args()
args.music.mkdir(parents=True, exist_ok=True)
rate = 22050
pcm = b''.join(struct.pack('<h', int(1800 * math.sin(2 * math.pi * 440 * index / rate))) for index in range(rate * 6))
def png_chunk(kind, content):
    return struct.pack('>I', len(content)) + kind + content + struct.pack('>I', zlib.crc32(kind + content) & 0xffffffff)
cover = (b'\x89PNG\r\n\x1a\n' + png_chunk(b'IHDR', struct.pack('>IIBBBBB', 2, 2, 8, 2, 0, 0, 0))
         + png_chunk(b'IDAT', zlib.compress((b'\0' + b'\x31\x90\xce' * 2) * 2)) + png_chunk(b'IEND', b''))
for index in range(125):
    album = index // 25
    artist = 'Fixture artist ' + str(album % 2 + 1)
    folder = args.music / artist / ('Fixture album ' + str(album + 1))
    folder.mkdir(parents=True, exist_ok=True)
    (folder / 'cover.png').write_bytes(cover)
    path = folder / ('Track ' + str(index + 1).zfill(3) + '.wav')
    with wave.open(str(path), 'wb') as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(rate)
        output.writeframes(pcm)
    media = WAVE(path)
    media.add_tags()
    for frame in [TIT2(encoding=3, text=['Fixture track ' + str(index + 1).zfill(3)]),
                  TPE1(encoding=3, text=[artist]), TPE2(encoding=3, text=[artist]),
                  TALB(encoding=3, text=['Fixture album ' + str(album + 1)]),
                  TCON(encoding=3, text=['Jazz' if album % 2 == 0 else 'Electronic']),
                  TDRC(encoding=3, text=[str(2020 + album)]), TRCK(encoding=3, text=[str(index % 25 + 1)]),
                  TPOS(encoding=3, text=['1'])]:
        media.tags.add(frame)
    if index == 0:
        media.tags.add(USLT(encoding=3, lang='eng', desc='', text='Fixture opening\nFixture second line\nFixture ending'))
        path.with_suffix('.lrc').write_text('[00:00.000]Fixture opening\n[00:02.000]Fixture second line\n[00:04.000]Fixture ending\n', encoding='utf-8')
    media.save()
print('Generated 125 tagged WAV files with artists, albums, genres and lyric evidence.')
