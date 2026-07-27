#!/usr/bin/env python3
"""Composite a food bowl beneath dog animation frames."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image


def composite_frames(
    frames_dir: Path,
    bowl_path: Path,
    output_dir: Path,
    bowl_width: int,
    right: int,
    bottom: int,
) -> None:
    with Image.open(bowl_path) as opened:
        bowl = opened.convert("RGBA")
    bounds = bowl.getbbox()
    if bounds is None:
        raise ValueError(f"food bowl is empty: {bowl_path}")

    bowl = bowl.crop(bounds)
    bowl_height = round(bowl.height * bowl_width / bowl.width)
    bowl = bowl.resize((bowl_width, bowl_height), Image.Resampling.LANCZOS)
    output_dir.mkdir(parents=True, exist_ok=True)

    frame_paths = sorted(frames_dir.glob("*.png"))
    if not frame_paths:
        raise ValueError(f"no PNG frames found: {frames_dir}")

    for frame_path in frame_paths:
        with Image.open(frame_path) as opened:
            dog = opened.convert("RGBA")
        left = dog.width - right - bowl.width
        top = dog.height - bottom - bowl.height
        if left < 0 or top < 0:
            raise ValueError("food bowl does not fit inside the frame")

        frame = Image.new("RGBA", dog.size, (0, 0, 0, 0))
        frame.alpha_composite(bowl, (left, top))
        frame.alpha_composite(dog)
        frame.save(output_dir / frame_path.name)

    manifest = {
        "source_frames": str(frames_dir),
        "food_bowl": str(bowl_path),
        "frame_count": len(frame_paths),
        "bowl_width": bowl_width,
        "right": right,
        "bottom": bottom,
        "layer_order": ["food-bowl", "dog"],
    }
    (output_dir / "composite-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--frames-dir", required=True)
    parser.add_argument("--bowl", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--bowl-width", type=int, default=64)
    parser.add_argument("--right", type=int, default=8)
    parser.add_argument("--bottom", type=int, default=10)
    args = parser.parse_args()

    composite_frames(
        Path(args.frames_dir).resolve(),
        Path(args.bowl).resolve(),
        Path(args.output_dir).resolve(),
        args.bowl_width,
        args.right,
        args.bottom,
    )


if __name__ == "__main__":
    main()