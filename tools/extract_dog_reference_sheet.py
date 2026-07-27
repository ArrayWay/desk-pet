#!/usr/bin/env python3
"""Extract normalized transparent frames from a 2x3 dog action reference sheet."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageChops

CELL_SIZE = (192, 208)
CHROMA_GREEN = (0, 255, 0)


def color_distance(left: tuple[int, int, int], right: tuple[int, int, int]) -> int:
    return max(abs(left[index] - right[index]) for index in range(3))


def remove_chroma_background(image: Image.Image, threshold: int) -> Image.Image:
    """Remove chroma-green pixels with Pillow-native operations."""
    rgba = image.convert("RGBA")
    rgb = rgba.convert("RGB")
    chroma = Image.new("RGB", rgb.size, CHROMA_GREEN)
    red, green, blue = ImageChops.difference(rgb, chroma).split()
    distance = ImageChops.lighter(ImageChops.lighter(red, green), blue)
    alpha = distance.point(lambda value: 0 if value <= threshold else 255)
    rgba.putalpha(ImageChops.darker(rgba.getchannel("A"), alpha))
    return rgba


def keep_largest_component(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A")
    width, height = image.size
    visited = bytearray(width * height)
    pixels = alpha.load()
    largest: list[tuple[int, int]] = []

    for start_y in range(height):
        for start_x in range(width):
            offset = start_y * width + start_x
            if visited[offset] or pixels[start_x, start_y] == 0:
                continue

            component: list[tuple[int, int]] = []
            stack = [(start_x, start_y)]
            visited[offset] = 1
            while stack:
                x, y = stack.pop()
                component.append((x, y))
                for next_x, next_y in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                    if next_x < 0 or next_y < 0 or next_x >= width or next_y >= height:
                        continue
                    next_offset = next_y * width + next_x
                    if visited[next_offset] or pixels[next_x, next_y] == 0:
                        continue
                    visited[next_offset] = 1
                    stack.append((next_x, next_y))

            if len(component) > len(largest):
                largest = component

    if not largest:
        return image

    mask = Image.new("L", image.size, 0)
    mask_pixels = mask.load()
    for x, y in largest:
        mask_pixels[x, y] = pixels[x, y]

    cleaned = image.copy()
    cleaned.putalpha(mask)
    return cleaned


def split_row_components(image: Image.Image, expected_count: int) -> list[Image.Image]:
    alpha = image.getchannel("A")
    width, height = image.size
    visited = bytearray(width * height)
    pixels = alpha.load()
    components: list[tuple[int, int, int, int, int]] = []

    for start_y in range(height):
        for start_x in range(width):
            offset = start_y * width + start_x
            if visited[offset] or pixels[start_x, start_y] == 0:
                continue

            stack = [(start_x, start_y)]
            visited[offset] = 1
            area = 0
            left = right = start_x
            top = bottom = start_y
            while stack:
                x, y = stack.pop()
                area += 1
                left = min(left, x)
                right = max(right, x)
                top = min(top, y)
                bottom = max(bottom, y)
                for next_x, next_y in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                    if next_x < 0 or next_y < 0 or next_x >= width or next_y >= height:
                        continue
                    next_offset = next_y * width + next_x
                    if visited[next_offset] or pixels[next_x, next_y] == 0:
                        continue
                    visited[next_offset] = 1
                    stack.append((next_x, next_y))
            components.append((area, left, top, right + 1, bottom + 1))

    subjects = sorted(components, reverse=True)[:expected_count]
    if len(subjects) != expected_count:
        raise ValueError(f"expected {expected_count} row subjects, found {len(subjects)}")
    subjects.sort(key=lambda component: component[1] + component[3])
    return [image.crop(component[1:]) for component in subjects]


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


def extract(
    source_path: Path,
    output_dir: Path,
    threshold: int,
    padding: int,
    columns: int = 3,
    rows: int = 2,
    frame_count: int | None = None,
    row_components: bool = False,
    keep_all_components: bool = False,
    slot_inset: int = 0,
) -> dict[str, object]:
    with Image.open(source_path) as opened:
        source = opened.convert("RGBA")

    output_dir.mkdir(parents=True, exist_ok=True)
    frame_count = frame_count or columns * rows
    if frame_count > columns * rows:
        raise ValueError("frame count exceeds the configured grid capacity")
    slot_width = source.width // columns
    slot_height = source.height // rows
    frames: list[dict[str, object]] = []
    row_subjects: list[list[Image.Image]] = []
    if row_components:
        for row in range(rows):
            top = row * slot_height
            bottom = source.height if row == rows - 1 else (row + 1) * slot_height
            transparent_row = remove_chroma_background(source.crop((0, top, source.width, bottom)), threshold)
            row_subjects.append(split_row_components(transparent_row, columns))

    for index in range(frame_count):
        column = index % columns
        row = index // columns
        left = column * slot_width
        top = row * slot_height
        right = source.width if column == columns - 1 else (column + 1) * slot_width
        bottom = source.height if row == rows - 1 else (row + 1) * slot_height
        if row_components:
            primary = row_subjects[row][column]
        else:
            slot = source.crop((left, top, right, bottom))
            if slot_inset > 0:
                if slot_inset * 2 >= slot.width or slot_inset * 2 >= slot.height:
                    raise ValueError("slot inset leaves no extractable content")
                slot = slot.crop((slot_inset, slot_inset, slot.width - slot_inset, slot.height - slot_inset))
            transparent = remove_chroma_background(slot, threshold)
            primary = transparent if keep_all_components else keep_largest_component(transparent)
        normalized = normalize_frame(primary, padding)
        output_path = output_dir / f"{index:02d}.png"
        normalized.save(output_path)
        alpha = normalized.getchannel("A")
        frames.append(
            {
                "index": index,
                "source_slot": [left, top, right, bottom],
                "content_bounds": list(normalized.getbbox() or (0, 0, 0, 0)),
                "opaque_pixels": sum(alpha.histogram()[1:]),
                "path": str(output_path),
            }
        )

    manifest = {
        "source": str(source_path),
        "cell_size": list(CELL_SIZE),
        "chroma_key": list(CHROMA_GREEN),
        "threshold": threshold,
        "padding": padding,
        "grid": [columns, rows],
        "extraction": "row-components" if row_components else "grid-slots-all-components" if keep_all_components else "grid-slots",
        "slot_inset": slot_inset,
        "frames": frames,
    }
    (output_dir / "frames-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source")
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--threshold", type=int, default=76)
    parser.add_argument("--padding", type=int, default=10)
    parser.add_argument("--columns", type=int, default=3)
    parser.add_argument("--rows", type=int, default=2)
    parser.add_argument("--frame-count", type=int)
    parser.add_argument("--row-components", action="store_true")
    parser.add_argument("--keep-all-components", action="store_true")
    parser.add_argument("--slot-inset", type=int, default=0)
    args = parser.parse_args()
    print(
        json.dumps(
            extract(
                Path(args.source).resolve(),
                Path(args.output_dir).resolve(),
                args.threshold,
                args.padding,
                args.columns,
                args.rows,
                args.frame_count,
                args.row_components,
                args.keep_all_components,
                args.slot_inset,
            ),
            ensure_ascii=True,
            indent=2,
        )
    )


if __name__ == "__main__":
    main()