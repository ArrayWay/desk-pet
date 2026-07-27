#!/usr/bin/env python3
"""Extract contract-sized pet frames from a multi-row source sheet."""

from __future__ import annotations

import argparse
import json
from collections import deque
from pathlib import Path

from PIL import Image

CELL_SIZE = (192, 208)
DEFAULT_SOURCE_ROWS = [
    ("idle", 6),
    ("running-right", 8),
    ("waving", 4),
    ("jumping", 5),
    ("failed", 8),
    ("waiting", 6),
    ("running", 6),
    ("review", 6),
]


def color_distance(left: tuple[int, int, int], right: tuple[int, int, int]) -> int:
    return max(abs(left[index] - right[index]) for index in range(3))


def estimate_background(image: Image.Image) -> tuple[int, int, int]:
    rgb = image.convert("RGB")
    width, height = rgb.size
    samples: list[tuple[int, int, int]] = []
    step_x = max(1, width // 64)
    step_y = max(1, height // 64)
    for x in range(0, width, step_x):
        samples.extend((rgb.getpixel((x, 0)), rgb.getpixel((x, height - 1))))
    for y in range(0, height, step_y):
        samples.extend((rgb.getpixel((0, y)), rgb.getpixel((width - 1, y))))
    return tuple(sorted(channel)[len(channel) // 2] for channel in zip(*samples))


def remove_border_background(
    image: Image.Image,
    background: tuple[int, int, int],
    threshold: int,
) -> Image.Image:
    rgba = image.convert("RGBA")
    rgb = rgba.convert("RGB")
    width, height = rgba.size
    pixels = rgb.load()
    background_mask = bytearray(width * height)
    queue: deque[tuple[int, int]] = deque()

    def enqueue(x: int, y: int) -> None:
        index = y * width + x
        if background_mask[index] or color_distance(pixels[x, y], background) > threshold:
            return
        background_mask[index] = 1
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

    data = bytearray(rgba.tobytes())
    for index, is_background in enumerate(background_mask):
        if is_background:
            offset = index * 4
            data[offset : offset + 4] = b"\x00\x00\x00\x00"
    return Image.frombytes("RGBA", rgba.size, bytes(data))


def normalize_frame(image: Image.Image, padding: int) -> Image.Image:
    bbox = image.getbbox()
    if bbox is None:
        return Image.new("RGBA", CELL_SIZE, (0, 0, 0, 0))
    content = image.crop(bbox)
    max_width = CELL_SIZE[0] - padding * 2
    max_height = CELL_SIZE[1] - padding * 2
    content.thumbnail((max_width, max_height), Image.Resampling.LANCZOS)
    frame = Image.new("RGBA", CELL_SIZE, (0, 0, 0, 0))
    left = (CELL_SIZE[0] - content.width) // 2
    top = CELL_SIZE[1] - padding - content.height
    frame.alpha_composite(content, (left, top))
    return frame


def component_centers(image: Image.Image, minimum_area: int) -> list[int]:
    alpha = image.getchannel("A")
    width, height = alpha.size
    foreground = bytearray(1 if value else 0 for value in alpha.tobytes())
    visited = bytearray(width * height)
    centers: list[int] = []
    for start, value in enumerate(foreground):
        if not value or visited[start]:
            continue
        visited[start] = 1
        stack = [start]
        area = 0
        minimum_x = width
        maximum_x = 0
        while stack:
            current = stack.pop()
            area += 1
            x = current % width
            minimum_x = min(minimum_x, x)
            maximum_x = max(maximum_x, x)
            for neighbor in (current - 1, current + 1, current - width, current + width):
                if neighbor < 0 or neighbor >= width * height or visited[neighbor]:
                    continue
                if abs(neighbor % width - x) > 1 or not foreground[neighbor]:
                    continue
                visited[neighbor] = 1
                stack.append(neighbor)
        if area >= minimum_area:
            centers.append((minimum_x + maximum_x + 1) // 2)
    return sorted(centers)


def extract(
    source_path: Path,
    output_root: Path,
    row_bands: list[tuple[int, int]],
    source_rows: list[tuple[str, int]],
    threshold: int,
    padding: int,
    mirror_running_left: bool,
    stable_slot_states: set[str],
) -> dict[str, object]:
    with Image.open(source_path) as opened:
        source = opened.convert("RGBA")
    if len(row_bands) != len(source_rows):
        raise SystemExit(f"expected {len(source_rows)} row bands, got {len(row_bands)}")

    background = estimate_background(source)
    manifest_rows: list[dict[str, object]] = []
    for (state, frame_count), (top, bottom) in zip(source_rows, row_bands):
        state_dir = output_root / state
        state_dir.mkdir(parents=True, exist_ok=True)
        row = source.crop((0, top, source.width, bottom))
        transparent_row = remove_border_background(row, background, threshold)
        centers = component_centers(transparent_row, minimum_area=3000)
        method = "components"
        if len(centers) != frame_count and state in stable_slot_states:
            boundaries = [round(source.width * index / frame_count) for index in range(frame_count + 1)]
            method = "stable-slots"
        elif len(centers) != frame_count:
            raise SystemExit(
                f"{state} needs {frame_count} subject components, detected {len(centers)} at {centers}"
            )
        else:
            boundaries = [0]
            boundaries.extend((left + right) // 2 for left, right in zip(centers, centers[1:]))
            boundaries.append(source.width)
        for index in range(frame_count):
            crop = transparent_row.crop((boundaries[index], 0, boundaries[index + 1], bottom - top))
            normalize_frame(crop, padding).save(state_dir / f"{index:02d}.png")
        manifest_rows.append(
            {
                "state": state,
                "frame_count": frame_count,
                "method": method,
                "source_band": [0, top, source.width, bottom],
                "subject_centers": centers,
            }
        )

    if mirror_running_left:
        right_dir = output_root / "running-right"
        left_dir = output_root / "running-left"
        left_dir.mkdir(parents=True, exist_ok=True)
        for index in range(8):
            with Image.open(right_dir / f"{index:02d}.png") as opened:
                mirrored = opened.convert("RGBA").transpose(Image.Transpose.FLIP_LEFT_RIGHT)
            mirrored.save(left_dir / f"{index:02d}.png")
        manifest_rows.insert(
            2,
            {
                "state": "running-left",
                "frame_count": 8,
                "method": "mirrored-running-right",
                "windows_design_decision": True,
            },
        )

    manifest = {
        "source": str(source_path),
        "cell_size": list(CELL_SIZE),
        "background": list(background),
        "background_threshold": threshold,
        "rows": manifest_rows,
    }
    (output_root / "frames-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )
    return manifest


def parse_row_bands(value: str) -> list[tuple[int, int]]:
    bands = []
    for item in value.split(","):
        top, bottom = item.split(":", 1)
        bands.append((int(top), int(bottom)))
    return bands


def parse_source_rows(value: str) -> list[tuple[str, int]]:
    rows = []
    for item in value.split(","):
        state, frame_count = item.rsplit(":", 1)
        rows.append((state, int(frame_count)))
    return rows


def parse_state_list(value: str) -> set[str]:
    return {item.strip() for item in value.split(",") if item.strip()}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source")
    parser.add_argument("--output-root", required=True)
    parser.add_argument("--row-bands", required=True, type=parse_row_bands)
    parser.add_argument(
        "--source-rows",
        type=parse_source_rows,
        default=DEFAULT_SOURCE_ROWS,
        help="Comma-separated state:frame-count rows in top-to-bottom order.",
    )
    parser.add_argument(
        "--mirror-running-left",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="Generate running-left by mirroring running-right after extraction.",
    )
    parser.add_argument(
        "--stable-slot-states",
        type=parse_state_list,
        default=set(),
        help="Use equal-width source slots for these rows when component detection is ambiguous.",
    )
    parser.add_argument("--background-threshold", type=int, default=28)
    parser.add_argument("--padding", type=int, default=10)
    args = parser.parse_args()

    manifest = extract(
        Path(args.source).resolve(),
        Path(args.output_root).resolve(),
        args.row_bands,
        args.source_rows,
        args.background_threshold,
        args.padding,
        args.mirror_running_left,
        args.stable_slot_states,
    )
    print(json.dumps(manifest, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()