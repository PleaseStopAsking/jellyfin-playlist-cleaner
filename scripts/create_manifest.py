#!/usr/bin/env python3
"""Create a Jellyfin plugin repository manifest for a release package."""

import argparse
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path


def normalize_version(value: str) -> str:
    version = value.removeprefix("v")
    if not re.fullmatch(r"\d+(?:\.\d+){0,3}", version):
        raise ValueError("Release version must contain one to four numeric components")
    return ".".join([*version.split("."), *(["0"] * (3 - version.count(".")))])


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--metadata", type=Path, default=Path("manifest.json"))
    parser.add_argument("--previous-manifest", type=Path)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--source-url", required=True)
    parser.add_argument("--changelog-url")
    parser.add_argument("--output", type=Path, default=Path("manifest.json"))
    args = parser.parse_args()

    source = args.previous_manifest if args.previous_manifest and args.previous_manifest.is_file() else args.metadata
    manifest = json.loads(source.read_text(encoding="utf-8"))
    if not isinstance(manifest, list) or len(manifest) != 1:
        raise ValueError("Expected manifest.json to contain exactly one plugin")

    package_bytes = args.package.read_bytes()
    version = normalize_version(args.version)
    previous_versions = manifest[0].get("versions", [])
    new_version = {
        "version": version,
        "changelog": args.changelog_url or "See the GitHub release notes.",
        "targetAbi": "12.4.0.0",
        "sourceUrl": args.source_url,
        "checksum": hashlib.md5(package_bytes, usedforsecurity=False).hexdigest(),
        "timestamp": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
    }
    manifest[0]["versions"] = [
        new_version,
        *(entry for entry in previous_versions if entry.get("version") != version),
    ]
    args.output.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()