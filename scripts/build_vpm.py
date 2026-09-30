#!/usr/bin/env python3
"""Build a VPM zip and index from the package manifest."""
import hashlib
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "Packages/dev.nx.nxclone"
manifest = json.loads((PACKAGE / "package.json").read_text())
version = manifest["version"]
filename = f"{manifest['name']}-{version}.zip"
out = ROOT / "dist"
out.mkdir(exist_ok=True)
archive = out / filename
with ZipFile(archive, "w", ZIP_DEFLATED) as zip_file:
    for file in sorted(PACKAGE.rglob("*")):
        if file.is_file() and not file.name.endswith((".pyc", ".dll")):
            zip_file.write(file, file.relative_to(PACKAGE).as_posix())
with ZipFile(archive) as zip_file:
    assert json.loads(zip_file.read("package.json")) == manifest
release_url = f"https://github.com/nerdrx/nxclone/releases/download/v{version}/{filename}"
listing = dict(manifest, url=release_url, zipSHA256=hashlib.sha256(archive.read_bytes()).hexdigest())
index = {
    "name": "nxclone",
    "id": "dev.nx.nxclone",
    "url": "https://raw.githubusercontent.com/nerdrx/nxclone/main/index.json",
    "author": "nerdrx@users.noreply.github.com",
    "packages": {manifest["name"]: {"versions": {version: listing}}},
}
(ROOT / "index.json").write_text(json.dumps(index, indent=2) + "\n")
print(archive)
