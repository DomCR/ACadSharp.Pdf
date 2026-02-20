# Stage 07: MLINE

## Overview

MLINE (Multiline) is a compound entity that draws multiple parallel lines following a common path, with configurable join and cap styles. Each line element has its own color, linetype, and offset from the center path. MLineStyle defines the set of elements and their properties.

MLines are used in architectural and structural drawings to represent walls, pipes, roads, and other features that consist of parallel lines. A typical wall MLine has two elements offset equidistant from the center, representing the two sides of the wall, with optional fill between them.

The core geometric challenge is the **parallel offset algorithm**: given a polyline path, generate parallel offset curves at specified distances. At vertices where the path changes direction, the offset curves must be joined using the specified join type (miter, arc, or none). At the ends of the path, cap types control how the element lines are terminated.

The target module for this stage is `MLineOffsetRenderer.cs`.

---

## Domain Knowledge

### MLINE Entity Structure

An MLINE entity contains:
- A reference to an `MLineStyle` (group 2 for style name, group 340 for handle)
- A scale factor (group 40): multiplies all element offsets
- Justification (group 70): 0=Top, 1=Zero (center), 2=Bottom
- A list of vertices defining the base path

**Key group codes**:

| Group Code | Field | Description |
|-----------|-------|-------------|
| 2 | Style name | Reference to MLINESTYLE dictionary entry |
| 340 | Style handle | Handle to the MLineStyle object |
| 40 | Scale factor | Multiplier for element offsets |
| 70 | Justification | 0=Top, 1=Zero, 2=Bottom |
| 71 | Flags | Bit 1=has fill color, bit 2=start cap, bit 4=end cap |
| 72 | Number of vertices | Count of path vertices |
| 73 | Number of elements | Count of elements per vertex (matches style) |
| 10/20/30 | Start point | First vertex (WCS) |
| 11/21/31 | Vertex direction | Direction vector at each vertex |
| 12/22/32 | Miter direction | Miter vector at each vertex |

Each vertex also contains per-element parameters that define the actual offset geometry at that point. These parameters include the miter distance and line segment data needed to correctly join elements at corners.

### MLineStyle

The MLineStyle defines the visual appearance of all MLINE instances using that style:

**Style properties**:
| Property | Description |
|----------|-------------|
| Name | Style name (e.g., "Standard", "Wall") |
| Description | Human-readable description |
| Fill color | Color for fill between outermost elements (if enabled) |
| Start/End angle | Cap angles (radians in ACadSharp via `DxfReferenceType.IsAngle`, default π/2 = 90°) |
| Elements | List of element definitions |

**Element definition**:
| Property | Description |
|----------|-------------|
| Offset | Distance from the center line (positive = above/left, negative = below/right) |
| Color | ACI color for this element line |
| Linetype | Linetype name for this element line |

The default "Standard" MLineStyle has two elements at offsets +0.5 and -0.5 (i.e., two lines 1.0 unit apart, centered on the path).

### Justification

Justification controls which part of the element set aligns with the path vertices:

| Value | Mode | Behavior |
|-------|------|----------|
| 0 | Top | The element with the most positive offset lies on the path. Other elements are offset downward/rightward. |
| 1 | Zero | The center line (offset 0) lies on the path. Elements are offset symmetrically. |
| 2 | Bottom | The element with the most negative offset lies on the path. Other elements are offset upward/leftward. |

Justification is implemented by adding a constant offset to all element offsets:
- Top: subtract `max_offset` from all offsets
- Zero: no adjustment
- Bottom: subtract `min_offset` from all offsets

### Parallel Offset Algorithm

For each element, generate a parallel curve at the element's offset distance from the base polyline:

```
For each segment of the base polyline (from vertex[i] to vertex[i+1]):
  1. Compute segment direction: d = normalize(vertex[i+1] - vertex[i])
  2. Compute perpendicular (left normal): n = (-d.Y, d.X)
  3. Offset both endpoints: start_offset = vertex[i] + n * offset
                            end_offset = vertex[i+1] + n * offset
```

At each interior vertex, the offset curves from adjacent segments must be connected using the join type.

### Join Types

**Miter Join**:
The offset lines from adjacent segments are extended until they intersect. This creates a sharp corner.
- Compute the intersection of the two offset lines
- If the miter length exceeds a limit (typically 2x the offset), use a bevel instead
- The miter direction vector stored in the MLINE entity provides the pre-computed miter direction

**Arc Join**:
An arc of constant radius connects the offset endpoints at each corner. The arc center is the original (unoffset) vertex.
- Arc radius = |offset|
- Arc from the end of the first offset segment to the start of the next offset segment
- Direction: follows the shorter arc (matches the turn direction)

**None (No Join)**:
Offset segments are left disconnected at corners. Each segment stands alone.

### Cap Types

Caps are drawn at the start and end of the MLINE path:

**Line Cap**:
A straight line connecting the endpoints of all elements at the cap end.

**Outer Arc**:
An arc connecting the outermost elements (those with the largest and smallest offsets) at the cap end.

**Inner Arc**:
Arcs connecting pairs of elements from outside to inside at the cap end.

**Angle Cap**:
Lines drawn at a specified angle (default π/2 rad = 90° = perpendicular to the path direction) from each element endpoint.

Cap types are independent for start and end caps.

### Fill

When fill is enabled (flag bit 1), the area between the outermost elements is filled with the fill color. The fill region is a polygon formed by:
- The outer element (most positive offset) path
- The cap connections
- The reverse of the inner element (most negative offset) path

### Scale Factor

The MLINE scale factor (group 40) multiplies all element offsets. A scale of 2.0 doubles the distance between elements. A scale of -1.0 reverses the offset direction (mirror).

---

## External Reference Code

### ezdxf MLine Documentation (MIT License)
- **URL**: https://ezdxf.readthedocs.io/en/stable/dxfinternals/entities/mline.html
- **What to study**: Internal structure of the MLINE entity, including how vertex parameters encode the offset geometry and join information.

### ezdxf MLine Virtual Entities (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/entities/mline.py
- **What to study**: The `virtual_entities()` method that decomposes an MLINE into LINE, ARC, and HATCH primitives. This is the direct reference for how to render an MLINE.

### GDAL OGR DXF MLINE Handling (MIT/X-style License)
- **URL**: https://github.com/OSGeo/gdal/blob/master/ogr/ogrsf_frmts/dxf/ogrdxf_feature.cpp
- **What to study**: `TranslateMLINE()` function. GDAL converts MLINE to MULTILINESTRING geometry (parallel lines) but ignores styling details (fill, caps, colors). Good reference for the basic offset algorithm.

### Clipper2 Offset (Boost License)
- **URL**: https://github.com/AngusJohnson/Clipper2
- **What to study**: `ClipperOffset` class for generating offset polygons/polylines. While Clipper2 is designed for closed polygons, it can handle open paths with `EndType.Joined` or `EndType.Butt`. The offset algorithm handles self-intersections at tight angles. NuGet: `Clipper2Lib`.

### CavalierContours (MIT License, Rust)
- **URL**: https://github.com/jbuckmccready/CavalierContours
- **What to study**: Academic-grade polyline offset algorithm with proper handling of self-intersections, arc segments, and degenerate cases. Written in Rust but the algorithm descriptions are well-documented and portable.

---

## Step-by-Step Implementation Plan

### Step 1: Create MLineOffsetRenderer Class

**What**: The main class for converting MLINE entities to render primitives.

**Key structure**:
```csharp
class MLineOffsetRenderer
{
    private PropertyResolver _resolver;
    private RenderLog _log;

    List<RenderNode> RenderMLine(MLine mline, Matrix4 parentTransform)
    {
        // 1. Resolve MLineStyle
        var style = ResolveMLineStyle(mline);
        if (style == null || style.Elements.Count == 0)
        {
            _log.Skip(mline, "no style or empty style");
            return empty;
        }

        // 2. Extract base path vertices
        var vertices = ExtractVertices(mline);
        if (vertices.Count < 2)
        {
            _log.Skip(mline, "insufficient vertices");
            return empty;
        }

        // 3. Apply justification offset
        var adjustedElements = ApplyJustification(style.Elements, mline.Justification, mline.Scale);

        var nodes = new List<RenderNode>();

        // 4. Generate fill (if enabled)
        if (mline.HasFill)
        {
            nodes.AddRange(RenderFill(vertices, adjustedElements, style.FillColor, parentTransform));
        }

        // 5. Generate element lines
        foreach (var element in adjustedElements)
        {
            nodes.AddRange(RenderElement(vertices, element, style, mline, parentTransform));
        }

        // 6. Generate caps
        nodes.AddRange(RenderCaps(vertices, adjustedElements, style, mline, parentTransform));

        return nodes;
    }
}
```

**Input**: MLine entity + parent transform.

**Output**: List of render primitives (paths for lines, arcs, fill).

**Edge cases**:
- MLine with only 2 vertices (single segment): no join, only caps
- MLine with coincident vertices: skip zero-length segments
- Scale factor of 0: all elements collapse to the center line

---

### Step 2: Implement Element Offset Adjustment for Justification

**What**: Adjust element offsets based on justification mode and scale factor.

**Algorithm**:
```csharp
List<AdjustedElement> ApplyJustification(List<MLineElement> elements, int justification, double scale)
{
    double maxOffset = elements.Max(e => e.Offset);
    double minOffset = elements.Min(e => e.Offset);

    double justificationShift;
    switch (justification)
    {
        case 0: // Top: most positive offset aligns with path
            justificationShift = -maxOffset;
            break;
        case 1: // Zero: center aligns with path
            justificationShift = 0;
            break;
        case 2: // Bottom: most negative offset aligns with path
            justificationShift = -minOffset;
            break;
        default:
            justificationShift = 0;
            break;
    }

    return elements.Select(e => new AdjustedElement
    {
        Offset = (e.Offset + justificationShift) * scale,
        Color = e.Color,
        Linetype = e.Linetype,
    }).ToList();
}
```

**Input**: Element definitions, justification code, scale.

**Output**: Adjusted element list with modified offsets.

**Edge cases**:
- All elements at offset 0: justification has no effect
- Negative scale: reverses offset direction (mirror)
- Single element: justification still applies to center it

---

### Step 3: Implement Parallel Offset Polyline Generation

**What**: For a single element, generate the offset polyline along the base path.

**Algorithm**:
```csharp
List<XY> GenerateOffsetPolyline(List<XY> vertices, double offset)
{
    if (Math.Abs(offset) < 1e-10)
    {
        return vertices.ToList(); // Zero offset = center line
    }

    var offsetPoints = new List<XY>();
    int n = vertices.Count;

    for (int i = 0; i < n; i++)
    {
        if (i == 0) // First vertex
        {
            XY dir = Normalize(vertices[1] - vertices[0]);
            XY normal = new XY(-dir.Y, dir.X);
            offsetPoints.Add(vertices[0] + normal * offset);
        }
        else if (i == n - 1) // Last vertex
        {
            XY dir = Normalize(vertices[n - 1] - vertices[n - 2]);
            XY normal = new XY(-dir.Y, dir.X);
            offsetPoints.Add(vertices[n - 1] + normal * offset);
        }
        else // Interior vertex
        {
            // Compute miter point at the intersection of offset lines from adjacent segments
            XY d1 = Normalize(vertices[i] - vertices[i - 1]);
            XY n1 = new XY(-d1.Y, d1.X);
            XY d2 = Normalize(vertices[i + 1] - vertices[i]);
            XY n2 = new XY(-d2.Y, d2.X);

            // Miter vector: bisector of the two normals, scaled by 1/cos(half_angle)
            XY bisector = Normalize(n1 + n2);
            double cosHalfAngle = DotProduct(bisector, n1);

            if (Math.Abs(cosHalfAngle) < 0.01) // Near-180-degree turn
            {
                // Degenerate: use simple offset
                offsetPoints.Add(vertices[i] + n1 * offset);
            }
            else
            {
                double miterLength = offset / cosHalfAngle;
                offsetPoints.Add(vertices[i] + bisector * miterLength);
            }
        }
    }

    return offsetPoints;
}
```

**Input**: Base path vertices, element offset distance.

**Output**: Offset polyline vertices.

**Edge cases**:
- Offset exceeds miter limit at sharp angles: the miter point shoots far away; clamp to a maximum miter distance (typically 2x offset)
- U-turn (180-degree angle change): miter intersection does not exist, fall back to perpendicular offset
- Collinear segments (0-degree angle change): miter point is at normal offset (trivial case)
- Very short segments: numerical instability in direction computation

---

### Step 4: Implement Join Type Rendering

**What**: Render the connection between offset segments at interior vertices.

**For Miter Join** (default):
```csharp
// Already handled by the miter computation in GenerateOffsetPolyline
// The offset polyline includes miter points at each interior vertex
// No additional geometry needed beyond the polyline itself
```

**For Arc Join**:
```csharp
List<RenderNode> RenderArcJoin(XY vertex, XY offsetEnd1, XY offsetStart2,
    double offset, Matrix4 transform)
{
    // Arc center = the original (unoffset) vertex
    // Arc radius = |offset|
    double radius = Math.Abs(offset);
    double startAngle = Math.Atan2(offsetEnd1.Y - vertex.Y, offsetEnd1.X - vertex.X);
    double endAngle = Math.Atan2(offsetStart2.Y - vertex.Y, offsetStart2.X - vertex.X);

    // Determine if CW or CCW based on turn direction
    bool ccw = Cross(offsetEnd1 - vertex, offsetStart2 - vertex) > 0;
    if (offset < 0) ccw = !ccw; // Reverse for negative offset (inside of turn)

    var arcPath = TessellateArc(vertex, radius, startAngle, endAngle, ccw);

    var path = new PathNode
    {
        Segments = CreateSegmentsFromPoints(Transform(arcPath, transform)),
        Stroke = currentStroke,
    };

    return new List<RenderNode> { path };
}
```

**For No Join**: Simply leave a gap between offset segments. Each segment is rendered as a separate PathNode.

**Input**: Vertex position, adjacent offset endpoints, join type.

**Output**: PathNode for the join (arc or nothing).

**Edge cases**:
- Arc join at near-zero turn angle: arc is negligibly small, skip
- Arc join at near-180-degree turn: arc is a semicircle

---

### Step 5: Implement Cap Type Rendering

**What**: Render the caps at the start and end of the MLINE.

**Algorithm** (Line Cap):
```csharp
List<RenderNode> RenderLineCap(List<XY> elementEndpoints, bool isStart,
    StrokeStyle stroke, Matrix4 transform)
{
    // Sort element endpoints by offset order
    // Draw a straight line connecting all endpoints
    var sorted = elementEndpoints.OrderBy(p => /* offset order */).ToList();

    var path = new PathNode
    {
        Segments = { MoveTo(Transform(sorted[0], transform)) },
        Stroke = stroke,
    };

    for (int i = 1; i < sorted.Count; i++)
    {
        path.Segments.Add(LineTo(Transform(sorted[i], transform)));
    }

    return new List<RenderNode> { path };
}
```

**Algorithm** (Outer Arc Cap):
```csharp
List<RenderNode> RenderOuterArcCap(XY outerEndpoint1, XY outerEndpoint2,
    XY pathEndpoint, StrokeStyle stroke, Matrix4 transform)
{
    // Arc connecting the two outermost element endpoints
    // Center = path endpoint (on the base path)
    XY center = pathEndpoint;
    double radius = Distance(center, outerEndpoint1);
    double startAngle = Math.Atan2(outerEndpoint1.Y - center.Y, outerEndpoint1.X - center.X);
    double endAngle = Math.Atan2(outerEndpoint2.Y - center.Y, outerEndpoint2.X - center.X);

    var arcPoints = TessellateArc(center, radius, startAngle, endAngle, true);

    var path = CreatePathFromPoints(Transform(arcPoints, transform));
    path.Stroke = stroke;

    return new List<RenderNode> { path };
}
```

**Input**: Element endpoints at the cap, cap type, transform.

**Output**: PathNode for the cap geometry.

**Edge cases**:
- Only 1 element: line cap is a single point, skip
- Angle cap with non-π/2 values: rotate the cap line by the specified angle (radians in ACadSharp)

---

### Step 6: Implement Fill Region Rendering

**What**: Fill the region between the outermost elements.

**Algorithm**:
```csharp
List<RenderNode> RenderFill(List<XY> vertices, List<AdjustedElement> elements,
    Color fillColor, Matrix4 transform)
{
    // Get the two outermost elements (max and min offset)
    var outerElement = elements.OrderByDescending(e => e.Offset).First();
    var innerElement = elements.OrderBy(e => e.Offset).First();

    // Generate offset polylines for both
    var outerPolyline = GenerateOffsetPolyline(vertices, outerElement.Offset);
    var innerPolyline = GenerateOffsetPolyline(vertices, innerElement.Offset);

    // Create a closed polygon: outer forward + inner reversed
    var fillPolygon = new List<XY>();
    fillPolygon.AddRange(outerPolyline);
    fillPolygon.AddRange(innerPolyline.AsEnumerable().Reverse());

    // Transform and create filled path
    var path = new PathNode
    {
        Fill = new FillStyle { Color = fillColor },
        Stroke = null,
    };

    path.Segments.Add(MoveTo(Transform(fillPolygon[0], transform)));
    for (int i = 1; i < fillPolygon.Count; i++)
        path.Segments.Add(LineTo(Transform(fillPolygon[i], transform)));
    path.Segments.Add(new CloseSegment());

    return new List<RenderNode> { path };
}
```

**Input**: Base path, elements, fill color, transform.

**Output**: Filled PathNode.

**Edge cases**:
- Only 1 element: no fill region
- Elements with same offset: zero-width fill
- Self-intersecting fill polygon at tight angles: may cause visual artifacts

---

### Step 7: Implement Per-Element Line Rendering

**What**: For each element, generate the offset polyline and render as a stroked path.

**Algorithm**:
```csharp
List<RenderNode> RenderElement(List<XY> vertices, AdjustedElement element,
    MLineStyle style, MLine mline, Matrix4 transform)
{
    var offsetPolyline = GenerateOffsetPolyline(vertices, element.Offset);

    // Create path segments
    var path = new PathNode
    {
        Stroke = new StrokeStyle
        {
            Color = _resolver.ResolveColor(element.Color, mline),
            Width = ResolveLineweight(mline),
            DashPattern = ResolveDashPattern(element.Linetype),
        },
    };

    path.Segments.Add(MoveTo(Transform(offsetPolyline[0], transform)));
    for (int i = 1; i < offsetPolyline.Count; i++)
        path.Segments.Add(LineTo(Transform(offsetPolyline[i], transform)));

    return new List<RenderNode> { path };
}
```

**Input**: Base path, element definition, transform.

**Output**: PathNode for the element line.

**Edge cases**:
- Element color ByBlock: use MLINE entity color
- Element linetype not found: use CONTINUOUS

---

### Step 8: Handle MLINE OCS Transform

**What**: Apply OCS-to-WCS transformation for the MLINE entity.

```csharp
// MLINE vertices are in WCS, but the entity may have a non-default extrusion vector
// Apply OCS-to-WCS if needed
Matrix4 ComputeMLineTransform(MLine mline)
{
    return Matrix4.GetArbitraryAxis(mline.Normal);
}
```

**Edge cases**:
- Most MLINEs use the default extrusion (0,0,1) so this is typically identity
- Non-default extrusion: transform all vertices before offset computation

---

### Step 9: Integrate into EntityFrontend

**What**: Add the MLINE case to the EntityFrontend dispatcher.

```csharp
case MLine mline:
    return _mlineRenderer.RenderMLine(mline, worldTransform);
```

---

## Testing Strategy

### Unit Tests

1. **Simple offset**: Horizontal line from (0,0) to (100,0), offset +5. Verify line from (0,5) to (100,5).
2. **Negative offset**: Same line, offset -5. Verify line from (0,-5) to (100,-5).
3. **Scale factor**: Scale 2.0 with offset 5. Verify effective offset is 10.
4. **Justification Top**: Two elements at +0.5, -0.5, Top justification. Verify offsets become 0 and -1.
5. **Justification Bottom**: Same elements, Bottom. Verify offsets become +1 and 0.
6. **90-degree corner miter**: L-shaped path with 90-degree turn. Verify miter point position.
7. **Acute angle miter limit**: Very sharp angle. Verify miter length is clamped.
8. **Arc join**: 90-degree corner with arc join. Verify arc center, radius, and angles.
9. **Line cap**: Two-element MLine. Verify straight line connecting element endpoints.
10. **Outer arc cap**: Two-element MLine with arc cap. Verify semicircular arc.
11. **Fill polygon**: Two-element MLine with fill. Verify closed polygon formed by outer + reverse inner.
12. **Three-element MLine**: Offsets at +1, 0, -1. Verify three parallel lines.
13. **Single-segment MLine**: Only 2 vertices. Verify no joins, only caps and lines.

### Integration Tests

14. **Standard MLine DXF**: Two-element wall with miter joins. Compare with oracle.
15. **Custom MLine style**: Three elements with different colors. Verify each element's color.
16. **MLine with fill**: Filled two-element MLine. Verify solid fill between elements.
17. **MLine with arc caps**: Verify rounded end caps.
18. **MLine in INSERT**: MLine inside a block with scale and rotation.
19. **Complex MLine**: Multi-segment path with various angles.

---

## Dependencies

### Depends On
- **Stage 00 (Render Infrastructure)**: PathNode, FillStyle, Transform, PropertyResolver

### Enables
- No other stages directly depend on MLINE

### External Dependencies
- (Optional) **Clipper2** (`Clipper2Lib` NuGet, Boost license): `ClipperOffset` can be used for robust polyline offset, especially for handling self-intersections at tight angles. However, a simpler miter-based offset algorithm is sufficient for most MLine cases.
- ACadSharp `MLine`, `MLineStyle`, `MLineStyleElement` classes
