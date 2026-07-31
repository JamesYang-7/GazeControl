#!/usr/bin/env python3
"""Replace spaces with hyphens in file names.

Unity-aware: an asset and its `.meta` sidecar are renamed together, because a
`.meta` whose name no longer matches its asset makes the Editor drop the asset's
GUID and re-import it as a new asset — which silently breaks every reference to
it from scenes and prefabs.

Usage:
    python Tools/rename-spaces.py <directory> [--recursive] [--dry-run]
"""

import argparse
import pathlib
import sys

META_SUFFIX = ".meta"


def target_name(name: str) -> str:
    return name.replace(" ", "-")


def collect(directory: pathlib.Path, recursive: bool) -> list[pathlib.Path]:
    paths = directory.rglob("*") if recursive else directory.glob("*")
    # Skip .meta files here: each is renamed alongside the asset it belongs to,
    # so handling them separately would rename them twice or orphan them.
    return sorted(p for p in paths if p.is_file() and p.suffix != META_SUFFIX)


def rename(path: pathlib.Path, dry_run: bool) -> bool:
    new_name = target_name(path.name)
    if new_name == path.name:
        return False

    destination = path.with_name(new_name)
    if destination.exists():
        print(f"  SKIP  {path.name}\n        -> {new_name} already exists", file=sys.stderr)
        return False

    meta = path.with_name(path.name + META_SUFFIX)
    meta_destination = destination.with_name(new_name + META_SUFFIX)

    print(f"  {path.name}\n    -> {new_name}" + ("   (+ .meta)" if meta.exists() else ""))
    if dry_run:
        return True

    path.rename(destination)
    if meta.exists():
        meta.rename(meta_destination)

    return True


def main() -> int:
    parser = argparse.ArgumentParser(description="Replace spaces with hyphens in file names.")
    parser.add_argument("directory", type=pathlib.Path)
    parser.add_argument("--recursive", action="store_true", help="descend into subdirectories")
    parser.add_argument("--dry-run", action="store_true", help="report renames without performing them")
    args = parser.parse_args()

    if not args.directory.is_dir():
        print(f"Not a directory: {args.directory}", file=sys.stderr)
        return 1

    print(f"{'Would rename' if args.dry_run else 'Renaming'} in {args.directory}:")
    renamed = sum(rename(path, args.dry_run) for path in collect(args.directory, args.recursive))
    print(f"{renamed} file(s) {'would be ' if args.dry_run else ''}renamed.")

    if renamed and not args.dry_run and "Assets" in args.directory.parts:
        print("Assets changed — let Unity reimport so any missing .meta files are generated.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
