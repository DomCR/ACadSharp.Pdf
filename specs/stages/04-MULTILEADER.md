# Stage 04: MULTILEADER (MLEADER)

## Overview

MULTILEADER (MLEADER) is a complex DXF/DWG annotation entity that combines:

- **Leader geometry**: one or more leader roots, each containing one or more leader lines (polylines or splines), optional line breaks, optional dogleg ("landing") segments, and arrowheads.
- **Annotation content**: either **MTEXT** content or **block** content (plus block attribute overrides).

Unlike DIMENSION (which often renders via anonymous blocks), MULTILEADERs must be rendered from their serialized internal data.

> **Key reference**: ["Demystifying DXF: LEADER and MULTILEADER"](https://atlight.github.io/formats/dxf-leader.html) (Alan Thomas, ThinkSpatial, 2018). A local copy is saved at `specs/dxf-leader-and-multileader-atlight.md`.

## Scope (ACadSharp.Pdf)

### Pipeline

MULTILEADER rendering is implemented in the **scene graph pipeline** (`PdfConfiguration.UseSceneGraph = true`) in:

- `src/ACadSharp.Pdf/Core/Render/SceneGraph/SceneGraphBuilder.cs` (`buildMultiLeader()` and helper methods)

The legacy `Core/IO/PdfPen` pipeline is not the reference implementation for this stage.

### Supported features (current)

- Straight and spline leaders (`MultiLeaderPathType.StraightLineSegments`, `Spline`, `Invisible`)
- Multiple leader roots and multiple leader lines per root
- Arrowheads:
  - Custom arrow blocks (when handle-resolved to a `BlockRecord`)
  - Fallback filled-triangle arrow when no block is available
- Dogleg ("landing") rendering for **horizontal** straight leaders
- Leader line breaks and dogleg breaks (when present in the ACadSharp object model)
- MTEXT content rendered via Stage 02 text layout
- Block content rendered via Stage 01 INSERT/block expansion
- Block attribute overrides applied to the inserted block content
- Correct propagation of parent INSERT transforms

### Non-goals / limitations (current)

- **No explicit content repositioning using `LandingGap`**: ACadSharp-provided content locations (`TextLocation`, `BlockContentLocation`) are treated as authoritative.
- **No bbox-based connection logic** ("connect to text box edge", block extents vs base point) beyond using the stored connection/direction/distance data.
- **Annotative context switching** is not handled (render uses `MultiLeader.ContextData`).
- Some MLEADERSTYLE properties are ignored (draw order, background mask, etc.).
- Context-data “render plane” orientation fields (`BasePoint`, `BaseDirection`, `BaseVertical`, `NormalReversed`) are not applied; geometry is treated as WCS.
- Context-data MTEXT extras (background fill/mask, columns/word-break) are not emitted because `createMLeaderText()` only maps core MTEXT placement/style fields.

## Domain knowledge (data model)

### ACadSharp object model

ACadSharp parses the nested DXF/DWG structure into a usable object graph:

- `ACadSharp.Entities.MultiLeader`
  - `Style` (`ACadSharp.Objects.MultiLeaderStyle`) + `PropertyOverrideFlags`
  - `ContextData` (`ACadSharp.Objects.MultiLeaderObjectContextData`)
    - Content: text or block fields (text label, text location, block record, block transforms, etc.)
    - Leaders: `LeaderRoots` (list)
      - `LeaderRoot.ConnectionPoint`: *landing point* (DXF: “last leader line point”)
      - `LeaderRoot.Direction`: dogleg vector
      - `LeaderRoot.LandingDistance`: dogleg length
      - `LeaderRoot.BreakStartEndPointsPairs`: dogleg breaks
      - `LeaderRoot.Lines` (list of `LeaderLine`)
        - `LeaderLine.Points`: leader vertices in WCS
        - `LeaderLine.PathType`, line-style/arrow overrides + `OverrideFlags`
        - `LeaderLine.StartEndPoints` + `LeaderLine.SegmentIndex`: leader line breaks

### Handles

MULTILEADER uses **handle references** for many relationships (styles, arrow blocks, text styles, content blocks, linetypes). ACadSharp resolves these to object references where possible.

### DXF vs DWG parsing note (breaks)

As of this stage:

- **DWG** parsing in ACadSharp populates leader breaks and dogleg breaks.
- **DXF** parsing in ACadSharp also populates leader line breaks (`LeaderLine.StartEndPoints` + `SegmentIndex`) and dogleg breaks (`LeaderRoot.BreakStartEndPointsPairs`), when present.

ACadSharp.Pdf supports breaks when they are present in the object model, but does not attempt to reconstruct them from raw DXF group codes.

## Rendering rules (geometry)

### Leader lines, landing point, dogleg endpoint

For a given leader root:

- `landingPoint = root.ConnectionPoint`
- `doglegDirection = root.Direction` (fallback inferred; see below)
- `landingDistance = root.LandingDistance` (fallback to resolved style distance)
- `doglegEndpoint = landingPoint + doglegDirection * landingDistance`

**Critical rule (horizontal leaders)**: even if the dogleg does not draw, the leader line must terminate at the **dogleg endpoint** (not at the landing point).

### When the dogleg draws

Dogleg drawing is enabled only when all of the following are true:

- Horizontal attachment (root/style is not vertical)
- `EnableLanding == true` **and** `EnableDogleg == true`
- Effective path type is **straight**
- `landingDistance > 0`
- Dogleg direction is not degenerate

Additionally, ACadSharp.Pdf avoids drawing **orphan doglegs**: a dogleg is rendered only if at least one visible leader line in that root produced renderable geometry.

### Dogleg direction fallback

If `root.Direction` is zero, the renderer infers dogleg direction from `contentAnchor - landingPoint`:

- Horizontal attachment: sign of ΔX (default to +X if ambiguous)
- Vertical attachment: direction toward content (default to +Y/+X fallback)

### Spline leaders

Spline leaders are stored as **fit points** (not control points). ACadSharp.Pdf tessellates these points into a polyline using a Catmull–Rom style interpolation (configurable density derived from `PdfConfiguration.ArcPrecision`).

### Breaks (DIMBREAK)

If break pairs exist:

- Leader line breaks split a single polyline segment into rendered sub-segments.
- Dogleg breaks similarly split the dogleg segment.

Break splitting is projection-based and merges overlapping intervals.

### Arrowheads

Arrowheads are drawn at the **first** leader vertex (the “tip”):

- Custom arrowhead block: inserted at the tip, scaled by arrow size, rotated from the first segment direction.
- Otherwise, a filled-triangle arrow is emitted.

Arrowheads are skipped if the first segment is too short relative to arrow size.

## Implementation (ACadSharp.Pdf)

### Key methods

All of these live in `src/ACadSharp.Pdf/Core/Render/SceneGraph/SceneGraphBuilder.cs`:

- `buildMultiLeader(...)`: main entry (builds paths + content, returns a `GroupNode`)
- `resolveMLeaderStyle(MultiLeader)` / `resolveMLeaderLineStyle(...)`: style + override resolution
- `buildLeaderVertices(...)`: appends the correct termination point (landing point or dogleg endpoint)
- `shouldDrawDogleg(...)`, `resolveLandingDistance(...)`, `resolveDoglegDirection(...)`
- `splitSegmentByBreaks(...)`: break splitting for leader segments and doglegs
- `tessellateSpline(...)`: spline → polyline tessellation
- `addMLeaderArrow(...)`: custom arrow block insert or fallback triangle
- `createMLeaderText(...)`: converts context data → `MText` entity for Stage 02
- `createMLeaderBlockInsert(...)` + `applyMLeaderBlockAttributes(...)`: block content insert + attribute overrides

### Content generation strategy

ACadSharp.Pdf does not attempt to “recompute” the MLEADER layout from style parameters. Instead, it:

- trusts ACadSharp’s stored content positions (`TextLocation`, `BlockContentLocation`) for placement
- uses style/override resolution primarily for **visibility and appearance** (path type, stroke, arrow size, enable dogleg/landing)
- uses connection/landing data (`ConnectionPoint`, `Direction`, `LandingDistance`) to build the leader end geometry

## Corner cases (must-handle)

- **No `ContextData`**: skip with a render-log entry.
- **No leader roots / no leader lines**: render content only; do not draw doglegs.
- **Invisible path type**: render content only.
- **Leader line with <2 vertices**: skip that line; do not draw arrowhead for it.
- **Dogleg disabled (or landing disabled)**: no separate dogleg segment; leader terminates at the dogleg endpoint.
- **Spline with only 2 distinct points**: treat as a straight segment.
- **Break pairs that cover the entire segment**: segment renders as empty (no output for that interval).
- **Custom arrowhead block missing or fails to render**: fallback triangle is used.
- **Block content missing or degenerate scale**: skip content with a render-log entry.

## Testing strategy (ACadSharp.Pdf)

### Unit tests (implemented)

Unit tests validate the scene-graph output by inspecting emitted PDF operators:

- `src/ACadSharp.Pdf.Tests/MultiLeaderRenderingTests.cs`
  - Straight + MTEXT leader: leader polyline + dogleg + text
  - Invisible leader path: content only
  - Spline tessellation produces multiple segments
  - Leader line breaks split a segment
  - Dogleg disabled: leader extends to dogleg endpoint (no separate dogleg segment)
  - Invisible leader line does not produce an orphan dogleg
  - Dogleg breaks split the dogleg segment
  - Multiple leader roots render shared content once
  - Direction fallback points dogleg toward content
  - Custom arrow block is inserted and rendered
  - Block content attribute overrides apply
  - Nested in INSERT applies parent transform

### Integration tests (recommended next)

- Parse real DXF/DWG with MULTILEADER(s) and compare rendered PDF to an oracle (AutoCAD or trusted renderer), including:
  - real-world MLEADERSTYLE permutations
  - multiple breaks and multiple leader lines per root
  - annotative multileaders (multiple context datas)

### Test DXF generation (for manual oracle work)

```python
import ezdxf

doc = ezdxf.new(setup=True)
msp = doc.modelspace()

msp.add_multileader_mtext(
    style="Standard",
    content="Test Leader",
    target_point=(100, 100),
    landing_point=(150, 120),
    insert_point=(200, 120),
)

doc.saveas("test_mleader.dxf")
```

## Dependencies

### Depends on

- **Stage 00 (Render Infrastructure)**: scene graph node types, transforms, property resolution, render log
- **Stage 01 (INSERT / Blocks)**: block expansion for block content + custom arrow blocks
- **Stage 02 (TEXT / MTEXT)**: MTEXT layout engine for text content

### Enables

- None directly, but MULTILEADER is a core annotation entity required for “prod-ready” CAD plotting.

## External reference code

- atlight article + DXF specimen breakdown: https://atlight.github.io/formats/dxf-leader.html
- GDAL reference implementation: https://github.com/OSGeo/gdal/blob/master/ogr/ogrsf_frmts/dxf/ogrdxf_leader.cpp
- ezdxf tutorial: https://ezdxf.readthedocs.io/en/stable/tutorials/mleader.html#tut-mleader
- ezdxf internals: https://ezdxf.readthedocs.io/en/stable/dxfinternals/entities/mleader.html#mleader-internals
- Local snapshots (fetched 2026-02-17): `specs/external/ezdxf-mleader-tutorial.md`, `specs/external/ezdxf-mleader-internals.md`
