# MapLab

**by Axnise**

MapLab is a 2D map toolkit for Unity. You build a map from curved ground shapes (plateaus, rings, paths, voids), split it into layers and mega chunks, bake it into chunked images, and bake the walkable area into a file other code can read.

## Requirements

- Unity 6 (6000.4 or newer)
- Universal Render Pipeline with the 2D Renderer
- Input System (used by the Elevation sample only)

## Install

Copy or clone this repository into a folder under `Assets/`, for example `Assets/ThirdParty/MapLab`.

## Folders

| Folder | What it holds |
|---|---|
| `Runtime/` | Shapes, layers, chunk streaming, world and preset data (`MapLab` assembly, no dependencies) |
| `Editor/` | The MapLab window, bakers, walkable area bake, presets and probes (`MapLab.Editor`) |
| `Samples/Navigation/` | Example consumer that builds a NavMesh from the baked walkable file |
| `Samples/Elevation/` | Example of walking up plateaus |
| `Art/` | Default tree and grass tuft art used by the demo |
| `Demo/` | Demo scene with its baked layers and walkable file |
| `Tests/Editor/` | EditMode tests |

## Walkable output

Baking writes a versioned JSON file (grid bitset and obstacle polygons) and a PNG mask. Consumers read these files and do not reference MapLab code. The NavMesh code in `Samples/Navigation` shows one way to use them.

## Open the window

`Tools > MapLab > MapLab Window`. New maps and shapes are under `GameObject > MapLab`.
