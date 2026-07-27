#!/usr/bin/env python3
"""Remove green-screen pixels from already normalized RGBA pet frames."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


def is_chroma_key(r: int, g: int, b: int, a: int) -> bool:
    return a > 0 and g >= 100 and g > r * 1.35 and g > b * 1.2 and g - max(r, b) >= 45


def clean_frame(source: Path, target: Path) -> int:
    with Image.open(source) as opened:
        image = opened.convert("RGBA")
    pixels = image.load()
    removed = 0
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = pixels[x, y]
            if is_chroma_key(r, g, b, a):
                pixels[x, y] = (0, 0, 0, 0)
                removed += 1
    target.parent.mkdir(parents=True, exist_ok=True)
    image.save(target)
    return removed


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_root", type=Path)
    parser.add_argument("target_root", type=Path)
    parser.add_argument("states", nargs="+")
    args = parser.parse_args()

    total = 0
    for state in args.states:
        for source in sorted((args.source_root / state).glob("*.png")):
            total += clean_frame(source, args.target_root / state / source.name)
    print(f"removed {total} chroma-key pixels")


if __name__ == "__main__":
    main()