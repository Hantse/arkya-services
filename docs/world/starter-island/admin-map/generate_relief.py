"""Render the checked-in Unity R16 coastal-height export as a north-up relief map."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re

import matplotlib

matplotlib.use("Agg")
from matplotlib.colors import to_rgb
from matplotlib.figure import Figure
import numpy as np


ROOT = Path(__file__).resolve().parents[4]
SOURCE = ROOT / "Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Terrain/CoastalHeights.asset"
LAYOUT = ROOT / "Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Layout/StarterIslandDefinition.asset"
OUTPUT = ROOT / "backend/src/Arkya.Admin/wwwroot/images/islands/starter-island.png"
METADATA = Path(__file__).with_name("metadata.json")


def field(text: str, name: str) -> str:
    match = re.search(r"^\s*" + re.escape(name) + r":\s*([^\r\n]+)$", text, re.MULTILINE)
    if not match:
        raise ValueError(f"Missing source field: {name}")
    return match.group(1).strip()


def sample(heights: np.ndarray, x: float, z: float, size: float) -> float:
    gx, gz = np.array([x, z]) / size * (heights.shape[0] - 1)
    ix, iz = int(gx), int(gz)
    fx, fz = gx - ix, gz - iz
    return float((1 - fz) * ((1 - fx) * heights[iz, ix] + fx * heights[iz, ix + 1])
                 + fz * ((1 - fx) * heights[iz + 1, ix] + fx * heights[iz + 1, ix + 1]))


def main() -> None:
    source_bytes = SOURCE.read_bytes()
    source = source_bytes.decode("utf-8")
    layout_bytes = LAYOUT.read_bytes()
    layout = layout_bytes.decode("utf-8")
    width, height = int(field(source, "m_Width")), int(field(source, "m_Height"))
    if width != 512 or height != 512 or field(source, "m_TextureFormat") != "9":
        raise ValueError("Expected the verified 512×512 R16 Unity texture.")
    raw = bytes.fromhex(field(source, "_typelessdata"))
    if len(raw) != width * height * 2:
        raise ValueError("Unexpected R16 payload size.")
    size, sea = float(field(layout, "TerrainSize")), float(field(layout, "SeaLevel"))
    if size != 1536 or sea != 28:
        raise ValueError("Revalidate world bounds and height scale before changing the map footprint.")
    # CreateOcean / RefreshCoastalHeights writes pixels[z * 512 + x] = height / 192.
    heights = np.frombuffer(raw, dtype="<u2").reshape(height, width).astype(float) / 65535 * 192
    if not np.isfinite(heights).all() or heights.max() <= sea:
        raise ValueError("Invalid terrain heights.")
    boundary = np.concatenate((heights[0], heights[-1], heights[:, 0], heights[:, -1]))
    if boundary.max() >= sea:
        raise ValueError("Expected the documented submerged terrain boundary.")
    checks = []
    for name in ("ArrivalPosition", "DeparturePosition"):
        match = re.fullmatch(r"\{x: ([\d.]+), y: ([\d.]+), z: ([\d.]+)\}", field(layout, name))
        if not match:
            raise ValueError(f"Unexpected layout point: {name}")
        x, y, z = map(float, match.groups())
        expected = y - 1  # Layout authoring uses Ground(x, z, 1).
        actual = sample(heights, x, z, size)
        error = abs(actual - expected)
        if error > 0.4:
            raise ValueError(f"Coastal export does not match {name}: {error:.3f} m error.")
        checks.append({"point": name, "x": x, "z": z, "expectedHeight": expected,
                       "sampledHeight": actual, "absoluteErrorMetres": error})

    levels = np.array([0, 15, 25, 28, 31, 36, 50, 70, 95, 125, 160, 192])
    colors = np.array([to_rgb(value) for value in (
        "#123d52", "#205c70", "#388d99", "#70b5b1", "#e0cf9c", "#b8bd85",
        "#8ba172", "#6d8969", "#7d8a6f", "#97917b", "#b7ad96", "#d0c9b5")])
    rgb = np.stack([np.interp(heights, levels, colors[:, c]) for c in range(3)], axis=-1)
    dz, dx = np.gradient(heights, size / (width - 1))
    light = np.array([-0.45, 0.55, 0.70])
    light /= np.linalg.norm(light)
    normals = np.stack((-dx, -dz, np.ones_like(heights)), axis=-1)
    normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
    shade = np.clip(0.77 + 0.33 * (normals @ light), 0.65, 1.12)
    rgb *= np.where(heights > sea, shade, 1)[..., None]
    rgb = np.clip(rgb, 0, 1)

    figure = Figure(figsize=(10.24, 10.24), dpi=100)
    axes = figure.add_axes((0, 0, 1, 1))
    half_step = size / (width - 1) / 2
    axes.imshow(rgb, origin="lower", interpolation="bilinear",
                extent=(-half_step, size + half_step, -half_step, size + half_step))
    coordinates = np.linspace(0, size, width)
    axes.contour(coordinates, coordinates, heights, levels=[sea], colors=["#d8e2c5"], linewidths=0.7)
    axes.contour(coordinates, coordinates, heights, levels=np.arange(40, 190, 20),
                 colors=["#344b3d"], linewidths=0.35, alpha=0.22)
    axes.set(xlim=(0, size), ylim=(0, size), aspect="equal")
    axes.set_axis_off()
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    figure.savefig(OUTPUT, dpi=100, metadata={"Software": "Arkya terrain relief generator"})

    metadata = {
        "kind": "terrain-relief", "label": "Carte du relief", "islandId": "starter-island",
        "image": "/images/islands/starter-island.png", "imagePixels": {"width": 1024, "height": 1024},
        "bounds": {"minX": 0, "minZ": 0, "maxX": size, "maxZ": size},
        "orientation": {"right": "+X", "up": "+Z", "origin": "bottom-left"},
        "heightScaleMetres": 192, "seaLevelMetres": sea,
        "source": SOURCE.relative_to(ROOT).as_posix(),
        "sourceSha256": hashlib.sha256(source_bytes).hexdigest(),
        "layoutSha256": hashlib.sha256(layout_bytes).hexdigest(),
        "sourceSamples": {"width": width, "height": height, "format": "R16 little-endian"},
        "heightRangeMetres": {"min": float(heights.min()), "max": float(heights.max())},
        "validation": {"submergedBoundaryMaximumMetres": float(boundary.max()), "layoutSamples": checks},
        "imageSha256": hashlib.sha256(OUTPUT.read_bytes()).hexdigest(),
        "limitations": ["Baked terrain elevation only; no buildings, vegetation or gameplay state.",
                        "Source sampling interval is approximately 3 metres; this is not collision data.",
                        "Refresh CoastalHeights in Unity after terrain edits, then regenerate."]
    }
    METADATA.write_text(json.dumps(metadata, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(OUTPUT), "metadata": str(METADATA), "layoutSamples": checks}, ensure_ascii=False))


if __name__ == "__main__":
    main()
