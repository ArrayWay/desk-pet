from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "shared" / "companions" / "training-dog" / "food-bowl.png"
SCALE = 4


def scaled_box(box: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
    return tuple(value * SCALE for value in box)


def main() -> None:
    canvas = Image.new("RGBA", (192 * SCALE, 144 * SCALE), (0, 0, 0, 0))
    shadow = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow)
    shadow_draw.ellipse(scaled_box((24, 112, 168, 136)), fill=(45, 25, 20, 75))
    shadow = shadow.filter(ImageFilter.GaussianBlur(5 * SCALE))
    canvas.alpha_composite(shadow)

    draw = ImageDraw.Draw(canvas)
    draw.ellipse(scaled_box((20, 20, 172, 88)), fill=(126, 45, 38, 255))
    draw.ellipse(scaled_box((27, 25, 165, 80)), fill=(220, 105, 88, 255))
    draw.ellipse(scaled_box((36, 31, 156, 74)), fill=(63, 35, 27, 255))
    draw.ellipse(scaled_box((42, 35, 150, 70)), fill=(105, 66, 41, 255))

    kibble = (
        (55, 44, 68, 53, (132, 82, 47, 255)),
        (70, 38, 84, 49, (156, 99, 57, 255)),
        (87, 44, 101, 54, (123, 73, 42, 255)),
        (105, 38, 119, 49, (169, 108, 62, 255)),
        (122, 45, 137, 55, (128, 76, 43, 255)),
        (63, 55, 78, 64, (167, 105, 59, 255)),
        (82, 54, 96, 66, (116, 68, 40, 255)),
        (101, 56, 116, 66, (151, 91, 50, 255)),
        (119, 55, 132, 64, (178, 113, 64, 255)),
    )
    for left, top, right, bottom, color in kibble:
        draw.ellipse(scaled_box((left, top, right, bottom)), fill=color)

    draw.polygon(
        [(24 * SCALE, 58 * SCALE), (168 * SCALE, 58 * SCALE), (153 * SCALE, 120 * SCALE), (39 * SCALE, 120 * SCALE)],
        fill=(177, 66, 56, 255),
    )
    draw.ellipse(scaled_box((39, 100, 153, 132)), fill=(118, 41, 35, 255))
    draw.ellipse(scaled_box((43, 99, 149, 123)), fill=(189, 70, 59, 255))
    draw.ellipse(scaled_box((31, 52, 161, 83)), outline=(235, 126, 106, 255), width=4 * SCALE)
    draw.arc(scaled_box((45, 65, 147, 121)), 8, 172, fill=(218, 91, 74, 255), width=3 * SCALE)

    result = canvas.resize((192, 144), Image.Resampling.LANCZOS)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    result.save(OUTPUT, optimize=True)


if __name__ == "__main__":
    main()