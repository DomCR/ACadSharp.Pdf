# Stage 05: HATCH

## Overview

HATCH entities fill enclosed regions with solid color, line patterns, or gradients. They are ubiquitous in technical drawings for representing material cross-sections (e.g., ANSI31 for steel), area fills, and visual differentiation between regions.

A HATCH consists of two parts: **boundaries** (the regions to fill) and **fill content** (solid, pattern, or gradient). Boundaries can be complex polygons with holes (islands), composed of lines, arcs, elliptical arcs, and splines. The pattern fill is the most complex part: each pattern is defined as a set of parallel line families, each at a specific angle and spacing, with optional dash/gap sequences. These infinite pattern lines must be clipped to the boundary polygons.

The Clipper2 library (Boost license) is the recommended tool for polygon clipping and is well-suited for clipping pattern lines against hatch boundaries.

The target module for this stage is `HatchPatternGenerator.cs`.

---

## Domain Knowledge

### Hatch Entity Structure

A HATCH entity contains:
- **Boundary paths** (one or more): Define the filled region
- **Pattern definition** (for pattern fill): Line families defining the pattern
- **Fill type** (group 70): 0 = Pattern fill, 1 = Solid fill, 2 = Gradient fill (if group 450 = 1)
- **Pattern name** (group 2): e.g., "ANSI31", "SOLID", "BRICK"
- **Pattern angle** (group 52): Rotation of the entire pattern
- **Pattern scale** (group 41): Scale factor for the pattern
- **Associative flag** (group 71): 1 = boundary linked to source entities, 0 = standalone
- **Hatch style** (group 75): 0 = Odd parity (normal), 1 = Outermost, 2 = Entire area

### Boundary Path Types

**PolylinePath** (flag bit 1 set):
- Sequence of vertices with optional bulge values
- Bulge != 0 indicates an arc segment between that vertex and the next
- Bulge = tan(included_angle / 4); positive = CCW, negative = CW
- Always forms a closed loop

**EdgePath** (flag bit 1 not set):
- Sequence of edge entities forming a closed loop
- Edge types:
  - **LineEdge**: start point, end point
  - **CircularArcEdge**: center, radius, start angle, end angle, is_counter_clockwise
  - **EllipticalArcEdge**: center, major axis endpoint, minor axis ratio, start angle, end angle, is_counter_clockwise
  - **SplineEdge**: degree, rational flag, periodic flag, knot values, control points, fit points, start/end tangents

### Boundary Classification and Island Detection

When a hatch has multiple boundary paths, they form a nesting hierarchy:

- **EXTERNAL** (outermost): The main boundary path
- **OUTERMOST**: Boundaries directly inside an EXTERNAL path (first level islands)
- **DEFAULT**: Boundaries inside OUTERMOST paths (second level and deeper)

The group 92 flags for each path include:
- Bit 0 (1): External flag
- Bit 1 (2): Polyline path
- Bit 2 (4): Derived flag
- Bit 3 (8): Textbox
- Bit 4 (16): Outermost flag

### Hatch Style (Island Detection)

The hatch style (group 75) determines how islands are handled:

| Value | Style | Behavior |
|-------|-------|----------|
| 0 | Normal (Odd parity) | Alternating fill: outer = filled, first island = empty, island-in-island = filled, etc. |
| 1 | Outer | Only the outermost region is filled (between EXTERNAL and OUTERMOST paths) |
| 2 | Ignore | All islands ignored, entire area filled (only EXTERNAL paths used) |

For **Normal** style, the nesting depth determines fill:
- Depth 0 (EXTERNAL): filled
- Depth 1 (OUTERMOST island): not filled (hole)
- Depth 2 (island inside island): filled
- Depth 3: not filled
- And so on...

### Pattern Definition

A hatch pattern consists of one or more **pattern lines**. Each line defines a family of parallel lines:

```
PatternLine:
  angle      - Line angle in degrees
  base_x     - X origin of the pattern line
  base_y     - Y origin of the pattern line
  offset_x   - X component of offset to next parallel line
  offset_y   - Y component of offset to next parallel line
  dashes[]   - Array of dash/gap lengths (positive=dash, negative=gap, 0=dot)
```

If the `dashes` array is empty, the line is continuous (solid).

**Example: ANSI31** (standard cross-hatch):
```
angle: 45, base: (0,0), offset: (-0.0884, 0.0884), dashes: []
```
This creates 45-degree continuous lines spaced at sqrt(0.0884^2 + 0.0884^2) = 0.125 units apart.

**Example: BRICK**:
```
Line 1: angle: 0,   base: (0,0),    offset: (0, 0.25), dashes: []
Line 2: angle: 90,  base: (0,0),    offset: (0.25, 0), dashes: [0.125, -0.125]
Line 3: angle: 90,  base: (0.25,0), offset: (0.25, 0), dashes: [-0.125, 0.125]
```

### Pattern Generation Algorithm

For each pattern line family:

1. **Compute line direction**: `direction = (cos(angle), sin(angle))`
2. **Compute offset direction**: The actual spacing direction is the offset vector `(offset_x, offset_y)`. The perpendicular distance between parallel lines is the component of the offset perpendicular to the line direction.
3. **Generate parallel lines**: Starting from the base point, generate parallel lines at the offset spacing until the entire bounding box of the boundary is covered.
4. **Apply dash pattern**: For each line, repeat the dash/gap pattern along its length.
5. **Clip to boundary**: Intersect each patterned line with the boundary polygons.

The pattern scale and angle from the HATCH entity modify the pattern:
- Scale: multiply all pattern dimensions (spacing, dash lengths) by the scale factor
- Angle: add the pattern angle to each pattern line's angle

### Solid Fill

For solid fill (fill type 1), the boundary paths are filled directly:
- Convert boundary paths to closed polygons (tessellate arcs/splines)
- For boundaries with holes: use even-odd or nonzero winding fill rule
- In PDF: use the `f*` operator (even-odd fill) or `f` (nonzero winding)
- DXF solid hatches use **nonzero winding** by convention, which naturally handles holes when inner boundaries have opposite winding direction

### Gradient Fill

Gradient fills (fill type 2 when group 450 = 1) define color gradients:
- One-color or two-color gradient
- Gradient type: linear, spherical, curved, etc.
- Gradient angle and center shift

Gradient fills can be deferred (rendered as solid fill with the primary color) or approximated with a series of colored strips.

### Fill Rules: Even-Odd vs Nonzero Winding

**Even-odd** (PDF `f*`): A point is inside if a ray from it crosses the boundary an odd number of times. Simple and handles most cases.

**Nonzero winding** (PDF `f`): Counts the direction of boundary crossings. A point is inside if the total winding number is non-zero. This naturally handles holes when inner boundaries wind in the opposite direction from outer boundaries.

For DXF HATCH with islands:
- Outer boundary: counter-clockwise
- First-level islands: clockwise (subtracted)
- Islands within islands: counter-clockwise (added back)

Use nonzero winding (PDF `f`) for solid fills with islands.

---

## External Reference Code

### ezdxf Hatching Module (MIT License)
- **URL**: https://ezdxf.mozman.at/docs/render/hatching.html
- **What to study**: High-level API for hatch pattern rendering. Key classes:
  - `HatchBaseLine`: Represents a single pattern line family (origin, direction, offset, dash pattern)
  - `PatternRenderer`: Generates infinite pattern lines and clips to polygons
  - `hatch_polygons()`: Generates pattern line geometry for polygon boundaries
  - `hatch_paths()`: Generates pattern line geometry for path boundaries

### ezdxf Hatching Source (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/render/hatching.py
- **What to study**: The complete algorithm for pattern line generation, clipping, and boundary handling. Pay attention to:
  - How the bounding box is computed for generating enough parallel lines
  - How dash patterns are applied along each line
  - How clipping against polygon boundaries works

### ezdxf Polygon Nesting (MIT License)
- **URL**: https://deepwiki.com/mozman/ezdxf/6.2-polygon-nesting-and-hatch-boundaries
- **What to study**: How boundary paths are classified into nesting levels and how island detection works. This is critical for correct solid fill with holes.

### ezdxf hatch_from_entities.py Example (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/examples/render/hatch_from_entities.py
- **What to study**: Practical example of how to create and render hatch patterns, showing the boundary-to-pattern pipeline.

### GDAL ogrdxf_hatch.cpp (MIT/X-style License)
- **URL**: https://github.com/OSGeo/gdal/blob/master/ogr/ogrsf_frmts/dxf/ogrdxf_hatch.cpp
- **What to study**: `TranslateHATCH()` function. GDAL converts hatch boundaries to OGR polygon geometry but does NOT render patterns (only boundary geometry). Useful for understanding boundary path parsing.

### Clipper2 Library (Boost License)
- **URL**: https://github.com/AngusJohnson/Clipper2
- **What to study**: Polygon boolean operations (intersection, union, difference) and open path clipping. For pattern hatching, use `ClipperD.Execute()` with open subject paths (pattern lines) and closed clip paths (boundaries). Clipper2 has a .NET port available on NuGet (`Clipper2Lib`).

### Standard Hatch Patterns (acad.pat)
- **URL**: Various sources document the standard AutoCAD hatch patterns
- **What to study**: The definitions for common patterns (ANSI31-37, SOLID, BRICK, EARTH, GRAVEL, etc.). Each pattern is a set of line family definitions.

---

## Step-by-Step Implementation Plan

### Step 1: Create HatchPatternGenerator Class

**What**: The main class for converting HATCH entities to render primitives.

**Key structure**:
```csharp
class HatchPatternGenerator
{
    private PropertyResolver _resolver;
    private RenderLog _log;

    List<RenderNode> RenderHatch(Hatch hatch, Matrix4 parentTransform)
    {
        // 1. Extract and tessellate boundary paths
        var boundaries = ExtractBoundaries(hatch);
        if (boundaries.Count == 0)
        {
            _log.Skip(hatch, "no valid boundaries");
            return empty;
        }

        // 2. Classify boundaries (nesting hierarchy)
        var nested = ClassifyBoundaries(boundaries, hatch.HatchStyle);

        // 3. Generate fill content
        switch (hatch.FillType)
        {
            case HatchFillType.Solid:
                return RenderSolidFill(nested, hatch, parentTransform);
            case HatchFillType.Pattern:
                return RenderPatternFill(nested, hatch, parentTransform);
            case HatchFillType.Gradient:
                return RenderGradientFill(nested, hatch, parentTransform);
        }
    }
}
```

**Input**: Hatch entity + parent transform.

**Output**: List of render primitives.

**Edge cases**:
- Hatch with no boundaries: skip
- Boundaries that do not form closed loops: attempt to close, log warning

---

### Step 2: Implement Boundary Path Extraction and Tessellation

**What**: Convert boundary paths from DXF representation to closed polygon point lists.

**Algorithm**:
```csharp
List<List<XY>> ExtractBoundaries(Hatch hatch)
{
    var boundaries = new List<List<XY>>();

    foreach (var path in hatch.BoundaryPaths)
    {
        var points = new List<XY>();

        if (path.IsPolyline)
        {
            // PolylinePath: vertices with optional bulge
            for (int i = 0; i < path.Vertices.Count; i++)
            {
                var v = path.Vertices[i];
                points.Add(new XY(v.X, v.Y));

                if (v.Bulge != 0)
                {
                    // Tessellate arc between this vertex and next
                    var next = path.Vertices[(i + 1) % path.Vertices.Count];
                    var arcPoints = TessellateArcFromBulge(v, next, v.Bulge);
                    points.AddRange(arcPoints);
                }
            }
        }
        else
        {
            // EdgePath: sequence of edges
            foreach (var edge in path.Edges)
            {
                switch (edge)
                {
                    case LineEdge line:
                        points.Add(new XY(line.Start.X, line.Start.Y));
                        break;
                    case CircularArcEdge arc:
                        points.AddRange(TessellateCircularArc(arc));
                        break;
                    case EllipticalArcEdge ellArc:
                        points.AddRange(TessellateEllipticalArc(ellArc));
                        break;
                    case SplineEdge spline:
                        points.AddRange(TessellateSpline(spline));
                        break;
                }
            }
        }

        if (points.Count >= 3)
            boundaries.Add(points);
    }

    return boundaries;
}
```

**Arc tessellation from bulge**:
```csharp
List<XY> TessellateArcFromBulge(XY start, XY end, double bulge)
{
    // bulge = tan(included_angle / 4)
    double includedAngle = 4 * Math.Atan(Math.Abs(bulge));
    double chordLength = Distance(start, end);
    double radius = chordLength / (2 * Math.Sin(includedAngle / 2));

    // Find center
    XY midpoint = (start + end) / 2;
    XY chordDir = Normalize(end - start);
    XY perpDir = bulge > 0 ? new XY(-chordDir.Y, chordDir.X) : new XY(chordDir.Y, -chordDir.X);
    double sagitta = radius - Math.Sqrt(radius * radius - (chordLength / 2) * (chordLength / 2));
    XY center = midpoint + perpDir * (radius - sagitta);

    // Tessellate arc from start to end
    double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
    double endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);

    return TessellateArc(center, radius, startAngle, endAngle, bulge > 0);
}
```

**Input**: Hatch boundary paths.

**Output**: List of closed polygon point lists.

**Edge cases**:
- Spline edges with insufficient data: degenerate to line between start/end
- Arc with very large radius (nearly straight): use fewer tessellation segments
- Boundary not closed (gap between last edge end and first edge start): add a closing line segment
- Boundary with self-intersections: proceed anyway (Clipper2 can handle these)

---

### Step 3: Implement Boundary Nesting Classification

**What**: Determine the nesting hierarchy of boundary paths for island detection.

**Algorithm**:
```csharp
List<NestingNode> ClassifyBoundaries(List<List<XY>> boundaries, int hatchStyle)
{
    // Sort boundaries by area (largest first = outermost)
    var sorted = boundaries.OrderByDescending(b => ComputeArea(b)).ToList();

    // Build containment tree using point-in-polygon tests
    var tree = new List<NestingNode>();

    foreach (var boundary in sorted)
    {
        var node = new NestingNode { Boundary = boundary };

        // Find the smallest existing node that contains this boundary
        NestingNode parent = FindContainingNode(tree, boundary);

        if (parent != null)
        {
            parent.Children.Add(node);
            node.Depth = parent.Depth + 1;
        }
        else
        {
            tree.Add(node);
            node.Depth = 0;
        }
    }

    // Apply hatch style to determine which regions are filled
    foreach (var node in AllNodes(tree))
    {
        switch (hatchStyle)
        {
            case 0: // Normal (odd parity)
                node.IsFilled = (node.Depth % 2 == 0);
                break;
            case 1: // Outer
                node.IsFilled = (node.Depth == 0);
                break;
            case 2: // Ignore
                node.IsFilled = true;
                break;
        }
    }

    return tree;
}

bool PointInPolygon(XY point, List<XY> polygon)
{
    // Ray casting algorithm
    bool inside = false;
    for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
    {
        if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
            (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) /
             (polygon[j].Y - polygon[i].Y) + polygon[i].X))
        {
            inside = !inside;
        }
    }
    return inside;
}
```

**Input**: Boundary polygons, hatch style.

**Output**: Nesting tree with fill/hole classification.

**Edge cases**:
- Overlapping boundaries at the same nesting level: treat as separate outer regions
- Boundary exactly touching (tangent): may cause ambiguity in containment test
- All boundaries at same level (no nesting): all are EXTERNAL

---

### Step 4: Implement Solid Fill Rendering

**What**: Render a solid fill hatch as filled polygons with hole handling.

**Algorithm**:
```csharp
List<RenderNode> RenderSolidFill(List<NestingNode> tree, Hatch hatch,
    Matrix4 parentTransform)
{
    var nodes = new List<RenderNode>();
    Color fillColor = _resolver.ResolveColor(hatch, null);

    // For each filled region: create a path with outer boundary + hole boundaries
    foreach (var region in GetFilledRegions(tree))
    {
        var path = new PathNode();

        // Outer boundary (counter-clockwise for nonzero winding)
        var outer = EnsureCCW(region.Boundary);
        AddPolygonToPath(path, TransformPoints(outer, parentTransform));

        // Hole boundaries (clockwise for nonzero winding)
        foreach (var hole in region.Holes)
        {
            var holePoints = EnsureCW(hole.Boundary);
            AddPolygonToPath(path, TransformPoints(holePoints, parentTransform));
        }

        path.Fill = new FillStyle { Color = fillColor };
        path.Stroke = null; // Solid fill typically has no stroke

        nodes.Add(path);
    }

    return nodes;
}
```

**PDF output**:
The PDF content stream for a filled polygon with holes:
```
outer_x0 outer_y0 m
outer_x1 outer_y1 l
... (outer boundary CCW)
h
hole_x0 hole_y0 m
hole_x1 hole_y1 l
... (hole boundary CW)
h
f    % nonzero winding fill
```

**Input**: Nesting tree, hatch entity, transform.

**Output**: PathNode with fill.

**Edge cases**:
- Very complex boundaries with many vertices: may cause large PDF content streams
- Self-intersecting boundaries: even-odd fill handles these better
- Zero-area boundaries: skip

---

### Step 5: Implement Pattern Line Generation

**What**: Generate the infinite pattern lines for a hatch pattern and clip them to boundaries.

**Algorithm**:
```csharp
List<RenderNode> RenderPatternFill(List<NestingNode> tree, Hatch hatch,
    Matrix4 parentTransform)
{
    var nodes = new List<RenderNode>();
    double scale = hatch.PatternScale;
    double angle = hatch.PatternAngle; // in radians

    // Get the bounding box of all boundaries (for generating enough lines)
    var bbox = ComputeBoundingBox(tree);
    double diagonal = Distance(bbox.Min, bbox.Max);

    foreach (var patternLine in hatch.Pattern.Lines)
    {
        // Apply hatch-level angle rotation to pattern line angle
        double lineAngle = patternLine.Angle + ToDegrees(angle);
        double lineAngleRad = ToRadians(lineAngle);

        // Line direction and perpendicular
        XY direction = new XY(Math.Cos(lineAngleRad), Math.Sin(lineAngleRad));
        XY perpendicular = new XY(-direction.Y, direction.X);

        // Offset vector (scaled)
        XY offset = new XY(patternLine.OffsetX * scale, patternLine.OffsetY * scale);
        double spacing = DotProduct(offset, perpendicular); // perpendicular distance
        if (Math.Abs(spacing) < 1e-10) continue; // degenerate pattern

        // Base point (scaled)
        XY basePoint = new XY(patternLine.BaseX * scale, patternLine.BaseY * scale);

        // Dash pattern (scaled)
        double[] dashes = patternLine.Dashes?.Select(d => d * scale).ToArray();

        // Generate parallel lines covering the bounding box
        int lineCount = (int)(diagonal / Math.Abs(spacing)) + 2;

        for (int i = -lineCount; i <= lineCount; i++)
        {
            XY lineOrigin = basePoint + perpendicular * (i * spacing);

            // Generate line segment covering the bbox
            // Line extends from lineOrigin - direction * diagonal to lineOrigin + direction * diagonal
            XY lineStart = lineOrigin - direction * diagonal;
            XY lineEnd = lineOrigin + direction * diagonal;

            // Apply dash pattern to create line segments
            var segments = ApplyDashPattern(lineStart, lineEnd, direction, dashes);

            // Clip each segment against boundary polygons
            foreach (var segment in segments)
            {
                var clipped = ClipLineToBoundaries(segment.Start, segment.End, tree);
                foreach (var clippedSeg in clipped)
                {
                    var p1 = Transform(clippedSeg.Start, parentTransform);
                    var p2 = Transform(clippedSeg.End, parentTransform);

                    var path = new PathNode
                    {
                        Segments = { MoveTo(p1), LineTo(p2) },
                        Stroke = new StrokeStyle
                        {
                            Color = _resolver.ResolveColor(hatch, null),
                            Width = ResolveLineweight(hatch),
                        }
                    };
                    nodes.Add(path);
                }
            }
        }
    }

    return nodes;
}
```

**Input**: Nesting tree, hatch pattern definition, scale/angle.

**Output**: PathNode for each visible pattern line segment.

**Edge cases**:
- Very fine pattern (small spacing, many lines): limit maximum line count to prevent memory exhaustion
- Pattern scale of 0: skip
- Empty dash array: continuous line (no gaps)
- All-negative dash pattern (all gaps): no visible output

---

### Step 6: Implement Dash Pattern Application

**What**: Apply a repeating dash/gap pattern along a line.

**Algorithm**:
```csharp
List<LineSegment> ApplyDashPattern(XY start, XY end, XY direction, double[] dashes)
{
    if (dashes == null || dashes.Length == 0)
    {
        // Continuous line
        return new List<LineSegment> { new LineSegment(start, end) };
    }

    var segments = new List<LineSegment>();
    double totalLength = Distance(start, end);
    double position = 0;
    int dashIndex = 0;
    bool penDown = true; // Start with pen down (first element is always a dash)

    while (position < totalLength)
    {
        double dashLength = Math.Abs(dashes[dashIndex]);
        double segmentEnd = Math.Min(position + dashLength, totalLength);

        if (dashes[dashIndex] > 0 || dashes[dashIndex] == 0)
        {
            // Dash (positive) or dot (zero)
            XY segStart = start + direction * position;
            XY segEnd = (dashes[dashIndex] == 0)
                ? segStart // Dot: zero-length segment (rendered as dot)
                : start + direction * segmentEnd;
            segments.Add(new LineSegment(segStart, segEnd));
        }
        // Negative = gap: skip

        position = segmentEnd;
        dashIndex = (dashIndex + 1) % dashes.Length;
    }

    return segments;
}
```

**Input**: Line endpoints, direction, dash pattern array.

**Output**: List of visible line segments.

**Edge cases**:
- Single dash element (continuous line): treat as continuous
- Very short dashes relative to line length: many segments, limit count
- Dot (0 length): render as a small circle or square of lineweight diameter

---

### Step 7: Implement Line-to-Boundary Clipping

**What**: Clip a line segment against boundary polygons, respecting fill/hole regions.

**Algorithm using Clipper2**:
```csharp
List<LineSegment> ClipLineToBoundaries(XY start, XY end, List<NestingNode> tree)
{
    // Build Clipper2 paths for filled regions
    var clipper = new ClipperD();

    // Add the line as an open subject path
    var linePath = new PathD { new PointD(start.X, start.Y), new PointD(end.X, end.Y) };
    clipper.AddOpenSubject(linePath);

    // Add filled regions as clip polygons
    foreach (var region in GetFilledRegions(tree))
    {
        var polyPath = region.Boundary.Select(p => new PointD(p.X, p.Y)).ToList();
        clipper.AddClip(new PathsD { polyPath });
    }

    // Execute intersection
    var solution = new PolyTreeD();
    var openSolution = new PathsD();
    clipper.Execute(ClipType.Intersection, FillRule.NonZero, solution, openSolution);

    // Convert result back to LineSegments
    var result = new List<LineSegment>();
    foreach (var path in openSolution)
    {
        if (path.Count >= 2)
        {
            result.Add(new LineSegment(
                new XY(path[0].x, path[0].y),
                new XY(path[path.Count - 1].x, path[path.Count - 1].y)));
        }
    }

    return result;
}
```

**Alternative (without Clipper2)**: Implement line-polygon intersection manually:
```csharp
List<LineSegment> ClipLineToPolygon(XY start, XY end, List<XY> polygon)
{
    // Find all intersection parameters t where line crosses polygon edges
    var intersections = new List<double>();
    intersections.Add(0); // start
    intersections.Add(1); // end

    for (int i = 0; i < polygon.Count; i++)
    {
        int j = (i + 1) % polygon.Count;
        if (LineLineIntersection(start, end, polygon[i], polygon[j], out double t, out double u))
        {
            if (t > 0 && t < 1 && u >= 0 && u <= 1)
                intersections.Add(t);
        }
    }

    intersections.Sort();

    // Test midpoint of each segment: if inside polygon, keep it
    var result = new List<LineSegment>();
    for (int i = 0; i < intersections.Count - 1; i++)
    {
        double tMid = (intersections[i] + intersections[i + 1]) / 2;
        XY midPoint = Lerp(start, end, tMid);

        if (PointInPolygon(midPoint, polygon))
        {
            XY segStart = Lerp(start, end, intersections[i]);
            XY segEnd = Lerp(start, end, intersections[i + 1]);
            result.Add(new LineSegment(segStart, segEnd));
        }
    }

    return result;
}
```

**Input**: Line segment, boundary polygons.

**Output**: List of clipped line segments that are inside filled regions.

**Edge cases**:
- Line entirely outside all boundaries: empty result
- Line entirely inside a hole: empty result
- Line tangent to boundary: may produce zero-length segment
- Very complex boundary with many edges: Clipper2 handles this efficiently

---

### Step 8: Implement Standard Pattern Definitions

**What**: Define the standard AutoCAD hatch patterns (from acad.pat) as code constants.

**Implementation**:
```csharp
static class StandardPatterns
{
    public static readonly Dictionary<string, PatternDefinition> Patterns = new()
    {
        ["ANSI31"] = new PatternDefinition(new[]
        {
            new PatternLine(45, 0, 0, -0.0884, 0.0884, Array.Empty<double>())
        }),

        ["ANSI32"] = new PatternDefinition(new[]
        {
            new PatternLine(45, 0, 0, -0.0884, 0.0884, Array.Empty<double>()),
            new PatternLine(45, 0.176, 0, -0.0884, 0.0884, Array.Empty<double>())
        }),

        ["ANSI33"] = new PatternDefinition(new[]
        {
            new PatternLine(45, 0, 0, -0.0884, 0.0884, Array.Empty<double>()),
            new PatternLine(45, 0.176, 0, -0.0884, 0.0884, Array.Empty<double>()),
            new PatternLine(45, 0.352, 0, -0.0884, 0.0884, Array.Empty<double>()),
            new PatternLine(45, 0.528, 0, -0.0884, 0.0884, Array.Empty<double>())
        }),

        ["ANSI34"] = new PatternDefinition(new[]
        {
            new PatternLine(45, 0, 0, -0.0884, 0.0884, Array.Empty<double>()),
            new PatternLine(-45, 0, 0, -0.0884, 0.0884, Array.Empty<double>())
        }),

        // ... more standard patterns (ANSI35-37, BRICK, EARTH, GRAVEL, etc.)
    };
}
```

The patterns should also be loadable from external `.pat` files for custom patterns.

**Input**: Pattern name.

**Output**: Pattern definition with line families.

**Edge cases**:
- Unknown pattern name: log warning, render as solid fill or skip
- Custom pattern embedded in the HATCH entity (rather than by name): use the embedded definition directly

---

### Step 9: Handle Hatch OCS and Transform

**What**: Apply the HATCH entity's coordinate system and any parent transforms.

**Algorithm**:
```csharp
// HATCH entities define boundaries in OCS
// The elevation (group 30) defines the Z position in OCS
// The extrusion vector defines the OCS normal

Matrix4 ComputeHatchTransform(Hatch hatch)
{
    var ocsToWcs = Matrix4.GetArbitraryAxis(hatch.Normal);
    var elevation = Matrix4.CreateTranslation(0, 0, hatch.Elevation);
    return ocsToWcs * elevation;
}
```

Boundary points are in OCS and must be transformed to WCS before clipping and pattern generation. Alternatively, generate pattern geometry in OCS and transform the final result to WCS.

---

### Step 10: Integrate into EntityFrontend

**What**: Add the HATCH case to the EntityFrontend dispatcher.

```csharp
case Hatch hatch:
    return _hatchGenerator.RenderHatch(hatch, worldTransform);
```

---

## Testing Strategy

### Unit Tests

1. **Boundary extraction (polyline)**: Square boundary with 4 vertices. Verify 4 points returned.
2. **Boundary extraction (arc)**: Polyline boundary with bulge. Verify arc tessellation.
3. **Boundary extraction (edge path)**: LineEdge + ArcEdge. Verify correct tessellation.
4. **Nesting classification**: Outer square + inner square. Verify outer=filled, inner=hole.
5. **Normal hatch style**: Three nested boundaries. Verify alternating fill/hole/fill.
6. **Outer hatch style**: Three nested boundaries. Verify only outermost filled.
7. **Ignore hatch style**: Inner boundaries ignored, everything filled.
8. **Solid fill path**: Square boundary. Verify filled PathNode with correct vertices.
9. **Solid fill with hole**: Square with circular hole. Verify two sub-paths with correct winding.
10. **Pattern line generation**: ANSI31 pattern on 100x100 square. Verify 45-degree lines at correct spacing.
11. **Dash pattern application**: Line with dash [5, -3] pattern. Verify alternating segments.
12. **Line clipping to rectangle**: Diagonal line across rectangle. Verify clipped endpoints.
13. **Pattern scale**: Scale 2.0 doubles spacing and dash lengths.
14. **Pattern angle**: 30-degree rotation applied to pattern lines.
15. **Point-in-polygon**: Test points inside/outside/on-edge of a polygon.

### Integration Tests

16. **ANSI31 hatch DXF**: Rectangular hatch with ANSI31 pattern. Compare with oracle.
17. **Solid fill with islands**: Multiple nested boundaries. Compare with oracle.
18. **Complex boundary**: Hatch bounded by arcs and splines.
19. **Hatch in INSERT**: Hatch inside a block, inserted with scale/rotation.
20. **Multiple patterns**: Drawing with different hatch patterns.

---

## Dependencies

### Depends On
- **Stage 00 (Render Infrastructure)**: PathNode with fill/stroke, Transform infrastructure, PropertyResolver
- **Stage 01 (INSERT/Blocks)**: Not directly, but hatch inside blocks uses BlockExpander

### Enables
- No other stages directly depend on HATCH

### External Dependencies
- **Clipper2** (`Clipper2Lib` NuGet package, Boost license): For polygon clipping of pattern lines against boundaries. Can be implemented without Clipper2 using manual line-polygon intersection, but Clipper2 is significantly more robust and handles edge cases.
- ACadSharp `Hatch`, `HatchBoundaryPath`, `HatchPattern` classes
