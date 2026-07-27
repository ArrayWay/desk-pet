#!/usr/bin/env python3
"""Analyze manually generated pet sprite sheets without modifying source images."""

from __future__ import annotations

import argparse
import json
from collections import deque
from pathlib import Path

from PIL import Image, ImageDraw


def color_distance(left: tuple[int, int, int], right: tuple[int, int, int]) -> int:
    return max(abs(left[index] - right[index]) for index in range(3))


def estimate_background(image: Image.Image) -> tuple[int, int, int]:
    rgb = image.convert("RGB")
    width, height = rgb.size
    samples: list[tuple[int, int, int]] = []
    step_x = max(1, width // 64)
    step_y = max(1, height // 64)
    for x in range(0, width, step_x):
        samples.append(rgb.getpixel((x, 0)))
        samples.append(rgb.getpixel((x, height - 1)))
    for y in range(0, height, step_y):
        samples.append(rgb.getpixel((0, y)))
        samples.append(rgb.getpixel((width - 1, y)))
    return tuple(sorted(channel)[len(channel) // 2] for channel in zip(*samples))


def border_background_mask(
    image: Image.Image,
    background: tuple[int, int, int],
    threshold: int,
) -> bytearray:
    rgb = image.convert("RGB")
    width, height = rgb.size
    pixels = rgb.load()
    mask = bytearray(width * height)
    queue: deque[tuple[int, int]] = deque()

    def enqueue(x: int, y: int) -> None:
        index = y * width + x
        if mask[index] or color_distance(pixels[x, y], background) > threshold:
            return
        mask[index] = 1
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
    return mask


def projection_bands(values: list[int], minimum: int, gap: int) -> list[tuple[int, int]]:
    active = [index for index, value in enumerate(values) if value >= minimum]
    if not active:
        return []
    bands: list[tuple[int, int]] = []
    start = active[0]
    previous = active[0]
    for current in active[1:]:
        if current - previous > gap:
            bands.append((start, previous + 1))
            start = current
        previous = current
    bands.append((start, previous + 1))
    return bands


def connected_components(
    foreground: bytearray,
    width: int,
    height: int,
    minimum_area: int,
) -> list[dict[str, int]]:
    visited = bytearray(width * height)
    components: list[dict[str, int]] = []
    for start, value in enumerate(foreground):
        if not value or visited[start]:
            continue
        visited[start] = 1
        stack = [start]
        area = 0
        min_x = width
        min_y = height
        max_x = 0
        max_y = 0
        while stack:
            current = stack.pop()
            area += 1
            x = current % width
            y = current // width
            min_x = min(min_x, x)
            min_y = min(min_y, y)
            max_x = max(max_x, x)
            max_y = max(max_y, y)
            for neighbor in (current - 1, current + 1, current - width, current + width):
                if neighbor < 0 or neighbor >= width * height or visited[neighbor]:
                    continue
                neighbor_x = neighbor % width
                if abs(neighbor_x - x) > 1 or not foreground[neighbor]:
                    continue
                visited[neighbor] = 1
                stack.append(neighbor)
        if area >= minimum_area:
            components.append(
                {
                    "area": area,
                    "left": min_x,
                    "top": min_y,
                    "right": max_x + 1,
                    "bottom": max_y + 1,
                    "center_x": (min_x + max_x + 1) // 2,
                    "center_y": (min_y + max_y + 1) // 2,
                }
            )
    return sorted(components, key=lambda item: (item["center_y"], item["center_x"]))


def analyze(path: Path, output_dir: Path, threshold: int) -> dict[str, object]:
    with Image.open(path) as opened:
        image = opened.convert("RGBA")
    width, height = image.size
    background = estimate_background(image)
    background_mask = border_background_mask(image, background, threshold)
    foreground = bytearray(0 if value else 1 for value in background_mask)
    row_counts = [sum(foreground[y * width : (y + 1) * width]) for y in range(height)]
    column_counts = [sum(foreground[y * width + x] for y in range(height)) for x in range(width)]
    row_bands = projection_bands(row_counts, max(3, width // 500), max(2, height // 250))
    column_bands = projection_bands(column_counts, max(3, height // 500), max(2, width // 250))
    components = connected_components(
        foreground,
        width,
        height,
        minimum_area=max(64, width * height // 20000),
    )

    preview = image.convert("RGB")
    draw = ImageDraw.Draw(preview)
    for index, (top, bottom) in enumerate(row_bands, start=1):
        draw.rectangle((0, top, width - 1, bottom - 1), outline=(0, 120, 255), width=2)
        draw.text((4, top + 4), f"row-band {index}", fill=(0, 80, 220))
    for index, component in enumerate(components, start=1):
        box = (
            component["left"],
            component["top"],
            component["right"] - 1,
            component["bottom"] - 1,
        )
        draw.rectangle(box, outline=(255, 30, 30), width=2)
        draw.text((component["left"] + 2, component["top"] + 2), str(index), fill=(200, 0, 0))

    output_dir.mkdir(parents=True, exist_ok=True)
    preview_path = output_dir / f"{path.stem}-analysis.png"
    preview.save(preview_path)
    return {
        "source": str(path),
        "size": [width, height],
        "mode": image.mode,
        "estimated_background": list(background),
        "background_threshold": threshold,
        "foreground_fraction": round(sum(foreground) / len(foreground), 6),
        "row_bands": [list(band) for band in row_bands],
        "column_bands": [list(band) for band in column_bands],
        "components": components,
        "preview": str(preview_path),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("images", nargs="+")
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--json-out", required=True)
    parser.add_argument("--background-threshold", type=int, default=28)
    args = parser.parse_args()

    output_dir = Path(args.output_dir).resolve()
    results = [
        analyze(Path(image_path).resolve(), output_dir, args.background_threshold)
        for image_path in args.images
    ]
    json_path = Path(args.json_out).resolve()
    json_path.parent.mkdir(parents=True, exist_ok=True)
    json_path.write_text(json.dumps(results, ensure_ascii=True, indent=2), encoding="utf-8")
    print(json.dumps(results, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()