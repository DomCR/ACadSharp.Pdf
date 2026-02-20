# Stage 03: DIMENSION Types

## Overview

DIMENSION entities represent measured distances, angles, radii, and coordinates in a drawing. They are the most common annotation type in technical drawings and consist of three visual components: extension lines (pointing to the measured feature), a dimension line (with arrowheads at each end), and dimension text (the measured value, formatted per the dimension style).

DXF supports seven dimension types: Linear, Rotated, Aligned, Angular (2-line), Angular (3-point), Diameter, Radius, and Ordinate. Each type has its own geometric construction rules but shares a common set of dimension style (DimStyle) variables that control appearance.

A critical shortcut exists: AutoCAD pre-renders each dimension into an anonymous block (`*D` prefix). If this block is present and populated, the renderer can simply expand it as a regular INSERT (Stage 01) instead of computing the geometry from scratch. However, the fallback computation is needed for DXF files from non-AutoCAD sources that may not include the anonymous block, or when the block is stale.

The target module for this stage is `DimLayoutEngine.cs`.

---

## Domain Knowledge

### Dimension Type Classification

The dimension type is stored in group code 70 (bitmask):

| Bits 0-3 (type) | Type | Description |
|------------------|------|-------------|
| 0 | Linear/Rotated | Measures horizontal/vertical/rotated distance |
| 1 | Aligned | Measures true distance between two points |
| 2 | Angular (2-line) | Measures angle between two lines |
| 3 | Diameter | Measures circle/arc diameter |
| 4 | Radius | Measures circle/arc radius |
| 5 | Angular (3-point) | Measures angle defined by three points |
| 6 | Ordinate | Shows X or Y coordinate of a feature |

Bit 5 (value 32) indicates the dimension text has been positioned by the user (not auto-placed). Bit 6 (value 64) means the dimension uses the actual measurement value (not user-overridden text). Bit 7 (value 128) is used internally.

### Common Group Codes

| Group Code | Field | Description |
|-----------|-------|-------------|
| 2 | Block name | Name of anonymous block (*D...) containing pre-rendered geometry |
| 10/20/30 | Definition point | WCS point (meaning varies by type) |
| 11/21/31 | Text midpoint | OCS point where dimension text is placed |
| 13/23/33 | First point | WCS (extension line 1 origin for linear/aligned) |
| 14/24/34 | Second point | WCS (extension line 2 origin for linear/aligned) |
| 15/25/35 | Additional point | Type-specific (e.g., arc point for radius) |
| 16/26/36 | Arc definition point | For angular dimensions |
| 40 | Leader length | For radius/diameter |
| 50 | Rotation angle | For rotated linear dimensions |
| 51 | Horizontal direction | Angle of horizontal (for oblique dimensions) |
| 53 | Oblique angle | Oblique extension line angle |
| 1 | User text | Override text ("" = use measured, " " = suppress, "<>" = include measured) |
| 3 | Dim style name | Reference to DIMSTYLE table |

### DimStyle Variables

DimStyle controls every aspect of dimension appearance. Key variables (with their DIMSTYLE group codes):

**Text**:
| Variable | GC | Default | Description |
|----------|-----|---------|-------------|
| DIMTXT | 140 | 2.5 | Text height |
| DIMTAD | 77 | 0 | Text above dim line: 0=centered, 1=above, 2=JIS, 3=below, 4=above no leader |
| DIMTIH | 73 | 1 | Text inside horizontal: 1=always horizontal, 0=aligned with dim line |
| DIMTOH | 74 | 1 | Text outside horizontal: 1=always horizontal, 0=aligned |
| DIMJUST | 280 | 0 | Text justification: 0=center, 1=left ext line, 2=right ext line, 3=above left, 4=above right |
| DIMGAP | 147 | 0.625 | Gap around text (negative = box around text) |
| DIMCLRT | 178 | 0 | Text color (0=ByBlock) |
| DIMDEC | 271 | 4 | Decimal places |
| DIMPOST | 3 | "" | Text prefix/suffix template (e.g., "<>mm") |
| DIMRND | 45 | 0.0 | Rounding increment |

**Lines**:
| Variable | GC | Default | Description |
|----------|-----|---------|-------------|
| DIMASZ | 41 | 2.5 | Arrow size |
| DIMEXE | 44 | 1.25 | Extension line extension beyond dim line |
| DIMEXO | 42 | 0.625 | Extension line offset from measured point |
| DIMDLE | 46 | 0.0 | Dim line extension beyond extension lines (for tick marks) |
| DIMCLRD | 176 | 0 | Dim line color |
| DIMCLRE | 177 | 0 | Extension line color |
| DIMLWD | 371 | -2 | Dim line lineweight |
| DIMLWE | 372 | -2 | Extension line lineweight |
| DIMSE1 | 75 | 0 | Suppress first extension line |
| DIMSE2 | 76 | 0 | Suppress second extension line |
| DIMSD1 | 281 | 0 | Suppress first dim line half |
| DIMSD2 | 282 | 0 | Suppress second dim line half |

**Arrows**:
| Variable | GC | Default | Description |
|----------|-----|---------|-------------|
| DIMBLK | 342 | "" | Arrow block name (both ends) |
| DIMBLK1 | 343 | "" | First arrow block (if DIMSAH=1) |
| DIMBLK2 | 344 | "" | Second arrow block (if DIMSAH=1) |
| DIMSAH | 173 | 0 | Separate arrow blocks: 0=same, 1=different |
| DIMTSZ | 142 | 0 | Tick size (if >0, use oblique tick instead of arrow) |

**Fit**:
| Variable | GC | Default | Description |
|----------|-----|---------|-------------|
| DIMATFIT | 289 | 3 | Arrow/text fit: 0=both outside, 1=arrows out, 2=text out, 3=best fit |
| DIMTMOVE | 279 | 0 | Text movement: 0=with dim line, 1=add leader, 2=no leader |
| DIMTOFL | 172 | 0 | Force dim line inside: 0=no, 1=yes |

### Anonymous Blocks (*D)

When AutoCAD saves a dimension, it pre-renders the complete visual geometry into an anonymous block named `*D0`, `*D1`, etc. This block contains:
- LINE entities for extension lines and the dimension line
- SOLID or INSERT for arrowheads
- TEXT or MTEXT for the dimension value

**Strategy**:
1. Check if the dimension's anonymous block (group 2) exists and is non-empty
2. If yes, expand the block as a regular INSERT (using Stage 01 BlockExpander) with identity transform
3. If no, compute the geometry from the dimension's definition points and DimStyle

This dual approach handles both AutoCAD-generated files (where blocks are reliable) and third-party DXF generators (where blocks may be missing).

### DimStyle Overrides

Individual dimension entities can override DimStyle variables via XDATA (group 1070/1040/1005 in the ACAD reactors) or via the DimStyleOverrides dictionary. The effective value for any variable is:
```
EffectiveValue = EntityOverride ?? DimStyle.Value ?? DefaultValue
```

The ACadSharp library provides override resolution through the dimension entity's properties.

---

## External Reference Code

### ezdxf DimStyleOverride + virtual_entities() (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/entities/dimension.py
- **What to study**: `DimStyleOverride` class that merges base style with entity-level overrides. `virtual_entities()` method that decomposes a dimension into lines, arcs, and text.

### ezdxf Dimension Rendering (MIT License)
- **URL**: https://github.com/mozman/ezdxf/tree/master/src/ezdxf/dimstyles
- **What to study**: Type-specific renderers for linear, angular, radial, ordinate dimensions. Each computes extension lines, dimension line, arrowheads, and text placement.

### GDAL ogrdxf_dimension.cpp (MIT/X-style License)
- **URL**: https://github.com/OSGeo/gdal/blob/master/ogr/ogrsf_frmts/dxf/ogrdxf_dimension.cpp
- **What to study**: `TranslateDIMENSION()` function. GDAL implements a rudimentary fallback for Linear dimensions only: it draws extension lines and a dimension line between the definition points. Good starting point but incomplete.

### AutoCAD DXF Reference - DIMENSION
- **URL**: https://help.autodesk.com/view/OARX/2024/ENU/?guid=GUID-239E1C8A-F876-4D87-9B46-1FBE89EC4250
- **What to study**: Official definition point semantics for each dimension type. Critical for understanding which group codes mean what for each type.

---

## Step-by-Step Implementation Plan

### Step 1: Create DimLayoutEngine Class with Anonymous Block Fallback

**What**: The main dimension rendering class that first tries anonymous block expansion, then falls back to geometric computation.

**Key structure**:
```csharp
class DimLayoutEngine
{
    private BlockExpander _blockExpander;
    private TextLayoutEngine _textEngine;
    private PropertyResolver _resolver;

    List<RenderNode> RenderDimension(Dimension dimension, Matrix4 parentTransform)
    {
        // Strategy 1: Try anonymous block
        if (TryExpandAnonymousBlock(dimension, parentTransform, out var blockNodes))
        {
            return blockNodes;
        }

        // Strategy 2: Compute geometry from definition points + DimStyle
        return ComputeDimensionGeometry(dimension, parentTransform);
    }

    bool TryExpandAnonymousBlock(Dimension dim, Matrix4 transform, out List<RenderNode> nodes)
    {
        string blockName = dim.BlockName; // group 2
        if (string.IsNullOrEmpty(blockName))
        {
            nodes = null;
            return false;
        }

        var block = dim.Document?.BlockRecords.FirstOrDefault(b => b.Name == blockName);
        if (block == null || !block.Entities.Any())
        {
            nodes = null;
            return false;
        }

        // Expand as a regular block insertion (identity transform since block is in WCS)
        nodes = _blockExpander.ExpandBlock(block, transform);
        return true;
    }
}
```

**Input**: Dimension entity + parent transform.

**Output**: List of render primitives (from block or computed).

**Edge cases**:
- Block exists but contains stale geometry (entity was modified after block creation): no reliable detection, use block as-is
- Block name is empty string: compute geometry
- Dimension in OCS with non-default extrusion: apply OCS-to-WCS before computing geometry

---

### Step 2: Implement DimStyle Resolution

**What**: Resolve all DimStyle variables for a dimension entity, accounting for entity-level overrides.

**Implementation**:
```csharp
class DimStyleResolver
{
    DimProperties Resolve(Dimension dimension)
    {
        var style = dimension.Style ?? GetDefaultStyle();

        return new DimProperties
        {
            TextHeight = dimension.TextHeight ?? style.DIMTXT ?? 2.5,
            ArrowSize = dimension.ArrowSize ?? style.DIMASZ ?? 2.5,
            ExtensionLineExtension = style.DIMEXE ?? 1.25,
            ExtensionLineOffset = style.DIMEXO ?? 0.625,
            TextGap = style.DIMGAP ?? 0.625,
            TextAbove = style.DIMTAD ?? 0,
            TextInsideHorizontal = style.DIMTIH ?? true,
            TextOutsideHorizontal = style.DIMTOH ?? true,
            DecimalPlaces = style.DIMDEC ?? 4,
            DimLineColor = style.DIMCLRD ?? Color.ByBlock,
            ExtLineColor = style.DIMCLRE ?? Color.ByBlock,
            TextColor = style.DIMCLRT ?? Color.ByBlock,
            ArrowBlockName = style.DIMBLK,
            ArrowBlock1Name = style.DIMBLK1,
            ArrowBlock2Name = style.DIMBLK2,
            SeparateArrows = style.DIMSAH ?? false,
            TickSize = style.DIMTSZ ?? 0,
            SuppressExtLine1 = style.DIMSE1 ?? false,
            SuppressExtLine2 = style.DIMSE2 ?? false,
            PostFormat = style.DIMPOST ?? "",
            Rounding = style.DIMRND ?? 0,
            // ... all other variables
        };
    }
}
```

**Input**: Dimension entity with its DimStyle reference.

**Output**: Fully resolved `DimProperties` record.

**Edge cases**:
- Missing DimStyle (reference handle not found): use all defaults
- Partial overrides: some values from override, rest from style, rest from defaults
- DIMPOST with `<>` placeholder: replace `<>` with the measured value text

---

### Step 3: Implement Linear/Aligned Dimension Geometry

**What**: Compute the visual geometry for linear (horizontal/vertical/rotated) and aligned dimensions.

**Algorithm** (Linear/Aligned):
```
Given:
  P1 = group 13/23 (first extension line origin, WCS)
  P2 = group 14/24 (second extension line origin, WCS)
  DimLinePoint = group 10/20 (defines position of dimension line, WCS)
  Rotation = group 50 (for rotated linear; aligned uses P1->P2 angle)

For Aligned:
  direction = normalize(P2 - P1)
  perpendicular = rotate(direction, 90deg)

For Linear/Rotated:
  direction = (cos(rotation), sin(rotation))
  perpendicular = (-sin(rotation), cos(rotation))

1. Project P1 and P2 onto the dimension line (through DimLinePoint, along perpendicular):
   D1 = P1 + perpendicular * dot(DimLinePoint - P1, perpendicular)
   D2 = P2 + perpendicular * dot(DimLinePoint - P2, perpendicular)

2. Extension lines:
   E1_start = P1 + perpendicular * sign * DIMEXO  (offset from measured point)
   E1_end = D1 + perpendicular * sign * DIMEXE    (extend beyond dim line)
   E2_start = P2 + perpendicular * sign * DIMEXO
   E2_end = D2 + perpendicular * sign * DIMEXE

3. Dimension line:
   From D1 to D2 (with gap for text if text is centered)

4. Arrowheads at D1 and D2:
   Direction: toward each other along the dimension line
   Size: DIMASZ

5. Text:
   Measured value = |D2 - D1| (distance along dimension direction)
   Format: apply DIMRND, DIMDEC, DIMPOST
   Position: midpoint of D1-D2, offset by DIMTAD, or at user-specified group 11/21
```

**Input**: Linear or Aligned Dimension entity + DimProperties.

**Output**: List of PathNode (extension lines, dimension line) + TextRunNode (value).

**Edge cases**:
- P1 == P2 (zero-length dimension): still render with dimension value "0"
- Dimension line overlaps extension lines (very small dimension): arrows may need to flip outside
- Oblique extension lines (DIMALTANG): extension lines at non-perpendicular angle
- User-placed text (bit 5 of group 70 set): use group 11/21 position instead of auto-placement

---

### Step 4: Implement Angular Dimension Geometry

**What**: Compute geometry for angular dimensions (both 2-line and 3-point variants).

**Algorithm** (Angular 2-line):
```
Given:
  P1, P2 = first line endpoints (group 13/14 and 15/16)
  The vertex (intersection) of the two lines
  ArcPoint = group 16/26 (point on the arc, defines radius)

1. Find intersection of the two lines -> vertex
2. Compute start and end angles from vertex to line endpoints
3. Arc radius = distance from vertex to ArcPoint
4. Draw arc from start_angle to end_angle at given radius
5. Extension lines from line endpoints toward arc
6. Arrowheads at arc endpoints, tangent to arc
7. Text centered on arc
```

**Algorithm** (Angular 3-point):
```
Given:
  Vertex = group 15/25 (angle vertex)
  P1 = group 13/23 (first line endpoint)
  P2 = group 14/24 (second line endpoint)

1. Compute angles: angle1 = atan2(P1-Vertex), angle2 = atan2(P2-Vertex)
2. Arc radius = distance from vertex to dimension arc point (group 16/26)
3. Draw arc between angle1 and angle2
4. Extension lines, arrowheads, text similar to 2-line
```

**Input**: Angular Dimension entity + DimProperties.

**Output**: PathNode (arc, extension lines) + TextRunNode (angle value in degrees).

**Edge cases**:
- Lines are parallel (no intersection): degenerate, skip or show 180 degrees
- Angle is reflex (>180 degrees): ensure correct arc direction
- Zero angle: degenerate

---

### Step 5: Implement Radial/Diameter Dimension Geometry

**What**: Compute geometry for radius and diameter dimensions.

**Algorithm** (Radius):
```
Given:
  Center = group 10/20 (center of circle/arc)
  ChordPoint = group 15/25 (point on the circle/arc)

1. Radius = distance(Center, ChordPoint)
2. Direction = normalize(ChordPoint - Center)
3. Dimension line: from Center (or offset) to ChordPoint
4. One arrowhead at ChordPoint, pointing toward center
5. Text: "R" + formatted radius value
6. Text position: along the dimension line, inside or outside based on fit
```

**Algorithm** (Diameter):
```
Given:
  Center = group 10/20
  ChordPoint = group 15/25

1. Diameter = 2 * distance(Center, ChordPoint)
2. Direction = normalize(ChordPoint - Center)
3. FarPoint = Center - Direction * radius (opposite side)
4. Dimension line: from FarPoint through Center to ChordPoint (full diameter)
5. Two arrowheads: at ChordPoint and FarPoint
6. Text: "%%c" (diameter symbol) + formatted value
7. Text position: along dimension line
```

**Input**: Radial/Diameter Dimension entity + DimProperties.

**Output**: PathNode + TextRunNode.

**Edge cases**:
- Very small radius where text does not fit inside: place text outside with leader
- Center mark/lines (DIMCEN): draw center mark if DIMCEN > 0

---

### Step 6: Implement Ordinate Dimension Geometry

**What**: Compute geometry for ordinate dimensions (show X or Y coordinate).

**Algorithm**:
```
Given:
  FeaturePoint = group 13/23 (point on the feature, WCS)
  LeaderEnd = group 14/24 (end of leader, where text appears)
  Type: X-datum or Y-datum (determined by group 70 bit flag or angle)

1. Value = X or Y coordinate of FeaturePoint (relative to dimension origin)
2. Leader: polyline from FeaturePoint to LeaderEnd with one or two bends
   - If horizontal ordinate: bend at (FeaturePoint.X, LeaderEnd.Y)
   - If vertical ordinate: bend at (LeaderEnd.X, FeaturePoint.Y)
3. Text at LeaderEnd
```

**Input**: Ordinate Dimension entity + DimProperties.

**Output**: PathNode (leader) + TextRunNode.

**Edge cases**:
- Leader endpoint at same position as feature point: zero-length leader
- User-specified UCS origin: ordinate values should be relative to it

---

### Step 7: Implement Arrowhead Rendering

**What**: Draw arrowheads at the ends of dimension lines.

**Types**:
- **Closed filled** (default, internal name `"."`): Filled triangular arrow
- **Open** (`"_OPEN"`): Two lines forming a V
- **Closed** (`"_CLOSED"`): Outlined triangle
- **Dot** (`"_DOT"`): Small filled circle
- **Oblique** (tick, when DIMTSZ > 0): Short diagonal line at 45 degrees
- **Custom block**: Arrow defined as a named block, referenced by DIMBLK/DIMBLK1/DIMBLK2

**Algorithm** (Closed filled arrow):
```csharp
PathNode CreateFilledArrow(XY tip, XY direction, double size)
{
    var perp = Perpendicular(direction);
    var back = tip - direction * size;
    var p1 = back + perp * (size * 0.3); // 30% of size for width
    var p2 = back - perp * (size * 0.3);

    return new PathNode
    {
        Segments = { MoveTo(tip), LineTo(p1), LineTo(p2), Close() },
        Fill = new FillStyle { Color = dimLineColor },
        Stroke = null
    };
}
```

**For custom block arrows**:
```csharp
List<RenderNode> CreateBlockArrow(string blockName, XY tip, XY direction, double size)
{
    var block = document.BlockRecords.FirstOrDefault(b => b.Name == blockName);
    if (block == null) return CreateFilledArrow(tip, direction, size); // fallback

    // Follow Stage 00 transform conventions (T * R * S).
    var transform = Matrix4.CreateTranslation(tip) *
                    Matrix4.CreateFromAxisAngle(XYZ.AxisZ, AngleOf(direction)) *
                    Matrix4.CreateScale(new XYZ(size, size, 1));

    return _blockExpander.ExpandBlock(block, transform);
}
```

**Input**: Arrow tip position, direction, size, arrow type/block name.

**Output**: PathNode (for built-in arrows) or expanded block nodes.

**Edge cases**:
- Arrow size of 0: skip arrow rendering
- Custom block not found: fall back to closed filled
- DIMSAH flag: use different blocks for first and second arrows

---

### Step 8: Implement Text Placement Algorithm

**What**: Determine where dimension text is placed relative to the dimension geometry.

**Algorithm**:
```
1. Compute available space between extension lines:
   space = distance(D1, D2)

2. Compute text width:
   textWidth = MeasureText(valueString, textHeight)

3. Check fit:
   requiredSpace = textWidth + 2 * DIMGAP
   textFits = (requiredSpace <= space)
   arrowsFit = (2 * DIMASZ <= space)

4. Based on DIMATFIT:
   0: Both arrows and text outside if they don't fit
   1: Move arrows outside first
   2: Move text outside first
   3: Best fit (whichever arrangement works)

5. Text vertical position (DIMTAD):
   0: Centered on dim line (break dim line around text)
   1: Above dim line (DIMGAP between text baseline and dim line)
   3: Below dim line

6. Text horizontal position (DIMJUST):
   0: Centered between extension lines
   1: Near first extension line
   2: Near second extension line

7. Text orientation:
   DIMTIH: If text is inside, force horizontal (ignore dim line angle)
   DIMTOH: If text is outside, force horizontal

8. If user-placed text (bit 5 of group 70): use text midpoint from group 11/21
```

**Input**: Dimension geometry, text metrics, DimProperties.

**Output**: Text position, rotation, and whether dim line needs a break.

**Edge cases**:
- Text override (group 1): user-specified text string
- Text suppressed (group 1 = " "): no text rendering
- Text with `<>` placeholder: insert measured value at `<>` position
- DIMGAP negative: draw a box around the text
- Zero-length dimension: text still displayed

---

### Step 9: Implement Dimension Value Formatting

**What**: Format the measured value into a display string.

**Algorithm**:
```csharp
string FormatDimensionValue(double measurement, DimProperties props, string userText)
{
    // Check for user-supplied text
    if (userText == " ") return null; // suppress text
    if (!string.IsNullOrEmpty(userText) && userText != "<>")
    {
        if (userText.Contains("<>"))
            return userText.Replace("<>", FormatNumber(measurement, props));
        return userText;
    }

    string formatted = FormatNumber(measurement, props);

    // Apply DIMPOST prefix/suffix
    if (!string.IsNullOrEmpty(props.PostFormat))
    {
        if (props.PostFormat.Contains("<>"))
            formatted = props.PostFormat.Replace("<>", formatted);
        else
            formatted = formatted + props.PostFormat;
    }

    return formatted;
}

string FormatNumber(double value, DimProperties props)
{
    // Apply rounding
    if (props.Rounding > 0)
        value = Math.Round(value / props.Rounding) * props.Rounding;

    // Format with decimal places
    return value.ToString($"F{props.DecimalPlaces}");
}
```

**Input**: Measured value, DimProperties, user text override.

**Output**: Formatted dimension string.

**Edge cases**:
- Tolerance display (DIMTOL): upper and lower tolerance values
- Alternate units (DIMALT): show both primary and alternate measurements
- Angular dimensions: format in degrees/minutes/seconds based on DIMUNIT
- Diameter/Radius prefix: prepend `%%c` (diameter symbol) or `R`

---

### Step 10: Integrate into EntityFrontend

**What**: Add dimension cases to the EntityFrontend dispatcher.

```csharp
case DimensionLinear dim:
case DimensionAligned dim:
case DimensionAngular2Line dim:
case DimensionAngular3Pt dim:
case DimensionDiameter dim:
case DimensionRadius dim:
case DimensionOrdinate dim:
    return _dimEngine.RenderDimension(dim, worldTransform);
```

---

## Testing Strategy

### Unit Tests

1. **DimStyle resolution**: Override text height on entity, verify it takes priority over style value.
2. **Linear dimension geometry**: P1=(0,0), P2=(100,0), dimline at Y=20. Verify extension lines, dimension line, and text position.
3. **Aligned dimension**: P1=(0,0), P2=(30,40). Verify angle = atan2(40,30), distance = 50.
4. **Radial dimension**: Center=(50,50), ChordPoint=(80,50). Verify radius=30, arrow at chord point.
5. **Diameter dimension**: Verify line passes through center, two arrows.
6. **Angular dimension**: Two lines at 30 and 60 degrees. Verify arc and angle = 30 degrees.
7. **Ordinate dimension**: Feature at (100, 200). Verify displayed value and leader path.
8. **Arrowhead types**: Closed filled, open, tick. Verify geometry for each.
9. **Text placement inside**: Space = 100, text width = 20. Verify centered placement.
10. **Text placement outside**: Space = 5, text width = 20. Verify text placed outside with leader.
11. **Value formatting**: Value 123.456 with DIMDEC=2. Verify "123.46".
12. **Value with DIMPOST**: DIMPOST="<> mm", value 50. Verify "50.0000 mm".
13. **User text override**: group 1 = "CUSTOM". Verify "CUSTOM" is displayed.
14. **Text suppressed**: group 1 = " ". Verify no text rendered.
15. **Anonymous block expansion**: Dimension with *D block. Verify block is expanded instead of computing geometry.

### Integration Tests

16. **Linear dimension DXF**: Create a DXF with a linear dimension, render, compare with oracle.
17. **Multiple dimension types**: DXF with one of each type. Verify all render correctly.
18. **DimStyle variations**: Same dimension with different styles (text size, arrow size, colors).
19. **Nested in INSERT**: Dimension inside a block, inserted with scale. Verify correct scaling.

### Test DXF Generation

```python
import ezdxf

doc = ezdxf.new()
msp = doc.modelspace()

# Linear dimension
msp.add_linear_dim(base=(0, 30), p1=(0, 0), p2=(100, 0)).render()

# Aligned dimension
msp.add_aligned_dim(p1=(0, 0), p2=(30, 40), distance=5).render()

# Radius dimension
msp.add_circle((50, 50), 30)
msp.add_radius_dim(center=(50, 50), radius=30, angle=45).render()

doc.saveas('test_dimensions.dxf')
```

---

## Dependencies

### Depends On
- **Stage 00 (Render Infrastructure)**: PathNode, TextRunNode, Transform infrastructure, PropertyResolver
- **Stage 01 (INSERT/Blocks)**: BlockExpander for anonymous block expansion and custom arrow blocks
- **Stage 02 (TEXT/MTEXT)**: TextLayoutEngine for dimension text formatting and positioning

### Enables
- **Stage 04 (MULTILEADER)**: Shares arrowhead rendering logic
- **Stage 09 (TOLERANCE)**: TOLERANCE entities can be associated with dimensions

### External Dependencies
- ACadSharp Dimension entity classes (DimensionLinear, DimensionAligned, etc.)
- ACadSharp DimStyle/DimStyleOverride
- Text measurement from Stage 02
