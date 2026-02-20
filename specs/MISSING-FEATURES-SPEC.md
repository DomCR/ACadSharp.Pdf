# Missing Features Spec: Oracle-Driven Rendering & Implementation Plan

Below is a methodology and plan that truly "scales" for an AI-coding agent: **you build a reference oracle on AutoCAD/accoreconsole**, perform a **deterministic diff**, and then **add entity support in ACadSharp.Pdf as translation to primitives** (path/text/image) with automatic validation.

The target is **prod-ready DXF/DWG → PDF + PNG/BMP**, closing the list: HATCH, TEXT/MTEXT/ATT (fonts/styles/multiline), DIMENSION, INSERT, UNDERLAY(PDF/IMAGE), MLINE, MULTILEADER, RAY, TOLERANCE, XLINE — these are exactly the entities marked as "Missing" in ACadSharp.Pdf.

The current extension point in the library is `PdfPen.DrawEntity(...)` with a type-switch, where almost everything from the list is not implemented.

---

## 1) Core Idea: "Oracle-Driven Rendering" + Strict Automatic Validation

### What constitutes a "reliable" check
1. **Numerical metric on raster comparison** (SSIM/PSNR + fraction of differing pixels + "heatmap"), not "LLM opinion."
2. **Determinism**: identical viewport/window, DPI, background, lineweights, CTB, text substitutions.
3. **Reference versioning**: reference = specific AutoCAD version (and specific font set). This matters: different versions can render text/hatch slightly differently.

LLM-vision is super useful, but **as a classifier/diagnostician**, not as a "judge." It answers "what broke" and "which entity type is to blame," while pass/fail is decided by metrics.

---

## 2) Rendering Architecture That Simplifies Feature Addition

To avoid "weaving each entity's logic into PDF strings," a normalization layer is needed:

### Layers
**A. DXF/DWG → Scene Graph (primitives)**
Translation of each entity into a set of primitives:
- `Path` (lines/arcs/splines/polylines) + stroke/fill
- `TextRun` (text with font/size/alignment/rotation)
- `Image` (bitmap/underlay)
- `Clip` (clipping region)
- `Group`/`Transform` (for INSERT/blocks)

**B. Scene Graph → Backend**
- Backend PDF (your current PdfPen / or alternative backend)
- Backend Raster (PNG/BMP) — optional, but convenient for debugging.

> Why this is critical: all your "complex" entities (DIMENSION, MULTILEADER, MLINE, HATCH) are almost always **compositions of primitives**. You write *one* translator "entity → primitives" and reuse it for both PDF and PNG.

---

## 3) "Elegant Simplification" via Interop: 3 Approaches (can be combined)

### Approach 1 (most practical): AutoCAD as **oracle + preprocessor**
You can already use `accoreconsole`, which means you can:
- produce a **reference render** (PDF/PNG) for tests,
- and (very useful) produce an **"exploded DXF"** for diagnostics.

The exploded DXF idea: run commands like `EXPLODE` / disassociate / explode-to-primitives (where possible) and save the result.
Then you can:
- compare your render not only with the "final PDF" but also with the "primitive geometry after explode" — this often dramatically simplifies understanding of what exactly needs to be implemented in code.

This **does not replace** feature implementation (because explode doesn't always produce a correct analog), but it speeds up debugging enormously.

### Approach 2: Python interop as test generator + "reference geometry"
The Python package `ezdxf` (MIT) is excellent for:
- generating a huge number of minimal DXF cases,
- parametric sweeps (different styles/scales/angles/alignments),
- partial explode/flatten at the DXF level.

`ezdxf` is under MIT license.

This is a good source of algorithms/ideas (and legally compatible with an MIT project), unlike LibreCAD/QCAD (GPL).

Integration: the simplest approach is to **not embed Python in runtime**, but keep it as a CLI utility in the test pipeline (or as a separate "test-synthesizer" service).

### Approach 3: Native/ready-made libraries for narrow subtasks
To avoid writing a "geometry kernel" and "text engine" from scratch:
- **Polygon clipping/offset** (for HATCH/MLINE): Clipper2 (Boost license) — very convenient and fast.
- **PDF underlay rasterization**: PDFium/MuPDF via .NET wrappers (practically unavoidable if PDF underlay is needed)
- **Text shaping/metrics**: Skia/Harfbuzz/FreeType (or at least proper TTF metrics). Otherwise MTEXT/DIM will constantly "drift."

---

## 4) Automatic Validation: Reference + Diff + Report + LLM-Vision (full loop)

### Pipeline for a single test case
1. **Generate reference** (AutoCAD):
   - `accoreconsole.exe /i input.dwg /s plot.scr` → `ref.pdf`
2. **Render by ACadSharp.Pdf**:
   - your utility/test → `test.pdf`
3. **Rasterize both PDFs**:
   - `ref.pdf` → `ref.png` (fixed DPI, e.g., 300 or 600)
   - `test.pdf` → `test.png`
4. **Normalize images**:
   - same size (or crop by content bbox),
   - same background (usually white),
   - optionally convert to grayscale.
5. **Compare**:
   - SSIM (structural similarity),
   - % of "mismatched" pixels after thresholding,
   - heatmap diff,
   - optional: component analysis (bounding boxes of differing regions).
6. **Generate report artifacts**:
   - side-by-side (ref/test/diff),
   - JSON with metrics,
   - log of "which entities were encountered, which were rendered/skipped."
7. **LLM-vision (not a gate!)**:
   - input: ref/test/diff + overlay with diff bboxes + list of entities in DXF,
   - output: classification (e.g.: "MULTILEADER missing", "DIM text offset", "HATCH pattern angle wrong", "missing font substitution"),
   - this is returned to the agent as a "debug hint."

### Why this works with an agent
An agent needs:
- **a specific regression** (test case),
- **a specific success signal** (metric passes threshold),
- **diagnostics** (diff image + LLM hint).

This loop usually converges.

---

## 5) Feature Implementation Plan for ACadSharp.Pdf (dependency-driven)

Important dependency: **DIMENSIONS / MULTILEADER / TOLERANCE heavily depend on TEXT/MTEXT and INSERT**. Therefore the order is critical.

### Milestone 0 — "Render Infrastructure"
1. Introduce a **Scene Graph / Render Commands** (even if internal).
2. Unified transformation math + clipping (viewport/layout).
3. Detailed log: "entity type → rendered/skipped + bbox."

### Milestone 1 — INSERT (BlockReference) + ATTRIB/ATTDEF
**Goal:** correctly expand blocks and attributes.
- Recursive rendering of `BlockRecord` contents with the INSERT matrix.
- Resolve `ATTRIB` value from the insertion (not the template from ATTDEF).
- Guard against recursion/cycles.

This directly contributes to MULTILEADER (block content) and DIM (arrows/symbols are often blocks/fonts).

### Milestone 2 — TEXT / MTEXT / ATT (the most "painful" part)
**Goal:** achieve stable metrics and correct positioning.
Minimum "prod-level":
- TEXT: alignment (left/center/right + baseline/middle/top), rotation, oblique, width factor.
- ATT: same as TEXT, but value is taken from INSERT.
- MTEXT:
  - `\P` line break,
  - wrap by width,
  - basic inline formats (height/bold/italic — at least partially),
  - multiline alignment.

The main problem is **fonts/SHX**. Quick "prod-compromise":
- substitution policy: "if SHX — map to nearest TTF (Arial/ISOCP/...)"
- fix the font set in the test environment.

### Milestone 3 — DIMENSIONS
DIM consists of:
- geometry (extension lines, dim line, arrowheads),
- value text + format,
- placement rules (inside/outside, fits, overrides).

Input data: `Dimension` entity + `DimStyle`.

Strategy:
- implement by type: Linear/Aligned → Angular → Radius/Diameter → Ordinate.
- rely on the text engine from Milestone 2.
- in tests, use "single dimension per file" + parameter sweeps.

### Milestone 4 — MULTILEADER
MULTILEADER is complex as a data structure (and even in the ACadSharp ecosystem it's acknowledged as "confusing").
Plus there are nuances: dogleg, landing, different arrow types, content (text/block), annotative scales.

Strategy:
1. "MVP support": leader lines + arrowhead + landing + MTEXT content.
2. Then: block content, multiple leaders, style overrides.
3. Separately: annotativity (scale).

As a reference/ideas, existing structure breakdowns + demo DXFs and MULTILEADER conversion code (e.g., for GDAL/OGR) can be used.

### Milestone 5 — HATCH
HATCH = boundary + (solid | pattern | gradient).
- Solid: polygon fill (important: holes/islands, winding rules).
- Pattern: generate pattern lines and **clip to boundary** (Clipper2 is very fitting here).
- Gradient can be deferred or approximated.

### Milestone 6 — UNDERLAY (PDF/IMAGE)
- IMAGE: load bitmap, transform (matrix), clipping, transparency.
- PDF UNDERLAY:
  - rasterization is mandatory (PDFium/MuPDF),
  - cache pages/results,
  - apply insertion matrix.

### Milestone 7 — MLINE
MLINE is essentially "several parallel lines + joins + caps."
Needed:
- offset algorithm for polyline,
- join/cap types,
- MLineStyle.

Clipper2 can also help (offsetting).

### Milestone 8 — RAY / XLINE
- These are infinite lines → convert to segments by intersecting with the current clip-rect/viewport extents.
- Important: correctly account for rotation/layout.

### Milestone 9 — TOLERANCE
TOLERANCE (feature control frame) = frame/table + text layout (often stacked).
Depends on MTEXT-like layout.

---

## 6) How to Build an "Ideal" Test Dataset (so the agent actually delivers)

### 6.1 Minimal tests ("unit drawings")
For each entity type — **a set of DXFs where the sheet contains only 1–3 objects** all in a fixed window (e.g., `0,0` → `1000,1000`), so that the plot is always identical.

### 6.2 Parametric sweeps
A script (Python/ezdxf) generates 100–500 variants:
- TEXT: all justify + rotation + height + widthFactor
- MTEXT: wrap widths + \P + different alignments
- DIM: types + overrides + decimal precision + arrow sizes
- HATCH: 5–10 patterns × angles × scale
- MLEADER: variants of landing/dogleg + block/text content

### 6.3 Real "regression corpus"
A separate folder with real drawings (anonymized) that cover edge cases.

---

## 7) How to Integrate LLM-Vision into the Loop (reliably, not "magic")

### What LLM does well
- Classification: "which feature is missing/broken"
- Localization: "where on the sheet is the difference"
- Hypotheses: "looks like wrong baseline / incorrect text bbox / forgot to account for rotation"
- Generating a "minimal case" (and then you validate it via AutoCAD)

### What LLM should NOT decide
- pass/fail of a test
- "correctness" of numerical parameters (only metrics + reference)

### Practical prompt format for the agent
Give the agent a structured package:
- `ref.png`, `test.png`, `diff.png`
- JSON: metrics + list of entities in DXF (by type) + log of "what was skipped"
- "render overlay": image with diff bboxes and/or entity bboxes
- reference/path to the DXF

And ask the LLM to respond **strictly in JSON** (type, suspicion, location, next_action).

---

## 8) Licenses: Important Practical Note for "agent takes code from anywhere"
If you add code from GPL projects to an MIT project — legally this is almost always a problem.

- `ezdxf` — MIT (safe donor of ideas/algorithms).
- LibreCAD — GPLv2.
- QCAD CE — GPLv3 (with exceptions for plugins).
- Clipper2 — Boost Software License (permissive).

In practice: **allow the agent to "learn" from GPL, but forbid copy-paste** (only reproduce the idea/algorithm in your own words/code), or accept GPL obligations in advance.

---

## 9) Concrete "First Sprint" (to quickly get a working loop)

### Days 1–2: Harness + reference
- CLI `render_ref` (accoreconsole) → `ref.pdf`
- CLI `render_test` (ACadSharp.Pdf) → `test.pdf`
- rasterize both → PNG
- diff + report + JSON

### Days 3–4: INSERT + basic TEXT
- INSERT (no dynamic blocks), recursion guard
- TEXT: baseline/rotation/align (multiline not yet)

### Days 5–7: MTEXT MVP + first DIMs
- MTEXT: `\P` + wrap by width
- DIM Linear/Aligned minimally

At this point you already have:
- automatic metrics,
- diff artifacts,
- the agent can iterate endlessly until convergence.

---

## 10) Important "Pragmatic Compromise" If You Need Prod-Ready Right Now
If the business needs it "yesterday":
- build a **dual-mode converter**:
  1. `fast_path`: ACadSharp.Pdf (cross-platform, cheap)
  2. `fallback_oracle`: accoreconsole (when unsupported entities are detected)

And then gradually replace the fallback as features are implemented (your test corpus will show progress).

---

## 11) Reference Implementations: Where to Look for Each Missing Feature

You can "peek at implementations" from several very useful donors of **ideas/algorithms** (and sometimes nearly ready data models) that map well to rewriting in C#.

### Extension point in ACadSharp.Pdf

In the current ACadSharp.Pdf the central point where entities are added is `PdfPen.DrawEntity(...)` (type-switch on Entity). For the missing types it's effectively "not implemented," so the easiest approach is to build separate "renderers" and plug them into this switch.

---

### 11.1) MULTILEADER / LEADER — where to look

#### A) GDAL/OGR DXF driver (C++, MIT/X-style license)
This is arguably the **best "reference code"** for the use case: it has real DXF parsing for **LEADER/MULTILEADER**, and many DXF entities in one place.

- `ogrdxf_leader.cpp` (LEADER/MULTILEADER) — [GitHub: GDAL ogrdxf_leader.cpp](https://github.com/OSGeo/gdal/blob/master/ogr/ogrsf_frmts/dxf/ogrdxf_leader.cpp)
  In the same directory sit `ogrdxf_dimension.cpp`, `ogrdxf_hatch.cpp`, etc. — all missing types in one set of files.

Plus the author has a detailed write-up and demo DXFs for leaders:
- "Demystifying DXF: LEADER and MULTILEADER..." — [atlight.github.io/formats/dxf-leader](https://atlight.github.io/formats/dxf-leader.html) — with links to C++ code and test DXFs.

#### B) ezdxf (Python, MIT) — structure and test examples
- MultiLeader documentation + typical components (landing/dogleg, leader, MTEXT/BLOCK content) — [ezdxf MultiLeader tutorial](https://ezdxf.readthedocs.io/en/stable/tutorials/mleader.html)
- Example code for mleader case (great as a test generator/minimal case) — [GitHub: mtext_quick_leader.py](https://github.com/mozman/ezdxf/blob/master/docs/source/tutorials/src/mleader/mtext_quick_leader.py)
- General: their drawing-addon is built as "entity → primitives → backend," which is exactly the ideal pattern — [DeepWiki: ezdxf drawing and rendering system](https://deepwiki.com/mozman/ezdxf/7-drawing-and-rendering-system)

> Important: ezdxf itself honestly states that MLEADER is a complex and "variable" object, so you still need the oracle-diff with AutoCAD (as planned).

---

### 11.2) HATCH — where to look

#### A) ezdxf.render.hatching (MIT) — ready-made pattern logic as "lines + clip"
- Module documentation `ezdxf.render.hatching` (high-level functions, returns pattern lines) — [ezdxf hatching docs](https://ezdxf.mozman.at/docs/render/hatching.html)
- Example `hatch_from_entities.py` (very useful as "how to glue it into renderer") — [GitHub: hatch_from_entities.py](https://github.com/mozman/ezdxf/blob/master/examples/render/hatch_from_entities.py)
- Their notes on "polygon nesting / holes" — this is exactly what breaks 80% of solid hatch and clipping implementations — [DeepWiki: polygon nesting and hatch boundaries](https://deepwiki.com/mozman/ezdxf/6.2-polygon-nesting-and-hatch-boundaries)

#### B) GDAL: `ogrdxf_hatch.cpp` (MIT/X-style)
Ideal if you want to keep everything "without Python" and read C++ algorithms alongside the DXF parser. (The file sits next to leader/dimension in the same directory.)

---

### 11.3) DIMENSIONS / TOLERANCE — where to look

#### A) GDAL: `ogrdxf_dimension.cpp` (MIT/X-style)
Again — everything next to the other DXF entities, convenient to read as "how they interpret it." — [GitHub: GDAL ogr/ogrsf_frmts/dxf/](https://github.com/OSGeo/gdal/blob/master/ogr/ogrsf_frmts/dxf/ogrdxf_leader.cpp)

#### B) ezdxf drawing system (MIT) — decomposition into primitives
They do it very clearly: the frontend "resolves properties + decomposes entities" and then backends draw. This is exactly the layer you need in C# — [DeepWiki: ezdxf drawing and rendering system](https://deepwiki.com/mozman/ezdxf/7-drawing-and-rendering-system)

---

### 11.4) TEXT / MTEXT / ATT (fonts, styles, multiline) — where to look

For "correct" text, two things usually matter: (1) parsing MTEXT formatting/wrapping, (2) metrics/bbox calculation.

The best thing to study is not "how to output text in PDF" but **how to build the layout layer**:
- ezdxf drawing-addon as architecture "frontend → primitives → backend" (supports different backends but unified entity logic) — [DeepWiki: ezdxf drawing and rendering system](https://deepwiki.com/mozman/ezdxf/7-drawing-and-rendering-system)

Idea: in C# you build your own `TextLayoutEngine`, and the PDF part stays thin.

---

### 11.5) INSERT / ATTRIB + UNDERLAY (IMAGE/PDF)

#### INSERT / ATTRIB
- QCAD dxflib lists support for `INSERT`, `HATCH`, `IMAGE`, `RAY`, `XLINE`, `DIMENSION`, `TEXT`, etc. — a good reference for "which entities have ready parsers/models." But dxflib is **GPL/commercial license** (cannot copy code into a proprietary product; studying ideas is fine) — [qcad.org dxflib](https://www.qcad.org/en/90-dxflib)

#### UNDERLAY (PDF)
Here you almost always need an **external PDF rasterization library** (PDFium/MuPDF) — because "PDF underlay" = embedded PDF content that needs to be rendered into a bitmap and then placed into your PDF as an image with a matrix/clip.

---

### 11.6) MLINE / RAY / XLINE — where to look
- ezdxf documents MLINE/MULTILEADER as "complex annotation entities" and keeps them in the general rendering system — [DeepWiki: complex annotation entities](https://deepwiki.com/mozman/ezdxf/4.5-complex-annotation-entities)
- QCAD dxflib explicitly declares support for `RAY` and `XLINE` (again: study ideas, but do not copy GPL code) — [qcad.org dxflib](https://www.qcad.org/en/90-dxflib)

---

### Practical "Rule of Thumb" for Donors

| Want | Source | License |
|------|--------|---------|
| Code whose ideas/structure can be freely transferred | **GDAL** (MIT/X-style), **ezdxf** (MIT) | Permissive |
| See how a full CAD does it | **QCAD/dxflib** | GPL/commercial — study only, no copy-paste |

---

### Target C# Modules (checklist for the AI agent)

For each feature, the following C# modules should emerge:

| Feature | Module Name | Key Responsibility |
|---------|-------------|--------------------|
| HATCH | `HatchPatternGenerator` | Generate pattern lines, clip to boundary, solid fill with holes |
| MULTILEADER | `MLeaderDecomposer` | Decompose MLEADER into leader lines + arrowheads + landing + MTEXT/block content |
| DIMENSION | `DimLayoutEngine` | Compute extension lines, dim line, arrowheads, text placement per DimStyle |
| UNDERLAY | `UnderlayRasterCache` | Rasterize PDF/image underlays, cache, apply insertion matrix |
| TEXT/MTEXT | `TextLayoutEngine` | Parse MTEXT formatting, compute text bbox, handle font substitution |
| INSERT | `BlockExpander` | Recursively expand BlockRecords with INSERT matrix, resolve ATTRIBs |
| MLINE | `MLineOffsetRenderer` | Generate parallel offset lines with join/cap types per MLineStyle |
| RAY/XLINE | `InfiniteLineClipper` | Intersect infinite lines with viewport extents to produce segments |
| TOLERANCE | `ToleranceFrameRenderer` | Compose feature control frame as table/boxes + stacked text |
