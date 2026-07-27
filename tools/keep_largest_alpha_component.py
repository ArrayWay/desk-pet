#!/usr/bin/env python3
"""Keep the largest connected opaque component in companion PNG frames."""

from __future__ import annotations

import argparse
from collections import deque
from pathlib import Path

from PIL import Image


def largest_component(image: Image.Image) -> set[tuple[int, int]]:
    alpha = image.getchannel("A")
    remaining = {
        (x, y)
        for y in range(image.height)
        for x in range(image.width)
        if alpha.getpixel((x, y)) >= 24
    }
    largest: set[tuple[int, int]] = set()
    while remaining:
        start = remaining.pop()
        component = {start}
        queue = deque([start])
        while queue:
            x, y = queue.popleft()
            for neighbor in (
                (x - 1, y - 1),
                (x, y - 1),
                (x + 1, y - 1),
                (x - 1, y),
                (x + 1, y),
                (x - 1, y + 1),
                (x, y + 1),
                (x + 1, y + 1),
            ):
                if neighbor in remaining:
                    remaining.remove(neighbor)
                    component.add(neighbor)
                    queue.append(neighbor)
        if len(component) > len(largest):
            largest = component
    return largest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()

    args.destination.mkdir(parents=True, exist_ok=True)
    for source_path in sorted(args.source.glob("*.png")):
        with Image.open(source_path) as opened:
            image = opened.convert("RGBA")
        keep = largest_component(image)
        pixels = image.load()
        for y in range(image.height):
            for x in range(image.width):
                if (x, y) not in keep:
                    r, g, b, _ = pixels[x, y]
                    pixels[x, y] = (r, g, b, 0)
        image.save(args.destination / source_path.name)
        print(f"{source_path.name}: kept {len(keep)} pixels")


if __name__ == "__main__":
    main()