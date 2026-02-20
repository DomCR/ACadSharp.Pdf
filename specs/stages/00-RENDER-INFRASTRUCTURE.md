# Stage 00: Render Infrastructure — Scene Graph / Render Commands Layer

## Overview

Introduce a rendering intermediate representation (IR) between DXF/DWG entities and output generation. Instead of each entity directly emitting PDF operators through `PdfPen`, entity renderers produce a small set of backend-agnostic primitives (Path, TextRun, Image, Clip, Group) plus fully resolved style. A backend then serializes this IR to PDF (and optionally a raster/debug backend later).

This is the foundational stage. Stages 01–09 should not duplicate:
- transformation logic (OCS/WCS, INSERT, viewport/model→paper)
- property resolution (color/lineweight/linetype/visibility)
- output-format details (PDF operator strings, escaping, etc.)

The current extension point is `PdfPen.DrawEntity(...)` with a type switch. Stage 00 should be implemented **alongside** the existing pipeline behind a feature flag to enable A/B comparison and incremental migration.

### Non-goals (Stage 00)

- Accurate SHX font rendering and full MTEXT formatting (Stage 02).
- Complex linetypes with embedded shapes/text (explicit fallback policy + logging is sufficient).
- Full 3D viewport projection (support orthographic top-view first; log unsupported view configurations).

---

## Conventions (Make These Explicit and Keep Them Consistent)

### Coordinate spaces

Use these names consistently:

- **OCS**: Object Coordinate System (entity-local), defined by extrusion/normal (group 210/220/230).
- **WCS**: World Coordinate System.
- **Model space**: WCS for model entities (drawing units).
- **Paper space**: layout sheet coordinates (paper units: mm/inches/pixels).
- **PDF user space**: points (1/72 inch).

### Units (geometry vs style)

CAD mixes “geometry in drawing units” with “lineweight in paper mm”.

Rules to bake into Stage 00:

- **Positions/geometry** are transformed through model→paper→PDF.
- **Lineweight** is a paper width (hundredths of mm): it should **not** scale with viewport/INSERT scaling.
- **Linetype pattern lengths** are lengths in drawing units: they **should** scale with geometric scaling (viewport scale, block scale, etc.).

### Matrix math (align with the codebase)

The repo already uses `CSMath.Matrix4` and `CSMath.Transform`. Stage 00 should reuse these to avoid mixing conventions with `System.Numerics.Matrix4x4`.

Document (and test) these invariants:
- points are transformed by the existing operator: `XYZ p2 = matrix * p1;`
- transform composition follows the existing `CSMath.Transform` behavior (currently built as `T * R * S` in `Transform.updateMatrix()`).
- angles in ACadSharp entities are commonly exposed as **radians** when tagged with `DxfReferenceType.IsAngle` (e.g., ARC start/end angles, TEXT rotation). Keep IR angles in radians unless there is a compelling reason not to.

If a future refactor adopts `System.Numerics`, Stage 00 should add a compatibility layer and tests proving equivalence before switching.

---

## Domain Knowledge

### Render primitives (IR)

Every complex entity decomposes into a small set of visuals:

| Primitive | Description |
|-----------|-------------|
| **Path** | 2D path of lines + curves (cubic Béziers; arcs may be carried or normalized to Béziers). Has stroke (color/width/dash/caps/joins) and optional fill (solid for Stage 00). |
| **TextRun** | Single run of text with font, font size, anchor/alignment, rotation, oblique (shear), width factor, and color. Stage 02 owns line breaking and MTEXT formatting. |
| **Image** | Bitmap plus placement transform and optional clip. |
| **Clip** | Clip path restricting children (viewport clipping, IMAGE clipping). |
| **Group** | Container with transform and (later) a property-inheritance scope (ByBlock) for block expansion. |

#### Suggested IR shapes (sketch)

Keep the IR immutable and unit-explicit (at least in naming):

```csharp
namespace ACadSharp.Pdf.Core.Render;

using CSMath;

abstract record RenderNode(ulong SourceHandle);

sealed record PathNode(
    ulong SourceHandle,
    IReadOnlyList<PathSegment> Segments,
    StrokeStyle? Stroke,              // linewidth + dash in PDF points
    FillStyle? Fill                   // solid fill for Stage 00
) : RenderNode(SourceHandle);

abstract record PathSegment;
sealed record MoveTo(XY Point) : PathSegment;
sealed record LineTo(XY Point) : PathSegment;
sealed record CubicTo(XY C1, XY C2, XY End) : PathSegment;
sealed record Close() : PathSegment;

sealed record TextRunNode(
    ulong SourceHandle,
    string Text,
    string FontName,
    double FontSizePt,
    XY AnchorPt,
    double RotationRad,
    double ObliqueRad,
    double WidthFactor,
    ACadSharp.Color Color,
    TextAlignment HAlign,
    TextVAlignment VAlign
) : RenderNode(SourceHandle);

sealed record GroupNode(
    ulong SourceHandle,
    Matrix4 Transform,
    IReadOnlyList<RenderNode> Children
) : RenderNode(SourceHandle);

sealed record ClipNode(
    ulong SourceHandle,
    PathNode ClipPath,
    IReadOnlyList<RenderNode> Children
) : RenderNode(SourceHandle);

sealed record StrokeStyle(
    ACadSharp.Color Color,
    double WidthPt,
    IReadOnlyList<double> DashArrayPt,
    double DashOffsetPt
);

sealed record FillStyle(ACadSharp.Color Color);
```

### OCS→WCS (Arbitrary Axis Algorithm)

DXF entities define OCS via an extrusion/normal vector (210/220/230, default (0,0,1)). The Arbitrary Axis Algorithm constructs an orthonormal basis:

```
Given N = (Nx, Ny, Nz):
  threshold = 1/64 = 0.015625
  if |Nx| < threshold AND |Ny| < threshold:
    Ax = normalize(cross((0,1,0), N))
  else:
    Ax = normalize(cross((0,0,1), N))
  Ay = normalize(cross(N, Ax))
```

For N=(0,0,1), this is identity. For N=(0,0,-1), it mirrors/rotates the plane and reverses winding.

### Color resolution (correct DXF group codes)

DXF uses:
- **62**: ACI (indexed color, including 0=ByBlock, 256=ByLayer)
- **420**: TrueColor (24-bit RGB)
- **60**: Visibility flag (this is not color)

Resolution priority:
1. TrueColor (420) if present → RGB.
2. ACI 1–255 → palette lookup (ACadSharp’s `Color` already contains the palette).
3. ByLayer (256) → `entity.Layer.Color`.
4. ByBlock (0) → containing INSERT / block defaults (Stage 01).

Special case:
- ACI 7 is “black/white depending on background”. For PDF export on white page, map to black (existing `PdfPen.applyStyle()` does this).

Note: `Entity.GetActiveColor()` exists in ACadSharp but is not a complete nested-INSERT resolver; Stage 00 should define a deterministic resolver and log decisions.

### Lineweight resolution (paper width)

Lineweight is `LineWeightType` (group 370) with sentinel values:

| Value | Meaning |
|-------|---------|
| -4 | `ByDIPs` (policy-driven width; log and pick a deterministic mapping) |
| -3 | Default (`$LWDEFAULT`) |
| -2 | ByBlock |
| -1 | ByLayer |
| 0..211 | Hundredths of mm (e.g., 25 → 0.25mm) |

PDF linewidth uses points: `points = mm * 72 / 25.4`.

Key rule:
- Lineweight should remain constant in paper/PDF units even when geometry is scaled by viewports or INSERT.

### Linetype patterns (drawing-unit lengths)

`LineType.Segments` define a repeating dash pattern:
- positive length → dash
- negative length → gap
- zero length → dot (usually approximated as a very short dash ≈ line width)

Scaling:
- global `LTSCALE` * per-entity `entity.LineTypeScale` (group 48)
- geometric scaling (viewport scale, block scale, etc.) must affect effective dash lengths

Complex linetypes:
- `LineType.HasShapes == true` means embedded shape/text segments.
- Stage 00 should pick and log an explicit fallback policy: continuous, or expensive geometric expansion via `LineType.CreateLineTypeShape(...)`.

### Visibility & layers (match ACadSharp model)

Skip rendering if:
- `entity.IsInvisible == true` (group 60)
- layer off: `layer.IsOn == false` (and DXF can also encode off by negative ACI)
- layer frozen: `layer.Flags.HasFlag(LayerFlags.Frozen)`
- layer not plottable: `layer.PlotFlag == false` (e.g., `defpoints`)
- viewport-specific freeze: the active viewport’s `FrozenLayers` contains the layer

Locked layers (`LayerFlags.Locked`) can be rendered normally in Stage 00; optionally log for later fading support.

### Viewport & layout mapping

DXF has model space and paper space. A viewport is a window in paper space that views model space.

In ACadSharp:
- `Viewport.ScaleFactor` is `Height_paper / ViewHeight_model`.
- `Viewport.GetModelBoundingBox()` is currently axis-aligned and does not incorporate twist/view direction.

Stage 00 should implement viewport rendering as:
- a clip region in paper space (rectangle first)
- a model→paper transform (scale + translate first; twist optional, but must be logged if ignored)

### DXF→PDF unit conversion (existing behavior)

Current coordinate conversion (`PdfPen.toPdfDouble`) does:
1. divide by `Layout.DenominatorScale`
2. convert from `Layout.PaperUnits` to PDF points

Stage 00 should encode the same behavior in a reusable mapper and keep units explicit to avoid accidental double conversion.

---

## Output backend constraints (PDF)

- PDF has no native arc operator; arcs must be approximated (usually with cubic Béziers).
- PDF dash arrays must be valid (avoid all-zero/negative entries; approximate dot segments).
- PDF string literals require escaping `\`, `(`, and `)`.
- If you rely on PDF `cm` scaling for geometry, you will also scale line widths and dash arrays. Because CAD lineweight is a paper width, Stage 00 should **prefer flattening geometry to PDF points in code** and emitting identity/translation-only CTM in PDF where possible.

---

## Step-by-step implementation plan

### Step 1: Define the render IR types

**What**: Add new types under `ACadSharp.Pdf/Core/Render/` for nodes, segments, and styles.

**Output**: A minimal, immutable IR with unit-explicit fields.

**Edge cases**:
- filled paths must be closed
- decide whether arcs are normalized to cubics or carried as first-class segments

---

### Step 2: Implement transform + mapping helpers

**What**: Add a single, shared place for transform construction (suggested: a static `TransformHelper` in `ACadSharp.Pdf/Core/Render/Transforms/`) that produces `CSMath.Matrix4` and handles unit conversion consistently.

**Key functions**:
- `OcsToWcs(normal)` (thin wrapper over `Matrix4.GetArbitraryAxis(normal)`)
- `ViewportModelToPaper(viewport)` (Stage 00: orthographic top-view; twist optional)
- `PaperToPdf(layout)` (DenominatorScale + PaperUnits)
- `CreateShearXByY(shear)` (needed for TEXT oblique handling in Stage 02)
- `ImagePixelToWcs(insertPoint, uVector, vVector)` (needed for IMAGE/UNDERLAY placement in Stage 06)

**Edge cases**:
- (0,0,-1) normal flips winding
- guard degenerate transforms (zero scales)

---

### Step 3: Implement a property resolver (style + visibility)

**What**: Centralize resolution of:
- color (TrueColor/ACI/ByLayer/ByBlock)
- lineweight (final points, paper width)
- linetype (dash arrays in points, scaled with geometry)
- entity visibility + layer/vp filters

**Output**: `ResolvedStyle` + `VisibilityDecision` (for logging).

**Edge cases**:
- nested ByBlock chains (INSERT inside INSERT)
- layer `0` behavior inside blocks (Stage 01 dependency; document expectations now)
- complex linetypes policy must be deterministic and logged

---

### Step 4: Implement the entity frontend (dispatcher)

**What**: Translate supported entities into IR primitives using:
- property resolver
- transform helpers (OCS→WCS + viewport/model mapping)

Stage 00 parity targets (match `PdfPen`):
- `Line`, `Arc`, `Circle`, `Ellipse`, `Point`, `IPolyline`, simple `TEXT`
- treat `Viewport` as layout coordination (build a `ClipNode` + `GroupNode` around model entities)

**Edge cases**:
- degenerate geometry (zero-length line, zero-radius circle): skip with log
- extremely large coordinates: log a warning (do not silently overflow)

---

### Step 5: Implement a scene flattener (recommended)

**What**: Convert hierarchical IR (`GroupNode` transforms, `ClipNode`) into a flat sequence of draw commands in **PDF points**.

Why: It prevents PDF CTM scaling from unintentionally scaling lineweight, and it keeps style units unambiguous.

**Output**: `IReadOnlyList<FlatDrawCommand>` where each command contains:
- already-transformed path points in PDF points
- stroke/fill already in PDF points/colors
- clip regions already applied or emitted as explicit clip operators

---

### Step 6: Implement the PDF backend

**What**: Serialize flattened commands to PDF operators.

Must handle:
- `m`, `l`, `c`, `h` path construction
- `S`, `f`/`f*`, `B`/`B*` painting choices
- text blocks (`BT`/`ET`) with rotation/shear via text matrix
- clipping (`W n`) when flattening chooses to emit explicit clip scopes

**Edge cases**:
- dash arrays: avoid invalid patterns; approximate dots
- text escaping

---

### Step 7: Implement the render log

**What**: Record a log entry per entity:
- entity type + handle
- rendered/skipped/not-implemented + reason
- optional bounds in PDF points

This log is a core artifact for oracle-driven validation and debugging.

---

### Step 8: Wire up the pipeline (feature flag)

**What**:
- add `PdfConfiguration.UseSceneGraph` (default `false`)
- when enabled: build IR → flatten → serialize; produce render log
- keep existing `PdfPen` pipeline for comparisons until parity is reached

---

## Testing strategy

### Unit tests

1. **Transform helper**
   - OCS→WCS: identity for (0,0,1)
   - OCS→WCS: correct handedness/flip for (0,0,-1)
   - arbitrary normals produce orthonormal basis
   - threshold behavior at 1/64

2. **Property resolver**
   - truecolor vs ACI precedence
   - ByLayer/ByBlock inheritance (including nested chains once Stage 01 exists)
   - ACI 7 mapping to black
   - lineweight conversion mm→points
   - visibility gates: `IsInvisible`, layer off/frozen/plotflag, viewport frozen layers

3. **Flattening**
   - group transforms affect geometry but do not scale lineweight
   - dash arrays scale with geometry scale (viewport scale) while lineweight remains constant

4. **PDF backend**
   - correct operator emission for simple paths
   - correct escaping for text literals

### Integration tests

- A/B parity: for a small DXF containing only the currently-supported entities, verify new pipeline output matches the legacy `PdfPen` output (or matches within a known-tolerance if `PdfPen` has known bugs).
- Viewport smoke test: layout with one viewport; verify clipping and placement visually and through bounds.

---

## Dependencies

### Depends on

- nothing (this is the first stage)

### Enables

- Stage 01 (INSERT/Blocks): Group/transform + ByBlock inheritance scope
- Stage 02 (TEXT/MTEXT): TextRun primitives + text measurement/layout
- Stage 03+ (DIMENSIONS, MULTILEADER, HATCH, …): consistent primitives and property resolution

### External dependencies

- none required beyond the existing ACadSharp/CSMath types already in the repo
