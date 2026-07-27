#!/usr/bin/env python3
"""Mirror PNG animation frames horizontally without reversing frame order."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source")
    parser.add_argument("output_dir")
    args = parser.parse_args()

    source = Path(args.source).resolve()
    output_dir = Path(args.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    paths = sorted(source.glob("*.png"))
    if not paths:
        raise SystemExit(f"no PNG frames found in {source}")

    for path in paths:
        with Image.open(path) as opened:
            mirrored = opened.convert("RGBA").transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        mirrored.save(output_dir / path.name)

    print(f"mirrored {len(paths)} frames from {source} to {output_dir}")


if __name__ == "__main__":
    main()