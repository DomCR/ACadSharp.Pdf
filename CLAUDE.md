# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ACadSharp.Pdf generates PDF files from DWG/DXF drawings parsed by the [ACadSharp](https://github.com/DomCR/ACadSharp) library (included as a git submodule under `src/ACadSharp/`). It also generates preview images via SkiaSharp.

## Build & Test Commands

All commands run from the `src/` directory (solution root):

```bash
# Restore, build, test
cd src
dotnet restore
dotnet build
dotnet test --framework net9.0

# Run a single test class
dotnet test --framework net9.0 --filter "FullyQualifiedName~RenderInfrastructureTests"

# Run a single test method
dotnet test --framework net9.0 --filter "FullyQualifiedName~RenderInfrastructureTests.MethodName"

# .NET Framework tests (CI runs on windows-latest)
dotnet test --framework net48
```

After cloning, initialize the ACadSharp submodule:
```bash
git submodule update --force --recursive --init --remote
```

## Build Configurations

- **Debug** — references ACadSharp via local project reference (`src/ACadSharp/src/ACadSharp/ACadSharp.csproj`)
- **Release** — references ACadSharp via NuGet (`ACadSharp 3.3.*`), auto-generates `.nupkg`
- **Test** — defined in `Directory.Build.props` alongside Debug/Release

The library targets: net5.0, net6.0, net7.0, net8.0, net9.0, net48, netstandard2.1. Tests target: net9.0, net48.

## Architecture

### Two Rendering Pipelines

The codebase has two rendering paths, toggled by `PdfConfiguration.UseSceneGraph` (default: `false`):

1. **Legacy pipeline** — `Core/IO/PdfPen.cs` renders entities directly to PDF stream operators.
2. **Scene graph pipeline** — multi-stage IR-based approach:
   - `SceneGraphBuilder` → builds a tree of `RenderNode` objects (IR)
   - `SceneFlattener` → traverses the tree, produces flat `DrawCommand` list
   - `PdfRenderBackend` → serializes draw commands into PDF content stream

Pipeline entry point: `SceneGraphPdfPipeline.Render()` in `Core/Render/SceneGraph/SceneGraphPdfPipeline.cs`.

### Scene Graph IR Types (`Core/Render/RenderNodes.cs`)

```
RenderNode (abstract, carries SourceHandle)
  ├─ PathNode (segments, stroke, fill)
  ├─ TextRunNode (text, font, position, color)
  ├─ GroupNode (transform matrix, children)
  └─ ClipNode (clip path, children)
```

Path geometry uses `PathSegment` variants: `MoveTo`, `LineTo`, `CubicTo`, `Close`.

### Rendering Stages (specs in `specs/stages/`)

Each stage has a detailed spec. Stages 00-01 are implemented; 02-09 are planned:

| Stage | Topic | Status |
|-------|-------|--------|
| 00 | Render infrastructure (IR, transforms, property resolution) | Done |
| 01 | INSERT / MINSERT block expansion | Done |
| 02 | TEXT / MTEXT / ATT layout | Planned |
| 03 | DIMENSIONS | Planned |
| 04 | MULTILEADER | Planned |
| 05 | HATCH | Planned |
| 06 | UNDERLAY / PDF / IMAGE | Planned |
| 07 | MLINE | Planned |
| 08 | RAY / XLINE | Planned |
| 09 | TOLERANCE | Planned |

### Key Modules

- **`Core/Render/Style/PropertyResolver.cs`** — resolves color, lineweight, linetype with DXF precedence: TrueColor(420) > ACI(1-255) > ByLayer > ByBlock > fallback. Also handles visibility (invisible flag, layer off/frozen/not-plottable).
- **`Core/Render/Transforms/TransformHelper.cs`** — coordinate transformations: OCS→WCS (arbitrary axis algorithm), viewport model→paper, paper→PDF points, shear for oblique text.
- **`Core/Render/SceneGraph/BlockExpander.cs`** — recursive INSERT expansion with MINSERT grid, scale inheritance, ByBlock property propagation, Layer 0 inheritance, cycle detection.
- **`Core/Render/Text/TextLayoutEngine.cs`** — text layout (Stage 02).
- **`Core/Render/Flattening/SceneFlattener.cs`** — tree-walk producing `FlatDrawCommand` list.
- **`Core/Render/Pdf/PdfRenderBackend.cs`** — emits PDF content stream from flat commands.
- **`PdfConfiguration.cs`** — settings: `UseSceneGraph`, `ArcPrecision`, `DotSize`, `DecimalFormat`, `ShxFontSubstitutions`, `LineWeightValues`.

### Core PDF Infrastructure (`Core/`)

Low-level PDF object model: `PdfObject`, `PdfDictionary`, `PdfArray`, `PdfPage`, `PdfWriter`, `PdfContent`, `PdfCatalog`. These map directly to PDF spec structures.

### Coordinate Spaces & Unit Conventions

- DXF lineweight: hundredths of mm (group code 370). Convert to PDF points: `mm * 72 / 25.4`.
- Lineweight does NOT scale with geometry — it's constant in paper/PDF units.
- Linetype dash lengths DO scale with geometric transforms (viewport/block scale).
- ACI color 7 is special-cased to map to black on white PDF background.
- INSERT transform composition: `T(insertPt) * Rz(angle) * S(sx,sy,sz) * T(-basePoint)` in OCS.

### Logging

`RenderLog` tracks per-entity rendering status (Rendered/Skipped/NotImplemented/Error) with reasons. Accessible after render via `PdfConfiguration.LastRenderLog`.

## Test Organization

Tests use XUnit in `src/ACadSharp.Pdf.Tests/`:

- **`RenderInfrastructureTests.cs`** — Stage 00: OCS→WCS transforms, ACI color mapping, lineweight conversion, dash scaling, PDF text escaping.
- **`InsertBlockExpansionTests.cs`** — Stage 01: base point transform, MINSERT grids, scale inheritance, ATTRIB handling, ByBlock/Layer 0 inheritance, cycle detection.
- **`PdfExporterTests.cs`** — integration tests for full export pipeline.
