#!/usr/bin/env python3
"""Prepare a GG1MapChooser Panorama source archive from a server's maps tree."""

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import sys
from tempfile import TemporaryDirectory
from zipfile import ZIP_DEFLATED, ZipFile

from PIL import Image, ImageOps
from preview_vpk import MAX_INDEX_BYTES, parse_index


IMAGE_EXTENSIONS = (".png", ".jpg", ".jpeg")
MAP_EXTENSIONS = (".vpk", ".bsp")
STYLE = Path("panorama/styles/custom_game/ggmc_map_vote.css")
TEXTURES = Path("panorama/images/mapPreviews")
PREVIEW_CONFIG = "GG1MapPreviews.json"
MAP_KEY = re.compile(r"[A-Za-z0-9_-]+\Z")
DEFAULT_PREVIEW = "defatul"
MANUAL_PREVIEWS = "manualy"
VTEX = '''<!-- dmx encoding keyvalues2_noids 4 format vtex 1 -->
"CDmeVtex"
{
    "m_inputTextureArray" "element_array"
    [
        "CDmeInputTexture"
        {
            "m_name" "string" "Source"
            "m_fileName" "string" "%s"
            "m_colorSpace" "string" "srgb"
            "m_typeString" "string" "2D"
        }
    ]
    "m_outputTypeString" "string" "2D"
    "m_outputFormat" "string" "RGBA8888"
    "m_textureOutputChannelArray" "element_array"
    [
        "CDmeTextureOutputChannel"
        {
            "m_inputTextureArray" "string_array" [ "Source" ]
            "m_srcChannels" "string" "rgba"
            "m_dstChannels" "string" "rgba"
            "m_outputColorSpace" "string" "srgb"
        }
    ]
}
'''


def find_maps(root):
    for directory in sorted({p.parent for p in root.rglob("*") if p.is_file() and p.suffix.lower() in MAP_EXTENSIONS}):
        files = sorted(p for p in directory.iterdir() if p.is_file() and not p.is_symlink())
        workshop_id = next((part for part in reversed((root.name, *directory.relative_to(root).parts))
                            if re.fullmatch(r"[0-9]+", part)), "")
        images = {p.name.lower(): p for p in files if p.suffix.lower() in IMAGE_EXTENSIONS}
        maps = {}
        for package in files:
            if package.suffix.lower() not in MAP_EXTENSIONS or re.search(r"_\d{3}\.vpk$", package.name, re.I):
                continue
            stem = package.stem.removesuffix("_dir")
            keys = [stem] if MAP_KEY.fullmatch(stem) and not stem.isdecimal() else []
            if workshop_id and package.suffix.lower() == ".vpk":
                with package.open("rb") as file:
                    entries = parse_index(file.read(MAX_INDEX_BYTES + 28))
                internal_keys = [entry.stem for entry in entries if entry.parent == PurePosixPath("maps")
                                 and entry.suffix.lower() in {".vpk", ".vmap_c"} and MAP_KEY.fullmatch(entry.stem)]
                keys = internal_keys or keys
            for key in keys:
                maps.setdefault(key, Path(key + package.suffix))
        for path in maps.values():
            names = [path.stem, f"{path.stem}_thumb", f"{path.stem}_preview"]
            if len(maps) == 1 and workshop_id:
                names += [f"thumb{workshop_id}", f"thumb{workshop_id}_s"]
            image = next((images[name.lower() + ext] for name in names for ext in IMAGE_EXTENSIONS
                          if name.lower() + ext in images), None)
            yield path.stem, image


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_texture(source, texture, image):
    png = texture.with_suffix(".png")
    (source / png).parent.mkdir(parents=True, exist_ok=True)
    image.save(source / png)
    (source / texture).write_text(VTEX % png.as_posix(), encoding="utf-8")
    return f"s2r://{texture.as_posix()}"


def convert_image(image, source, texture):
    try:
        with Image.open(image) as photo:
            thumbnail = ImageOps.fit(ImageOps.exif_transpose(photo).convert("RGB"), (320, 180))
    except (OSError, Image.DecompressionBombError) as error:
        print(f"Warning: {image}: {error}", file=sys.stderr)
        return None
    return write_texture(source, texture, thumbnail)


def stage_preview(directory, source, texture, extensions=IMAGE_EXTENSIONS):
    name = texture.stem
    vtex = directory / texture.name
    if vtex.is_file():
        target = source / texture
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(vtex, target)
        for ext in extensions:
            image = directory / (name + ext)
            if image.is_file():
                shutil.copy2(image, target.with_suffix(ext))
        return f"s2r://{texture.as_posix()}"

    for ext in extensions:
        image = directory / (name + ext)
        if image.is_file() and (uri := convert_image(image, source, texture)):
            return uri
    return None


def pack(maps, output, output_dir=None, preview_root=None, fetch_image=None):
    tool_dir = Path(__file__).resolve().parent
    template = tool_dir.parent / "Panorama"
    preview_root = preview_root if preview_root is not None else template / TEXTURES
    if not preview_root.is_dir():
        raise ValueError(f"Preview directory not found: {preview_root}")
    if output is None and output_dir is None:
        raise ValueError("Specify an output directory or ZIP path")
    with TemporaryDirectory(prefix="ggmc-panorama-") as temp:
        staging = Path(temp)
        source = staging / "Panorama"
        shutil.copytree(template / "panorama", source / "panorama", ignore=shutil.ignore_patterns(TEXTURES.name))
        (source / TEXTURES / MANUAL_PREVIEWS).mkdir(parents=True)
        previews = {}
        seen_names = set()
        default_texture = TEXTURES / f"{DEFAULT_PREVIEW}.vtex"
        fallback = stage_preview(preview_root, source, default_texture, (".png",))
        if fallback is None:
            fallback = stage_preview(template / TEXTURES, source, default_texture, (".png",))
        if fallback is None:
            raise ValueError(f"Bundled fallback texture missing: {default_texture}")
        for key, image in maps:
            if not MAP_KEY.fullmatch(key) or key.lower() == DEFAULT_PREVIEW:
                raise ValueError(f"Invalid or reserved map filename {key!r}")
            if key.lower() in seen_names:
                raise ValueError(f"Multiple map packages share the filename {key}")
            seen_names.add(key.lower())
            texture = TEXTURES / f"{key}.vtex"
            uri = (
                stage_preview(preview_root / MANUAL_PREVIEWS, source, texture.parent / MANUAL_PREVIEWS / texture.name)
                or stage_preview(preview_root, source, texture)
            )
            if uri is None:
                if image is None and fetch_image is not None:
                    image = fetch_image(key)
                if image is not None:
                    uri = convert_image(image, source, texture)
            previews[key] = uri or fallback

        rules = []
        for uri in sorted(set(previews.values()) - {fallback}):
            digest = hashlib.sha256(uri.encode()).hexdigest()
            rules.append(f'.vote-preview.preview-{digest} {{ background-image: url("{uri}"); }}')

        with (source / STYLE).open("a", encoding="utf-8") as css:
            css.write("\n" + "\n".join(rules) + "\n")
        write_json(staging / PREVIEW_CONFIG, previews)
        if output_dir is not None:
            shutil.copytree(staging, output_dir)
        if output is not None:
            with ZipFile(output, "x", compression=ZIP_DEFLATED) as archive:
                for file in sorted(staging.rglob("*")):
                    archive.write(file, file.relative_to(staging).as_posix())
    print(f"{output_dir or output}: {len(previews)} map preview entries")
    return previews


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("maps", type=Path, help="server maps or Workshop directory")
    parser.add_argument("--output", "-o", type=Path, help="new ZIP archive path")
    parser.add_argument("--output-dir", type=Path, help="new directory with addon sources and GG1MapPreviews.json")
    parser.add_argument("--preview-root", type=Path, help="addon mapPreviews directory with optional manualy overrides")
    args = parser.parse_args()
    if not args.maps.is_dir():
        parser.error(f"maps directory not found: {args.maps}")
    try:
        maps = list(find_maps(args.maps.resolve()))
        if not maps:
            raise ValueError("No map packages found")
        pack(maps, args.output, output_dir=args.output_dir, preview_root=args.preview_root)
    except (OSError, ValueError) as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
