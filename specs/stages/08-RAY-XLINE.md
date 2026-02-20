# Stage 08: RAY / XLINE (Infinite Line Clipping)

## Overview

RAY and XLINE are construction-line entities that extend infinitely. A RAY starts at a base point and extends infinitely in one direction (semi-infinite). An XLINE passes through a base point and extends infinitely in both directions (fully infinite).

These entities cannot be rendered directly because they have no finite extent. The rendering strategy is to clip them against the viewport or paper extents, producing a finite line segment that can be drawn normally.

This is the simplest missing feature to implement. The geometry is trivial (straight lines), and the only algorithmic challenge is parametric line clipping against a rectangle. The Liang-Barsky algorithm is well-suited for this task.

The target module for this stage is `InfiniteLineClipper.cs`.

---

## Domain Knowledge

### RAY Entity

A RAY is a semi-infinite line defined by:

| Group Code | Field | Description |
|-----------|-------|-------------|
| 10/20/30 | Base point | Starting point of the ray (WCS) |
| 11/21/31 | Direction vector | Direction in which the ray extends (WCS, unit or non-unit) |

The parametric form: `P(t) = BasePoint + t * Direction`, where `t >= 0`.

The ray starts at `BasePoint` (t=0) and extends infinitely in the `Direction` (t -> infinity).

### XLINE Entity

An XLINE (construction line) is a fully infinite line defined by:

| Group Code | Field | Description |
|-----------|-------|-------------|
| 10/20/30 | Base point | A point on the line (WCS) |
| 11/21/31 | Direction vector | Direction of the line (WCS, unit or non-unit) |

The parametric form: `P(t) = BasePoint + t * Direction`, where `-infinity < t < +infinity`.

The line passes through `BasePoint` and extends infinitely in both directions.

### Why Infinite Lines Exist

RAY and XLINE are used as construction aids in drafting:
- Reference lines for alignment
- Bisectors for geometric constructions
- Horizon lines in perspective drawings
- Guide lines that should not have visible endpoints

They are excluded from bounding box calculations (they would make the bbox infinite), and many renderers skip them entirely (ezdxf's Path module does not support them due to their infinite nature).

### Clipping Strategy

To render an infinite line, clip it against a finite rectangle (viewport or paper extents):

1. Determine the clip rectangle (the visible area)
2. Express the infinite line in parametric form
3. Compute the parameter range `[t_min, t_max]` where the line is inside the rectangle
4. If a valid range exists, render the line from `P(t_min)` to `P(t_max)`
5. If no valid range exists (line entirely outside), skip rendering

For a **RAY**: t_min is clamped to `max(0, computed_t_min)` (cannot go before the base point).

For an **XLINE**: both t_min and t_max are computed from the clip.

### Liang-Barsky Algorithm

The Liang-Barsky algorithm is a parametric line clipping algorithm that is efficient and handles infinite lines naturally:

```
Given:
  Line: P(t) = P0 + t * d, where d = (dx, dy)
  Rectangle: x_min, x_max, y_min, y_max

Define:
  p[0] = -dx,  q[0] = P0.x - x_min   (left boundary)
  p[1] = +dx,  q[1] = x_max - P0.x   (right boundary)
  p[2] = -dy,  q[2] = P0.y - y_min   (bottom boundary)
  p[3] = +dy,  q[3] = y_max - P0.y   (top boundary)

For each boundary i (0..3):
  if p[i] == 0:
    if q[i] < 0: line is entirely outside (parallel to and outside boundary)
    else: line is parallel to and inside this boundary (no constraint)
  else:
    t_i = q[i] / p[i]
    if p[i] < 0: t_i is an entry (update t_min)
    if p[i] > 0: t_i is an exit (update t_max)

Initial: t_min = -infinity (XLINE) or 0 (RAY), t_max = +infinity

After processing all boundaries:
  if t_min <= t_max: line is visible, segment from P(t_min) to P(t_max)
  else: line is entirely outside, skip
```

This algorithm naturally handles both RAY and XLINE by adjusting the initial t_min.

### Determining the Clip Rectangle

The clip rectangle depends on context:
- **In model space** (direct rendering): Use the model-space extents or a configured paper size
- **In a viewport**: Use the viewport's model-space visible window (ViewCenter +/- ViewHeight/2 in model coords)
- **In paper space**: Use the paper size from the Layout

The clip rectangle should be slightly expanded (by 1-2% of its dimensions) to avoid visual artifacts from lines ending exactly at the viewport boundary.

### OCS Considerations

RAY and XLINE can have non-default extrusion vectors (group 210/220/230), meaning their base point and direction are in OCS. Before clipping, transform to WCS using the Arbitrary Axis Algorithm.

---

## External Reference Code

### ezdxf Ray/XLine Entities (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/entities/xline.py
- **What to study**: Entity class definitions for Ray and XLine. Note that ezdxf's Path module explicitly excludes these entities. The `virtual_entities()` method is NOT implemented for these types.

### Liang-Barsky Algorithm
- **Reference**: Liang, Y.D. and Barsky, B.A. (1984). "A New Concept and Method for Line Clipping." ACM TOG 3(1).
- **What to study**: The parametric approach where each rectangle edge produces a constraint on the line parameter t. The algorithm runs in O(1) per line (constant number of boundary tests).

### Alternative: Cohen-Sutherland Algorithm
- **Reference**: Standard computer graphics textbook algorithm
- **What to study**: Uses region codes (4-bit outcodes) for each endpoint. Efficient for trivial accept/reject cases. Less elegant for infinite lines (requires artificial endpoints first).

### QCAD dxflib RAY/XLINE Support
- **URL**: https://www.qcad.org/en/90-dxflib
- **What to study**: dxflib declares support for RAY and XLINE parsing. The rendering approach in QCAD clips to the view bounds. (GPL license -- study ideas only, do not copy code.)

---

## Step-by-Step Implementation Plan

### Step 1: Create InfiniteLineClipper Class

**What**: A static utility class that clips infinite lines to a rectangle.

**Key structure**:
```csharp
static class InfiniteLineClipper
{
    /// <summary>
    /// Clip a RAY (semi-infinite, t >= 0) to a rectangle.
    /// Returns null if the ray does not intersect the rectangle.
    /// </summary>
    public static (XY Start, XY End)? ClipRay(XY basePoint, XY direction, BoundingBox clipRect);

    /// <summary>
    /// Clip an XLINE (fully infinite) to a rectangle.
    /// Returns null if the line does not intersect the rectangle.
    /// </summary>
    public static (XY Start, XY End)? ClipXLine(XY basePoint, XY direction, BoundingBox clipRect);
}
```

**Input**: Base point, direction vector, clip rectangle.

**Output**: Finite line segment endpoints, or null if no intersection.

**Edge cases**:
- Direction vector of (0,0): degenerate, skip
- Direction vector unnormalized: algorithm works with any non-zero direction

---

### Step 2: Implement Liang-Barsky Clipping

**What**: The core clipping algorithm.

**Algorithm**:
```csharp
static (XY Start, XY End)? LiangBarskyClip(
    XY basePoint, XY direction, BoundingBox rect, double tMinInitial)
{
    double dx = direction.X;
    double dy = direction.Y;
    double x0 = basePoint.X;
    double y0 = basePoint.Y;

    double[] p = { -dx, dx, -dy, dy };
    double[] q = {
        x0 - rect.Min.X,   // left
        rect.Max.X - x0,   // right
        y0 - rect.Min.Y,   // bottom
        rect.Max.Y - y0    // top
    };

    double tMin = tMinInitial;  // 0 for RAY, -1e18 for XLINE
    double tMax = 1e18;         // Effectively infinity

    for (int i = 0; i < 4; i++)
    {
        if (Math.Abs(p[i]) < 1e-15)
        {
            // Line is parallel to this boundary
            if (q[i] < 0)
            {
                // Line is outside this boundary
                return null;
            }
            // Otherwise: line is inside this boundary, no constraint
        }
        else
        {
            double t = q[i] / p[i];
            if (p[i] < 0)
            {
                // Entry point: update tMin
                if (t > tMin) tMin = t;
            }
            else
            {
                // Exit point: update tMax
                if (t < tMax) tMax = t;
            }
        }
    }

    if (tMin > tMax)
    {
        return null; // Line is entirely outside
    }

    XY startPoint = new XY(x0 + tMin * dx, y0 + tMin * dy);
    XY endPoint = new XY(x0 + tMax * dx, y0 + tMax * dy);

    return (startPoint, endPoint);
}

public static (XY Start, XY End)? ClipRay(XY basePoint, XY direction, BoundingBox clipRect)
{
    return LiangBarskyClip(basePoint, direction, clipRect, tMinInitial: 0.0);
}

public static (XY Start, XY End)? ClipXLine(XY basePoint, XY direction, BoundingBox clipRect)
{
    return LiangBarskyClip(basePoint, direction, clipRect, tMinInitial: -1e18);
}
```

**Input**: Base point, direction, rectangle, initial t_min.

**Output**: Clipped segment or null.

**Edge cases**:
- Line passes through exactly one corner of the rectangle: produces a degenerate zero-length segment (tMin == tMax), skip
- Line coincides with a rectangle edge: valid segment along the edge
- Very small direction component (nearly parallel to an edge): handled by the `< 1e-15` threshold

---

### Step 3: Determine the Clip Rectangle

**What**: Compute the appropriate clip rectangle for clipping infinite lines.

**Algorithm**:
```csharp
BoundingBox GetClipRectangle(Layout layout, Viewport viewport)
{
    if (viewport != null)
    {
        // Clip to viewport's model-space visible window
        double halfWidth = viewport.ViewportSize.X / 2 / viewport.ScaleFactor;
        double halfHeight = viewport.ViewHeight / 2;
        XY center = viewport.ViewCenter;

        // Expand slightly to avoid clipping artifacts
        double margin = Math.Max(halfWidth, halfHeight) * 0.02;

        return new BoundingBox(
            new XY(center.X - halfWidth - margin, center.Y - halfHeight - margin),
            new XY(center.X + halfWidth + margin, center.Y + halfHeight + margin)
        );
    }
    else
    {
        // Clip to paper/layout extents
        BoundingBox paperBounds = layout.GetPaperBounds();

        // Expand slightly
        double margin = Math.Max(paperBounds.Width, paperBounds.Height) * 0.02;

        return new BoundingBox(
            paperBounds.Min - new XY(margin, margin),
            paperBounds.Max + new XY(margin, margin)
        );
    }
}
```

**Input**: Layout and optional viewport.

**Output**: Clip rectangle in the appropriate coordinate space.

**Edge cases**:
- No layout or viewport: use a default large rectangle (e.g., -10000 to +10000 in each axis)
- Viewport with rotation (TwistAngle): clipping should be done after rotation is applied, or the clip rect should be expanded to account for rotation
- Zero-size viewport: skip

---

### Step 4: Implement RAY Rendering

**What**: Process a RAY entity into a PathNode render primitive.

**Algorithm**:
```csharp
List<RenderNode> RenderRay(Ray ray, Matrix4 parentTransform, BoundingBox clipRect,
    StrokeStyle style)
{
    // 1. Transform base point and direction to WCS
    var ocsToWcs = Matrix4.GetArbitraryAxis(ray.Normal);
    XYZ baseWCS = TransformPoint(ray.StartPoint, ocsToWcs);
    XYZ dirWCS = TransformDirection(ray.Direction, ocsToWcs);

    // 2. Apply parent transform
    baseWCS = TransformPoint(baseWCS, parentTransform);
    dirWCS = TransformDirection(dirWCS, parentTransform);

    // 3. Project to 2D (ignore Z for 2D rendering)
    XY base2D = new XY(baseWCS.X, baseWCS.Y);
    XY dir2D = new XY(dirWCS.X, dirWCS.Y);

    if (dir2D.Length() < 1e-15)
    {
        _log.Skip(ray, "zero direction vector");
        return empty;
    }

    // 4. Clip to rectangle
    var clipped = InfiniteLineClipper.ClipRay(base2D, dir2D, clipRect);

    if (clipped == null)
    {
        _log.Skip(ray, "ray outside clip rectangle");
        return empty;
    }

    // 5. Create PathNode
    var path = new PathNode
    {
        Segments = {
            new MoveToSegment { Point = clipped.Value.Start },
            new LineToSegment { Point = clipped.Value.End }
        },
        Stroke = style,
        SourceHandle = ray.Handle,
    };

    return new List<RenderNode> { path };
}
```

**Input**: RAY entity, parent transform, clip rectangle, style.

**Output**: PathNode with finite line segment, or empty if outside clip.

**Edge cases**:
- RAY base point is outside the clip rectangle but direction points toward it: the clipped segment starts where the ray enters the rectangle
- RAY base point is inside the clip rectangle: the clipped segment starts at the base point

---

### Step 5: Implement XLINE Rendering

**What**: Process an XLINE entity into a PathNode render primitive.

**Algorithm**:
```csharp
List<RenderNode> RenderXLine(XLine xline, Matrix4 parentTransform, BoundingBox clipRect,
    StrokeStyle style)
{
    // Same as RAY but using ClipXLine instead of ClipRay

    var ocsToWcs = Matrix4.GetArbitraryAxis(xline.Normal);
    XYZ baseWCS = TransformPoint(xline.StartPoint, ocsToWcs);
    XYZ dirWCS = TransformDirection(xline.Direction, ocsToWcs);

    baseWCS = TransformPoint(baseWCS, parentTransform);
    dirWCS = TransformDirection(dirWCS, parentTransform);

    XY base2D = new XY(baseWCS.X, baseWCS.Y);
    XY dir2D = new XY(dirWCS.X, dirWCS.Y);

    if (dir2D.Length() < 1e-15)
    {
        _log.Skip(xline, "zero direction vector");
        return empty;
    }

    var clipped = InfiniteLineClipper.ClipXLine(base2D, dir2D, clipRect);

    if (clipped == null)
    {
        _log.Skip(xline, "xline outside clip rectangle");
        return empty;
    }

    var path = new PathNode
    {
        Segments = {
            new MoveToSegment { Point = clipped.Value.Start },
            new LineToSegment { Point = clipped.Value.End }
        },
        Stroke = style,
        SourceHandle = xline.Handle,
    };

    return new List<RenderNode> { path };
}
```

Identical to RAY rendering except `ClipXLine` allows negative t values.

---

### Step 6: Integrate into EntityFrontend

**What**: Add RAY and XLINE cases to the EntityFrontend dispatcher.

```csharp
case Ray ray:
    var rayClipRect = GetClipRectangle(currentLayout, currentViewport);
    return RenderRay(ray, worldTransform, rayClipRect, style);

case XLine xline:
    var xlineClipRect = GetClipRectangle(currentLayout, currentViewport);
    return RenderXLine(xline, worldTransform, xlineClipRect, style);
```

The `GetClipRectangle` must be accessible to the EntityFrontend, which means the current viewport/layout context needs to be passed through or stored as state.

---

## Testing Strategy

### Unit Tests

1. **XLINE through center**: Base (50,50), direction (1,0), clip rect (0,0)-(100,100). Verify clipped to (0,50)-(100,50).
2. **XLINE diagonal**: Base (50,50), direction (1,1), clip rect (0,0)-(100,100). Verify clipped to (0,0)-(100,100).
3. **XLINE outside**: Base (200,200), direction (1,0), clip rect (0,0)-(100,100). Verify null (outside).
4. **XLINE parallel to edge**: Base (0,50), direction (1,0), clip rect (0,0)-(100,100). Verify clipped to (0,50)-(100,50).
5. **XLINE through corner**: Base (0,0), direction (1,1), clip rect (0,0)-(100,100). Verify clipped to (0,0)-(100,100).
6. **RAY starting inside**: Base (50,50), direction (1,0), clip rect (0,0)-(100,100). Verify clipped to (50,50)-(100,50).
7. **RAY starting outside, pointing in**: Base (-50,50), direction (1,0), clip rect (0,0)-(100,100). Verify clipped to (0,50)-(100,50).
8. **RAY starting outside, pointing away**: Base (-50,50), direction (-1,0), clip rect (0,0)-(100,100). Verify null.
9. **RAY starting at edge**: Base (0,50), direction (1,0), clip rect (0,0)-(100,100). Verify clipped to (0,50)-(100,50).
10. **Vertical XLINE**: Base (50,0), direction (0,1). Verify clipped to (50,0)-(50,100).
11. **Zero direction**: Base (50,50), direction (0,0). Verify skipped.
12. **Nearly parallel to edge**: Base (50,0), direction (1,0.001). Verify correct clip.

### Integration Tests

13. **RAY in DXF**: DXF with a RAY entity. Verify it renders as a line from base to viewport edge.
14. **XLINE in DXF**: DXF with an XLINE. Verify it renders spanning the viewport.
15. **Multiple RAY/XLINE**: DXF with several construction lines at different angles.
16. **RAY/XLINE in viewport**: Construction lines visible through a viewport with specific view.
17. **RAY/XLINE in INSERT**: Construction line inside a block.

### Test DXF Generation

```python
import ezdxf

doc = ezdxf.new()
msp = doc.modelspace()

# Horizontal XLINE through center
msp.add_xline((50, 50), (1, 0))

# Diagonal XLINE
msp.add_xline((0, 0), (1, 1))

# RAY from corner
msp.add_ray((0, 0), (1, 0.5))

# Vertical RAY
msp.add_ray((50, 0), (0, 1))

doc.saveas('test_ray_xline.dxf')
```

---

## Dependencies

### Depends On
- **Stage 00 (Render Infrastructure)**: PathNode, Transform, PropertyResolver, RenderLog

### Enables
- No other stages depend on RAY/XLINE

### External Dependencies
- None beyond the standard library. The Liang-Barsky algorithm is self-contained.
- ACadSharp `Ray` and `XLine` entity classes
