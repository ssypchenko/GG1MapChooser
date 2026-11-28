#!/usr/bin/env python3
"""Read a CS2 server mapcycle over FTP and resolve its Steam Workshop preview URLs."""

import argparse
from ftplib import FTP, FTP_TLS, error_perm, all_errors
from getpass import getpass
from io import BytesIO
import json
import os
from pathlib import Path, PurePosixPath
import re
import runpy
import ssl
import sys
from tempfile import NamedTemporaryFile, TemporaryDirectory
from urllib.parse import urlencode, urlsplit
from urllib.request import Request, urlopen

from preview_vpk import MAX_INDEX_BYTES, parse_index


STEAM_DETAILS = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/"
PLUGIN_CONFIG = "addons/counterstrikesharp/configs/plugins/GG1MapChooser/GG1MapChooser.json"
MAX_IMAGE_BYTES = 16 * 1024 * 1024
MAP_KEY = re.compile(r"[A-Za-z0-9_-]+\Z")


def read_json(ftp, path, optional=False):
    data = BytesIO()
    try:
        ftp.retrbinary(f"RETR {path}", data.write)
    except error_perm as error:
        if optional and str(error).startswith("550"):
            print(f"Warning: cannot read optional file {path}", file=sys.stderr)
            return {}
        raise
    result = json.loads(data.getvalue().decode("utf-8-sig"))
    if not isinstance(result, dict):
        raise ValueError(f"Expected a JSON object in {path}")
    return result


def map_rows(entries, pool=None):
    for key, info in entries.items():
        if not isinstance(info, dict) or not MAP_KEY.fullmatch(key):
            continue
        workshop_id = str(info.get("mapid") or "").strip()
        yield {
            "map": key, "pool": pool, "name": info.get("display") or key,
            "workshop": info.get("ws") is True or bool(workshop_id),
            "configured": True,
            "workshop_id": workshop_id if re.fullmatch(r"[1-9][0-9]{0,19}", workshop_id) else "",
        }


def configured_maps(mapcycle, low_player_maps):
    for key, info in mapcycle.items():
        if not isinstance(info, dict):
            continue
        if any(isinstance(value, dict) for value in info.values()) and not {"ws", "display", "mapid"}.intersection(info):
            yield from map_rows(info, key)
        else:
            yield from map_rows({key: info})
    yield from map_rows(low_player_maps, "LowPlayerMaps")


def list_directory(ftp, root):
    try:
        return sorted({PurePosixPath(name.rstrip("/")).name for name in ftp.nlst(str(root))})
    except error_perm as error:
        if str(error).startswith("550"):
            print(f"Warning: cannot list FTP directory {root}", file=sys.stderr)
            return []
        raise


def vpk_index(ftp, path):
    # Inventory metadata only. Large map packages stay on the server.
    try:
        ftp.voidcmd("TYPE I")
        size = ftp.size(str(path))
        if size is None or size > MAX_INDEX_BYTES:
            return []
        data = BytesIO()
        ftp.retrbinary(f"RETR {path}", data.write)
        return parse_index(data.getvalue())
    except error_perm:
        return []


def inventory_packages(ftp, maps_root, workshop_roots):
    def inspect(directory, workshop_id, names):
        images = [name for name in names if PurePosixPath(name).suffix.lower() in {".jpg", ".jpeg", ".png"}]
        chunks = re.compile(r"_\d{3}\.vpk$", re.IGNORECASE)
        for name in names:
            path = PurePosixPath(name)
            if path.suffix.lower() not in {".vpk", ".bsp"} or chunks.search(name):
                continue
            stem = path.stem.removesuffix("_dir")
            keys = [stem] if MAP_KEY.fullmatch(stem) and not stem.isdecimal() else []
            entries = vpk_index(ftp, directory / name) if workshop_id and path.suffix.lower() == ".vpk" else []
            internal_keys = sorted({entry.stem for entry in entries if entry.parent == PurePosixPath("maps")
                                    and entry.suffix.lower() in {".vpk", ".vmap_c"} and MAP_KEY.fullmatch(entry.stem)})
            for key in internal_keys or keys or [None]:
                matching_images = {key.lower(), f"{key.lower()}_thumb", f"{key.lower()}_preview"} if key else set()
                if workshop_id and len(internal_keys or keys) <= 1:
                    matching_images.update({f"thumb{workshop_id}", f"thumb{workshop_id}_s"})
                resources = [entry.as_posix() for entry in entries if entry.as_posix().startswith("panorama/images/")
                             and entry.suffix == ".vtex_c" and (entry.stem.lower() in matching_images or
                             (key and entry.stem.lower() in {f"{key.lower()}_png", f"{key.lower()}_jpg", f"{key.lower()}_preview_png"}))]
                yield {
                    "map": key, "workshop_id": workshop_id, "server_package": str(directory / name),
                    "server_preview_files": [str(directory / image) for image in images if PurePosixPath(image).stem.lower() in matching_images],
                    "embedded_preview_paths": ["s2r://" + resource.removesuffix("_c") for resource in resources],
                }

    yield from inspect(maps_root, "", list_directory(ftp, maps_root))
    for workshop_root in workshop_roots:
        for folder in list_directory(ftp, workshop_root):
            if re.fullmatch(r"[1-9][0-9]{0,19}", folder):
                directory = workshop_root / folder
                yield from inspect(directory, folder, list_directory(ftp, directory))


def merge_inventory(maps, packages, screenshot_config, stock_previews=None):
    stock_previews = stock_previews or {}
    ids = {}
    for package in packages:
        key, workshop_id = package["map"], package["workshop_id"]
        if key and workshop_id:
            ids.setdefault(key, set()).add(workshop_id)
    for row in maps:
        candidates = ids.get(row["map"], set())
        if row["workshop"] and not row["workshop_id"] and len(candidates) == 1:
            row["workshop_id"] = next(iter(candidates))
    matched = set()
    for row in maps:
        files = [package for package in packages if package["workshop_id"] == row["workshop_id"] and
                 (package["map"] == row["map"] or (package["map"] is None and row["workshop_id"]))]
        matched.update((package["server_package"], package["map"]) for package in files)
        attach_preview_paths(row, files, screenshot_config, stock_previews)
    unconfigured = {}
    for package in packages:
        if (package["server_package"], package["map"]) in matched:
            continue
        key, workshop_id = package["map"], package["workshop_id"]
        unconfigured.setdefault((key, workshop_id), []).append(package)
    for (key, workshop_id), files in unconfigured.items():
        row = {"map": key, "pool": None, "name": key or workshop_id, "configured": False,
               "workshop": bool(workshop_id), "workshop_id": workshop_id}
        if key is None:
            row["status"] = "map_key_unknown"
        attach_preview_paths(row, files, screenshot_config, stock_previews)
        maps.append(row)
    return ids


def attach_preview_paths(row, packages, screenshot_config, stock_previews):
    row["server_packages"] = sorted({package["server_package"] for package in packages})
    for field in ("server_preview_files", "embedded_preview_paths"):
        row[field] = sorted({value for package in packages for value in package[field]})
    source = screenshot_config.get(row["map"])
    if isinstance(source, str) and source.startswith("s2r://panorama/"):
        row["client_preview_path"] = source
        row["client_preview_source"] = "configured_resource"
    elif row["embedded_preview_paths"]:
        row["client_preview_path"] = row["embedded_preview_paths"][0]
        row["client_preview_source"] = "mounted_workshop_package"
    elif not row["workshop"] and row["map"] in stock_previews:
        row["client_preview_path"] = stock_previews[row["map"]]
        row["client_preview_source"] = "stock_resource"
    elif https_url(source):
        row["client_preview_path"] = source
        row["client_preview_source"] = "configured_https"
    else:
        row["client_preview_path"] = None
        row["client_preview_source"] = None


def steam_details(ids):
    details = {}
    for start in range(0, len(ids), 50):
        batch = ids[start:start + 50]
        form = {f"publishedfileids[{i}]": value for i, value in enumerate(batch)}
        form["itemcount"] = len(batch)
        request = Request(STEAM_DETAILS, data=urlencode(form).encode("ascii"))
        with urlopen(request, timeout=30) as response:
            items = json.load(response)["response"]["publishedfiledetails"]
        details.update((str(item["publishedfileid"]), item) for item in items)
    return details


def https_url(value):
    if not isinstance(value, str):
        return None
    try:
        url = urlsplit(value)
    except ValueError:
        return None
    return value if url.scheme == "https" and url.hostname else None


def resolve_previews(maps, installed):
    configured_ids = {}
    for row in maps:
        if row["workshop_id"]:
            configured_ids.setdefault(row["map"], set()).add(row["workshop_id"])
    for row in maps:
        if row["workshop"] and not row["workshop_id"]:
            candidates = configured_ids.get(row["map"]) or installed.get(row["map"], set())
            if len(candidates) == 1:
                row["workshop_id"] = next(iter(candidates))
            elif candidates:
                row["status"] = "ambiguous_workshop_id"

    details = steam_details(sorted({row["workshop_id"] for row in maps if row["workshop_id"]}))
    for row in maps:
        item = details.get(row["workshop_id"], {})
        preview = https_url(item.get("preview_url")) if item.get("result") == 1 else None
        row["preview_url"] = preview
        row["workshop_title"] = item.get("title") if item.get("result") == 1 else None
        status = "preview_found" if preview else "no_public_preview" if row["workshop_id"] else (
            "missing_workshop_id" if row["workshop"] else "not_workshop")
        row.setdefault("status", status)
    return maps


def unique_previews(maps):
    ids = {}
    for row in maps:
        if row["map"] and row.get("configured", True) and row["workshop_id"]:
            ids.setdefault(row["map"], set()).add(row["workshop_id"])
    for row in maps:
        if len(ids.get(row["map"], ())) > 1:
            row["status"] = "conflicting_workshop_ids"
    return {row["map"]: row for row in maps if row["map"] and row["status"] == "preview_found" and row.get("configured", True)}


def download_preview(row, destination, ftp):
    from PIL import Image

    def valid_image(data):
        if not data or len(data) > MAX_IMAGE_BYTES:
            raise ValueError("preview is empty or exceeds 16 MiB")
        with Image.open(BytesIO(data)) as image:
            image.verify()
        destination.write_bytes(data)
        return destination

    if ftp is not None:
        for path in row.get("server_preview_files", []):
            buffer = BytesIO()
            size = 0

            def collect(chunk):
                nonlocal size
                size += len(chunk)
                if size <= MAX_IMAGE_BYTES:
                    buffer.write(chunk)

            try:
                ftp.retrbinary(f"RETR {path}", collect)
                if size > MAX_IMAGE_BYTES:
                    raise ValueError("preview exceeds 16 MiB")
                return valid_image(buffer.getvalue())
            except (error_perm, OSError, ValueError, Image.DecompressionBombError) as error:
                print(f"Warning: preview for {row['map']} from FTP: {error}", file=sys.stderr)
    if row.get("preview_url"):
        try:
            with urlopen(row["preview_url"], timeout=30) as response:
                if not https_url(response.geturl()):
                    raise ValueError("preview redirected away from HTTPS")
                return valid_image(response.read(MAX_IMAGE_BYTES + 1))
        except (OSError, ValueError, Image.DecompressionBombError) as error:
            print(f"Warning: preview for {row['map']} from Steam: {error}", file=sys.stderr)
    return None


def build_panorama(maps, output_dir, ftp=None, preview_root=None):
    builder = runpy.run_path(str(Path(__file__).with_name("Pack-MapVoteAddon.py")))
    with TemporaryDirectory(prefix="ggmc-workshop-previews-") as directory:
        def fetch_image(key):
            return download_preview({**maps[key], "map": key}, Path(directory) / f"{key}.img", ftp)

        entries = ((key, None) for key in maps)
        return builder["pack"](entries, None, output_dir, preview_root, fetch_image)


def read_map_list(path):
    maps = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(maps, dict) or not maps:
        raise ValueError("Map list must be a non-empty JSON object keyed by map filenames")
    for key, info in maps.items():
        if not MAP_KEY.fullmatch(key) or not isinstance(info, dict):
            raise ValueError(f"Invalid map entry: {key!r}")
        display = info.get("display")
        if not isinstance(display, str) or not display.strip():
            raise ValueError(f"Map {key!r} requires a non-empty display name")
        mapid = info.get("mapid")
        if mapid not in (None, "") and (not isinstance(mapid, str) or not re.fullmatch(r"[1-9][0-9]{0,19}", mapid)):
            raise ValueError(f"Map {key!r} has an invalid Workshop ID")
    return maps


def publish_results(args, maps, installed, ftp=None):
    resolve_previews(maps, installed)
    found = unique_previews(maps)
    if args.panorama:
        by_key = {}
        for row in maps:
            if row["map"] is not None:
                by_key.setdefault(row["map"], []).append(row)
        chosen = {}
        for key, choices in by_key.items():
            candidates = [row for row in choices if row["configured"]] or choices
            ids = {row["workshop_id"] for row in candidates if row["workshop_id"]}
            if len(ids) > 1 or any(row["status"] in {"conflicting_workshop_ids", "ambiguous_workshop_id"} for row in candidates):
                chosen[key] = {"workshop_id": "", "preview_url": None}
            else:
                chosen[key] = next((row for row in candidates if row.get("server_preview_files") or row["preview_url"]), candidates[0])
        previews = build_panorama(chosen, args.output_dir, ftp, args.preview_root)
        for row in maps:
            row["generated_preview_path"] = previews.get(row["map"])
    elif args.output_dir:
        args.output_dir.mkdir(parents=True, exist_ok=False)
    if args.output_dir:
        write_json(args.output_dir / "workshop-previews.json", maps)
    if args.artifact:
        write_artifact(args.artifact, maps)
    print(f"{len(maps)} map entries, {len(found)} usable Workshop previews: {args.artifact or args.output_dir}")


def write_json(path, data):
    with path.open("x", encoding="utf-8") as file:
        json.dump(data, file, indent=2, ensure_ascii=False)
        file.write("\n")


def read_env(path):
    settings = {}
    for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        name, separator, value = line.partition("=")
        name, value = name.strip(), value.strip()
        if not separator or not re.fullmatch(r"GGMC_[A-Z0-9_]+", name):
            raise ValueError(f"Invalid deployment setting at line {number} in {path}")
        settings[name] = value[1:-1] if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'" else value
    return settings


def write_artifact(path, data):
    with NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent, delete=False) as file:
        temporary = Path(file.name)
        try:
            json.dump(data, file, indent=2, ensure_ascii=False)
            file.write("\n")
            file.close()
            temporary.replace(path)
        finally:
            temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("host", nargs="?", help="FTP hostname")
    parser.add_argument("--env-file", type=Path, help="deployment settings with GGMC_FTP_* values")
    parser.add_argument("--map-list", type=Path, help="local map list JSON; resolve Workshop previews without FTP")
    parser.add_argument("--port", type=int)
    parser.add_argument("--user")
    parser.add_argument("--tls", action="store_true", help="use explicit FTPS")
    parser.add_argument("--csgo-dir", help="game/csgo as seen by FTP")
    parser.add_argument("--workshop-dir", help="FTP directory with numbered Workshop subdirectories")
    parser.add_argument("--output-dir", type=Path)
    parser.add_argument("--preview-root", type=Path, help="addon mapPreviews directory with optional manualy overrides")
    parser.add_argument("--artifact", type=Path, help="refresh a JSON map inventory at this path")
    parser.add_argument("--panorama", action="store_true", help="prepare Panorama addon sources in --output-dir (requires Pillow)")
    args = parser.parse_args()
    if not (args.artifact or args.output_dir) or (args.panorama and not args.output_dir):
        parser.error("specify --artifact or --output-dir; --panorama requires --output-dir")

    try:
        if args.map_list:
            mapcycle = read_map_list(args.map_list)
            publish_results(args, list(map_rows(mapcycle)), {})
            return 0
        settings = read_env(args.env_file) if args.env_file else {}
        host = args.host or settings.get("GGMC_FTP_HOST")
        user = args.user or settings.get("GGMC_FTP_USER")
        if not host or not user:
            parser.error("set GGMC_FTP_HOST and GGMC_FTP_USER in .env or pass the host and --user")
        port = args.port or int(settings.get("GGMC_FTP_PORT") or 21)
        tls = args.tls or settings.get("GGMC_FTP_TLS") == "1"
        workshop_dir = args.workshop_dir or settings.get("GGMC_FTP_WORKSHOP_DIR")
        password = os.environ.get("GGMC_FTP_PASSWORD") or settings.get("GGMC_FTP_PASSWORD")
        if not password:
            password = getpass("FTP password: ")
        root = PurePosixPath(args.csgo_dir or settings.get("GGMC_FTP_CSGO_DIR") or "/game/csgo")
        connection = FTP_TLS(context=ssl.create_default_context()) if tls else FTP()
        with connection as ftp:
            ftp.connect(host, port, timeout=30)
            ftp.login(user, password)
            if tls:
                ftp.prot_p()
            mapcycle = read_json(ftp, root / "cfg/GGMCmaps.json")
            config = read_json(ftp, root / PLUGIN_CONFIG, optional=True)
            low_players = (config.get("OtherSettings") or {}).get("LowPlayerMaps") or {}
            maps = list(configured_maps(mapcycle, low_players))
            workshop_roots = [root / "maps/workshop"]
            extra = PurePosixPath(workshop_dir) if workshop_dir else root.parent.parent / "steamapps/workshop/content/730"
            if extra not in workshop_roots:
                workshop_roots.append(extra)
            screenshot_config = read_json(ftp, root / PLUGIN_CONFIG.replace("GG1MapChooser.json", "GG1MapPreviews.json"), optional=True)
            if not screenshot_config:
                screenshot_config = (config.get("MenuSettings") or {}).get("MapThumbnails") or {}
            packages = list(inventory_packages(ftp, root / "maps", workshop_roots))
            stock_folder = PurePosixPath("panorama/images/map_icons/screenshots/360p")
            stock_previews = {entry.name.removesuffix("_png.vtex_c"): "s2r://" + entry.as_posix().removesuffix("_c")
                              for entry in vpk_index(ftp, root / "pak01_dir.vpk")
                              if entry.parent == stock_folder and entry.name.endswith("_png.vtex_c")}
            installed = merge_inventory(maps, packages, screenshot_config, stock_previews)
            publish_results(args, maps, installed, ftp)
    except (*all_errors, ImportError, ValueError, KeyError) as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
