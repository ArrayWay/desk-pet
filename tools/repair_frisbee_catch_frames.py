#!/usr/bin/env python3
"""Repair checkerboard pixels in the frisbee catch reference sheet."""

from __future__ import annotations

import argparse
from collections import deque
from pathlib import Path

from PIL import Image

CELL_SIZE = (192, 208)
SLOT_SIZE = (682, 1024)
CHECKER_PERIOD = 41
CHECKER_DARK = 203
CHECKER_LIGHT = 253


def is_checker_pixel(rgb: tuple[int, int, int]) -> bool:
    if max(rgb) - min(rgb) > 4:
        return False
    return CHECKER_DARK - 12 <= rgb[0] <= CHECKER_LIGHT + 8


def remove_checkerboard(image: Image.Image) -> Image.Image:
    rgba = image.convert("RGBA")
    width, height = rgba.size
    pixels = rgba.load()
    background = bytearray(width * height)
    queue: deque[tuple[int, int]] = deque()

    def enqueue(x: int, y: int) -> None:
        index = y * width + x
        if background[index] or not is_checker_pixel(pixels[x, y][:3]):
            return
        background[index] = 1
        queue.append((x, y))

    for x in range(width):
        enqueue(x, 0)
        enqueue(x, height - 1)
    for y in range(height):
        enqueue(0, y)
        enqueue(width - 1, y)

    while queue:
        x, y = queue.popleft()
        if x > 0:
            enqueue(x - 1, y)
        if x + 1 < width:
            enqueue(x + 1, y)
        if y > 0:
            enqueue(x, y - 1)
        if y + 1 < height:
            enqueue(x, y + 1)

    for y in range(rgba.height):
        for x in range(rgba.width):
            if background[y * width + x]:
                pixels[x, y] = (0, 0, 0, 0)
    return rgba


def normalize(image: Image.Image, padding: int) -> Image.Image:
    bbox = image.getbbox()
    if bbox is None:
        return Image.new("RGBA", CELL_SIZE, (0, 0, 0, 0))
    content = image.crop(bbox)
    content.thumbnail((CELL_SIZE[0] - padding * 2, CELL_SIZE[1] - padding * 2), Image.Resampling.LANCZOS)
    frame = Image.new("RGBA", CELL_SIZE, (0, 0, 0, 0))
    frame.alpha_composite(content, ((CELL_SIZE[0] - content.width) // 2, CELL_SIZE[1] - padding - content.height))
    return frame


def remove_upper_subject(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A")
    width, height = image.size
    visited = bytearray(width * height)
    pixels = alpha.load()
    cleaned = image.copy()
    output = cleaned.load()

    for start_y in range(height):
        for start_x in range(width):
            offset = start_y * width + start_x
            if visited[offset] or pixels[start_x, start_y] == 0:
                continue
            stack = [(start_x, start_y)]
            visited[offset] = 1
            component: list[tuple[int, int]] = []
            minimum_y = height
            maximum_y = 0
            while stack:
                x, y = stack.pop()
                component.append((x, y))
                minimum_y = min(minimum_y, y)
                maximum_y = max(maximum_y, y)
                for next_x, next_y in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                    if next_x < 0 or next_y < 0 or next_x >= width or next_y >= height:
                        continue
                    next_offset = next_y * width + next_x
                    if visited[next_offset] or pixels[next_x, next_y] == 0:
                        continue
                    visited[next_offset] = 1
                    stack.append((next_x, next_y))
            if maximum_y < 140:
                for x, y in component:
                    output[x, y] = (0, 0, 0, 0)
    return cleaned


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--padding", type=int, default=10)
    args = parser.parse_args()

    with Image.open(args.source) as opened:
        source = opened.convert("RGB")
    args.output_dir.mkdir(parents=True, exist_ok=True)
    for index in range(6):
        column = index % 3
        row = index // 3
        left = column * SLOT_SIZE[0]
        top = row * SLOT_SIZE[1]
        right = source.width if column == 2 else (column + 1) * SLOT_SIZE[0]
        bottom = source.height if row == 1 else (row + 1) * SLOT_SIZE[1]
        cleaned = remove_checkerboard(source.crop((left, top, right, bottom)))
        frame = normalize(cleaned, args.padding)
        remove_upper_subject(frame).save(args.output_dir / f"{index:02d}.png")


if __name__ == "__main__":
    main()
