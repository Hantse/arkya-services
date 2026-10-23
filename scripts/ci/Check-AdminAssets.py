"""Check that the published realm map is PNG data rather than a Git LFS pointer."""

from pathlib import Path
import sys

root = Path(sys.argv[1])
island = root / "images/islands/starter-island.png"
with island.open("rb") as source:
    signature = source.read(128)
if signature.startswith(b"version https://git-lfs.github.com/spec/v1"):
    raise SystemExit("Admin island image is a Git LFS pointer. Hydrate the Admin checkout before building.")
if not signature.startswith(b"\x89PNG\r\n\x1a\n"):
    raise SystemExit("Admin island image is missing or is not PNG data.")
print("Validated hydrated Admin island PNG.")
