#!/usr/bin/env python3
"""Validate custom companion frame directories and create a QA contact sheet."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

CELL_SIZE = (192, 208)
LABEL_HEIGHT = 24


def parse_state(value: str) -> tuple[str, int]:
    name, count = value.rsplit(":", 1)
    return name, int(count)


def checker(size: tuple[int, int], square: int = 12) -> Image.Image:
    image = Image.new("RGB", size, "#ffffff")
    draw = ImageDraw.Draw(image)
    for top in range(0, size[1], square):
        for left in range(0, size[0], square):
            if (left // square + top // square) % 2:
                draw.rectangle((left, top, left + square - 1, top + square - 1), fill="#e8e8e8")
    return image


def validate_state(root: Path, name: str, expected_count: int) -> dict[str, object]:
    files = sorted((root / name).glob("*.png"))
    errors: list[str] = []
    frames: list[dict[str, object]] = []
    if len(files) != expected_count:
        errors.append(f"{name}: expected {expected_count} PNG frames, found {len(files)}")

    for index, path in enumerate(files):
        with Image.open(path) as opened:
            image = opened.convert("RGBA")
        alpha = image.getchannel("A")
        opaque_pixels = sum(alpha.histogram()[1:])
        bounds = image.getbbox()
        edge_pixels = sum(
            sum(crop.histogram()[1:])
            for crop in (
                alpha.crop((0, 0, image.width, 2)),
                alpha.crop((0, image.height - 2, image.width, image.height)),
                alpha.crop((0, 0, 2, image.height)),
                alpha.crop((image.width - 2, 0, image.width, image.height)),
            )
        )
        if image.size != CELL_SIZE:
            errors.append(f"{name}/{path.name}: size is {image.size}, expected {CELL_SIZE}")
        if opaque_pixels < 400:
            errors.append(f"{name}/{path.name}: frame is empty or too sparse")
        if edge_pixels:
            errors.append(f"{name}/{path.name}: {edge_pixels} opaque pixels touch the safe edge")
        frames.append(
            {
                "index": index,
                "file": str(path),
                "bounds": list(bounds) if bounds else None,
                "opaque_pixels": opaque_pixels,
                "edge_pixels": edge_pixels,
            }
        )

    return {
        "state": name,
        "expected_frames": expected_count,
        "actual_frames": len(files),
        "ok": not errors,
        "errors": errors,
        "frames": frames,
    }


def create_contact_sheet(root: Path, states: list[tuple[str, int]], output: Path) -> None:
    scale = 0.55
    cell_width = round(CELL_SIZE[0] * scale)
    cell_height = round(CELL_SIZE[1] * scale)
    columns = max(count for _, count in states)
    width = columns * cell_width
    height = len(states) * (cell_height + LABEL_HEIGHT)
    sheet = Image.new("RGB", (width, height), "#f7f7f7")
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default()

    for row, (name, _) in enumerate(states):
        top = row * (cell_height + LABEL_HEIGHT)
        draw.rectangle((0, top, width, top + LABEL_HEIGHT - 1), fill="#171717")
        draw.text((6, top + 6), name, fill="#ffffff", font=font)
        for column, path in enumerate(sorted((root / name).glob("*.png"))):
            with Image.open(path) as opened:
                frame = opened.convert("RGBA").resize((cell_width, cell_height), Image.Resampling.LANCZOS)
            background = checker((cell_width, cell_height))
            background.paste(frame, (0, 0), frame)
            left = column * cell_width
            sheet.paste(background, (left, top + LABEL_HEIGHT))
            draw.rectangle(
                (left, top + LABEL_HEIGHT, left + cell_width - 1, top + LABEL_HEIGHT + cell_height - 1),
                outline="#18864b",
            )
            draw.text((left + 4, top + LABEL_HEIGHT + 4), str(column), fill="#111111", font=font)

    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--frames-root", required=True)
    parser.add_argument("--state", action="append", type=parse_state, required=True)
    parser.add_argument("--json-out", required=True)
    parser.add_argument("--contact-sheet", required=True)
    args = parser.parse_args()

    root = Path(args.frames_root).resolve()
    results = [validate_state(root, name, count) for name, count in args.state]
    report = {
        "ok": all(result["ok"] for result in results),
        "frames_root": str(root),
        "cell_size": list(CELL_SIZE),
        "states": results,
    }
    json_out = Path(args.json_out).resolve()
    json_out.parent.mkdir(parents=True, exist_ok=True)
    json_out.write_text(json.dumps(report, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
    create_contact_sheet(root, args.state, Path(args.contact_sheet).resolve())
    print(json.dumps({"ok": report["ok"], "states": len(results)}, ensure_ascii=True))
    if not report["ok"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()