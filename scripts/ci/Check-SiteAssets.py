"""Refuse Docker contexts containing Git LFS pointer text instead of public binaries."""

from pathlib import Path
import sys

root = Path(sys.argv[1])
extensions = {".webp", ".png", ".jpg", ".jpeg", ".gif", ".wasm", ".data", ".unityweb", ".bin"}
assets = [path for path in root.rglob("*") if path.is_file() and path.suffix.lower() in extensions]
if not assets:
    raise SystemExit("No public binary assets found. The site checkout is incomplete.")
for path in assets:
    with path.open("rb") as source:
        if source.read(128).startswith(b"version https://git-lfs.github.com/spec/v1"):
            raise SystemExit(f"Git LFS pointer in site Docker context: {path}. Hydrate the checkout first.")
with (root / "assets/hero.webp").open("rb") as source:
    signature = source.read(12)
if not (signature.startswith(b"RIFF") and signature[8:] == b"WEBP"):
    raise SystemExit("The site hero is not a real WebP image.")
print(f"Validated {len(assets)} public binary assets; no Git LFS pointers.")
