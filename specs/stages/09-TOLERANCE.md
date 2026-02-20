# Stage 09: TOLERANCE (Feature Control Frame / GD&T)

## Overview

The TOLERANCE entity represents a Geometric Dimensioning and Tolerancing (GD&T) feature control frame. These are standardized annotation symbols used in mechanical engineering drawings to specify permissible variation in a part's geometry. They follow international standards ASME Y14.5 (US) and ISO 1101.

A feature control frame is a rectangular box divided into compartments. The first compartment contains a geometric characteristic symbol (e.g., position, flatness, perpendicularity). Subsequent compartments contain tolerance values and datum references. The frame can be connected to a LEADER entity that points to the feature being toleranced.

TOLERANCE entities depend heavily on the text layout capabilities from Stage 02, as each compartment contains text that must be measured and positioned. The GD&T symbols can be rendered using Unicode glyphs, custom drawing routines, or by referencing the `gdt.shx` font used by AutoCAD.

The target module for this stage is `ToleranceFrameRenderer.cs`.

---

## Domain Knowledge

### TOLERANCE Entity Group Codes

| Group Code | Field | Description |
|-----------|-------|-------------|
| 3 | Dimension style | DimStyle name reference |
| 10/20/30 | Insertion point | WCS location of the frame |
| 11/21/31 | Direction vector | X-axis direction for frame orientation (unit vector) |
| 1 | Content string | Encoded feature control frame content |

The TOLERANCE entity is relatively simple in DXF terms -- most of its complexity is in parsing the content string and laying out the frame compartments.

### Content String Format

The content string uses `%%v` as a delimiter between compartments. The format is:

```
{\\Fgdt;SYMBOL_CHAR}%%vTOLERANCE_VALUE%%vDATUM_A%%vDATUM_B%%vDATUM_C%%v...
```

**Parsing algorithm**:
1. Split the string by `%%v`
2. Each part is a compartment in the feature control frame
3. The first compartment typically contains a GD&T symbol encoded as `{\\Fgdt;X}` where `X` is a character that maps to a symbol
4. Subsequent compartments contain tolerance values and datum letters

**Example content strings**:
```
{\\Fgdt;j}%%v0.05%%vA%%v%%v%%v%%v
```
This means: Position symbol, tolerance 0.05, datum A.

```
{\\Fgdt;f}%%v0.01%%v%%v%%v%%v%%v
```
This means: Flatness symbol, tolerance 0.01, no datum references.

### GD&T Symbols (gdt.shx Character Map)

The `gdt.shx` font maps single characters to GD&T symbols:

| Char | Symbol | Unicode | Description |
|------|--------|---------|-------------|
| j | Position | U+2316 | Circle with crosshairs |
| e | Flatness | U+23E5 | Parallelogram |
| a | Straightness | U+2014 | Horizontal line |
| g | Circularity (Roundness) | U+25CB | Circle |
| h | Cylindricity | U+232D | Cylinder outline |
| b | Perpendicularity | U+27C2 | Perpendicular symbol |
| f | Parallelism | U+2225 | Double vertical bars |
| d | Angularity | U+2220 | Angle symbol |
| r | Circular Runout | U+2197 | Arrow at angle |
| t | Total Runout | U+2197 U+2197 | Double arrow |
| i | Concentricity | U+25CE | Bullseye |
| k | Symmetry | U+232F | Three horizontal lines |
| c | Profile of a Line | U+2312 | Arc segment |
| n | Diameter | U+2300 | Circle with slash (also %%c in DXF) |
| m | Material Condition (MMC) | U+24C2 | M in circle |
| l | Material Condition (LMC) | U+24C1 | L in circle |
| p | Projected Tolerance Zone | U+24C5 | P in circle |
| s | (Regardless of Feature Size) | U+24C8 | S in circle |

### Feature Control Frame Layout

A feature control frame is a horizontal row of rectangular compartments:

```
+--------+----------+-------+-------+-------+
| Symbol | Tol Value| Dat A | Dat B | Dat C |
+--------+----------+-------+-------+-------+
```

**Layout rules**:
- Each compartment has a fixed height (typically 2 * text height)
- Each compartment's width is determined by its text content plus padding (DIMGAP on each side)
- The symbol compartment is typically square (width = height)
- Compartments are drawn left to right
- All compartments share the same height
- Borders are drawn around each compartment
- Text is vertically centered within each compartment

**Stacked (two-row) frames**: A feature control frame can have two rows, representing a composite tolerance:
```
+--------+----------+-------+-------+-------+
| Symbol | Tol1     | Dat A | Dat B | Dat C |
+--------+----------+-------+-------+-------+
| Symbol | Tol2     | Dat A | Dat B | Dat C |
+--------+----------+-------+-------+-------+
```

The two rows share the same compartment structure but may have different values.

### Tolerance Value Format

The tolerance value compartment can contain:
- A simple number: `0.05`
- A diameter symbol prefix: `%%c0.05` (meaning the tolerance zone is cylindrical)
- Material condition modifier: `0.05%%cm` (MMC), `0.05%%cl` (LMC)
- Combined: `%%c0.05%%cm` (diameter tolerance at MMC)

The `%%c` escape is the DXF encoding for the diameter symbol (U+2300).
The `%%p` escape is the plus/minus symbol (U+00B1).

### Datum References

Datum compartments contain capital letters (A, B, C) identifying datum features. They may also include:
- Material condition modifiers: `A%%cm` (datum A at MMC)
- Compound datums: `A-B` (common datum established by datums A and B)

Empty datum compartments are not rendered (the frame stops at the last non-empty datum).

### Dimension Style Influence

The TOLERANCE entity references a DimStyle (group 3) which provides:
- **DIMTXT** (group 140): Text height for the frame content
- **DIMGAP** (group 147): Padding inside each compartment
- **DIMCLRT** (group 178): Text color
- **DIMTXSTY**: Text style (font) for the content

### Orientation

The TOLERANCE entity has a direction vector (group 11/21/31) that defines the X-axis of the frame. The frame is drawn along this direction, with the Y-axis perpendicular to it. This allows the frame to be rotated to any angle.

If the direction vector is (1, 0, 0), the frame is horizontal (the default).

### Connection to LEADER

A TOLERANCE entity is often connected to a LEADER entity. The LEADER's `AnnotationHandle` property points to the TOLERANCE entity. The LEADER provides the pointer line from the feature to the frame; the TOLERANCE provides the frame content.

When rendering, if a TOLERANCE has an associated LEADER, the frame should be positioned at the LEADER's landing point. If no LEADER is present, the frame is positioned at the TOLERANCE's insertion point.

---

## External Reference Code

### HOOPS Visualize GD&T Documentation
- **URL**: https://docs.techsoft3d.com/visualize/latest/build/prog_guide/ht_visualize_segments.html (search for GD&T)
- **What to study**: How a commercial CAD visualization library renders GD&T symbols. Provides guidance on symbol drawing primitives and layout conventions.

### AutoCAD gdt.shx Font
- **What to study**: The gdt.shx font defines the exact geometry of each GD&T symbol. Since this font is proprietary and not distributable, the symbols must be rendered programmatically or mapped to Unicode equivalents.

### ASME Y14.5-2018 Standard
- **What to study**: The official standard for GD&T symbols, frame layout rules, and compartment sizing. Key rules:
  - Frame height = 2 * text height
  - Minimum compartment width = text width + 2 * gap
  - Symbol compartment is square
  - Text is baseline-centered vertically within compartments

### ezdxf Tolerance Entity (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/entities/tolerance.py
- **What to study**: Entity class definition. ezdxf stores the content string and provides basic property access. No rendering/decomposition is implemented (it is a "complex entity" left to backends).

### ezdxf Drawing Frontend (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/addons/drawing/frontend.py
- **What to study**: How the frontend handles TOLERANCE entities (if at all). As of recent versions, TOLERANCE rendering is minimal in ezdxf.

---

## Step-by-Step Implementation Plan

### Step 1: Create ToleranceFrameRenderer Class

**What**: The main class for converting TOLERANCE entities to render primitives.

**Key structure**:
```csharp
class ToleranceFrameRenderer
{
    private TextLayoutEngine _textEngine;
    private PropertyResolver _resolver;
    private RenderLog _log;

    List<RenderNode> RenderTolerance(Tolerance tolerance, Matrix4 parentTransform)
    {
        // 1. Parse the content string
        var frame = ParseContentString(tolerance.Content);
        if (frame == null || frame.Rows.Count == 0)
        {
            _log.Skip(tolerance, "empty or unparseable content");
            return empty;
        }

        // 2. Resolve DimStyle properties
        var dimProps = ResolveDimStyle(tolerance);

        // 3. Compute frame layout (compartment sizes)
        var layout = ComputeLayout(frame, dimProps);

        // 4. Generate render primitives
        var nodes = new List<RenderNode>();

        // 5. Compute frame transform (position + orientation)
        var frameTransform = ComputeFrameTransform(tolerance, parentTransform);

        // 6. Render frame borders
        nodes.AddRange(RenderBorders(layout, frameTransform, dimProps));

        // 7. Render content (symbols + text)
        nodes.AddRange(RenderContent(frame, layout, frameTransform, dimProps));

        return nodes;
    }
}
```

**Input**: Tolerance entity + parent transform.

**Output**: List of PathNode (borders) + TextRunNode (text) + PathNode (symbols).

**Edge cases**:
- Empty content string: skip
- Invalid content format: log warning, render as plain text

---

### Step 2: Implement Content String Parser

**What**: Parse the TOLERANCE content string into a structured representation.

**Algorithm**:
```csharp
class ToleranceContentParser
{
    FeatureControlFrame Parse(string content)
    {
        if (string.IsNullOrEmpty(content)) return null;

        var frame = new FeatureControlFrame();

        // Split into rows (some frames have two rows separated by a special delimiter)
        // Check for second row: content may contain a second frame row
        string[] rows = SplitIntoRows(content);

        foreach (string rowContent in rows)
        {
            var row = new FrameRow();

            // Split by %%v delimiter
            string[] parts = rowContent.Split(new[] { "%%v" }, StringSplitOptions.None);

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (string.IsNullOrEmpty(part)) continue;

                var compartment = new Compartment();

                if (i == 0)
                {
                    // First compartment: parse GD&T symbol
                    compartment = ParseSymbolCompartment(part);
                }
                else if (i == 1)
                {
                    // Second compartment: tolerance value
                    compartment = ParseToleranceCompartment(part);
                }
                else
                {
                    // Subsequent compartments: datum references
                    compartment = ParseDatumCompartment(part);
                }

                row.Compartments.Add(compartment);
            }

            frame.Rows.Add(row);
        }

        return frame;
    }

    Compartment ParseSymbolCompartment(string text)
    {
        // Look for {\\Fgdt;X} pattern
        var match = Regex.Match(text, @"\{\\Fgdt;(.)\}");
        if (match.Success)
        {
            char symbolChar = match.Groups[1].Value[0];
            return new Compartment
            {
                Type = CompartmentType.Symbol,
                SymbolChar = symbolChar,
                GdtSymbol = MapCharToSymbol(symbolChar),
            };
        }

        // Plain text symbol (unusual but possible)
        return new Compartment { Type = CompartmentType.Text, Text = text };
    }

    Compartment ParseToleranceCompartment(string text)
    {
        var compartment = new Compartment { Type = CompartmentType.Tolerance };

        // Check for diameter prefix
        if (text.StartsWith("%%c") || text.Contains("\u2300"))
        {
            compartment.HasDiameterPrefix = true;
            text = text.Replace("%%c", "").Replace("\u2300", "");
        }

        // Check for material condition suffix
        if (text.EndsWith("%%cm"))
        {
            compartment.MaterialCondition = MaterialCondition.MMC;
            text = text[..^4];
        }
        else if (text.EndsWith("%%cl"))
        {
            compartment.MaterialCondition = MaterialCondition.LMC;
            text = text[..^4];
        }
        else if (text.EndsWith("%%cs"))
        {
            compartment.MaterialCondition = MaterialCondition.RFS;
            text = text[..^4];
        }

        compartment.Text = text; // The numeric tolerance value
        return compartment;
    }

    Compartment ParseDatumCompartment(string text)
    {
        var compartment = new Compartment { Type = CompartmentType.Datum };

        // Check for material condition on datum
        if (text.EndsWith("%%cm"))
        {
            compartment.MaterialCondition = MaterialCondition.MMC;
            text = text[..^4];
        }
        else if (text.EndsWith("%%cl"))
        {
            compartment.MaterialCondition = MaterialCondition.LMC;
            text = text[..^4];
        }

        compartment.Text = text; // The datum letter(s)
        return compartment;
    }

    GdtSymbol MapCharToSymbol(char c)
    {
        return c switch
        {
            'j' => GdtSymbol.Position,
            'e' => GdtSymbol.Flatness,
            'a' => GdtSymbol.Straightness,
            'g' => GdtSymbol.Circularity,
            'h' => GdtSymbol.Cylindricity,
            'b' => GdtSymbol.Perpendicularity,
            'f' => GdtSymbol.Parallelism,
            'd' => GdtSymbol.Angularity,
            'r' => GdtSymbol.CircularRunout,
            't' => GdtSymbol.TotalRunout,
            'i' => GdtSymbol.Concentricity,
            'k' => GdtSymbol.Symmetry,
            'c' => GdtSymbol.ProfileOfALine,
            _ => GdtSymbol.Unknown,
        };
    }
}
```

**Input**: Content string from TOLERANCE entity.

**Output**: Structured `FeatureControlFrame` with rows and compartments.

**Edge cases**:
- Multiple `%%v` delimiters with empty parts between them: skip empty compartments
- Content without `{\\Fgdt;...}`: treat as plain text
- Unknown symbol characters: render as the character itself or a question mark
- Content with escaped sequences other than `%%v`, `%%c`: handle or pass through

---

### Step 3: Implement Frame Layout Computation

**What**: Compute the size and position of each compartment in the frame.

**Algorithm**:
```csharp
class FrameLayout
{
    List<RowLayout> Rows;
    double TotalWidth;
    double TotalHeight;
}

class RowLayout
{
    List<CompartmentLayout> Compartments;
    double Width;
    double Height;
}

class CompartmentLayout
{
    double X; // Left edge X relative to frame origin
    double Y; // Top edge Y relative to frame origin
    double Width;
    double Height;
    Compartment Content;
}

FrameLayout ComputeLayout(FeatureControlFrame frame, DimProperties dimProps)
{
    double textHeight = dimProps.TextHeight;
    double gap = dimProps.TextGap;
    double rowHeight = 2.0 * textHeight; // Standard: frame height = 2x text height

    var layout = new FrameLayout();

    double currentY = 0;

    foreach (var row in frame.Rows)
    {
        var rowLayout = new RowLayout { Height = rowHeight };
        double currentX = 0;

        foreach (var compartment in row.Compartments)
        {
            double compartmentWidth;

            if (compartment.Type == CompartmentType.Symbol)
            {
                // Symbol compartment is square
                compartmentWidth = rowHeight;
            }
            else
            {
                // Measure text width
                string displayText = GetDisplayText(compartment);
                double textWidth = _textEngine.MeasureWidth(displayText,
                    dimProps.FontName, textHeight);

                // Add diameter prefix width if present
                if (compartment.HasDiameterPrefix)
                {
                    textWidth += _textEngine.MeasureWidth("\u2300",
                        dimProps.FontName, textHeight);
                }

                // Add material condition modifier width
                if (compartment.MaterialCondition != MaterialCondition.None)
                {
                    textWidth += _textEngine.MeasureWidth(
                        GetModifierText(compartment.MaterialCondition),
                        dimProps.FontName, textHeight);
                }

                compartmentWidth = textWidth + 2 * gap;

                // Ensure minimum width
                compartmentWidth = Math.Max(compartmentWidth, rowHeight * 0.75);
            }

            rowLayout.Compartments.Add(new CompartmentLayout
            {
                X = currentX,
                Y = currentY,
                Width = compartmentWidth,
                Height = rowHeight,
                Content = compartment,
            });

            currentX += compartmentWidth;
        }

        rowLayout.Width = currentX;
        layout.Rows.Add(rowLayout);
        currentY += rowHeight;
    }

    layout.TotalWidth = layout.Rows.Max(r => r.Width);
    layout.TotalHeight = currentY;

    return layout;
}
```

**Input**: Parsed frame content, DimStyle properties.

**Output**: Layout with computed positions and sizes for each compartment.

**Edge cases**:
- Frame with only a symbol (no tolerance or datums): single compartment
- Very wide tolerance value: compartment expands to fit
- Rows with different numbers of compartments: each row is independent
- Zero text height: use default 2.5

---

### Step 4: Implement GD&T Symbol Drawing

**What**: Draw the GD&T geometric characteristic symbols.

**Algorithm** (programmatic drawing):
```csharp
List<RenderNode> DrawGdtSymbol(GdtSymbol symbol, double x, double y,
    double size, Color color, Matrix4 transform)
{
    var nodes = new List<RenderNode>();
    double cx = x + size / 2; // Center of symbol compartment
    double cy = y + size / 2;
    double r = size * 0.35;   // Symbol radius (35% of compartment size)
    double strokeWidth = size * 0.05; // Line thickness

    var stroke = new StrokeStyle { Color = color, Width = strokeWidth };

    switch (symbol)
    {
        case GdtSymbol.Position: // Circle with crosshairs
            // Circle
            nodes.Add(CreateCirclePath(cx, cy, r, stroke, transform));
            // Vertical crosshair
            nodes.Add(CreateLinePath(cx, cy - r * 1.3, cx, cy + r * 1.3, stroke, transform));
            // Horizontal crosshair
            nodes.Add(CreateLinePath(cx - r * 1.3, cy, cx + r * 1.3, cy, stroke, transform));
            break;

        case GdtSymbol.Flatness: // Parallelogram
            double hw = r * 0.8;
            double hh = r * 0.4;
            double skew = r * 0.3;
            nodes.Add(CreatePolygonPath(new[]
            {
                new XY(cx - hw + skew, cy - hh),
                new XY(cx + hw + skew, cy - hh),
                new XY(cx + hw - skew, cy + hh),
                new XY(cx - hw - skew, cy + hh),
            }, stroke, transform));
            break;

        case GdtSymbol.Straightness: // Horizontal line
            nodes.Add(CreateLinePath(cx - r, cy, cx + r, cy, stroke, transform));
            break;

        case GdtSymbol.Circularity: // Circle
            nodes.Add(CreateCirclePath(cx, cy, r, stroke, transform));
            break;

        case GdtSymbol.Cylindricity: // Circle with two horizontal tangent lines
            nodes.Add(CreateCirclePath(cx, cy, r * 0.7, stroke, transform));
            nodes.Add(CreateLinePath(cx - r, cy - r * 0.7, cx - r, cy + r * 0.7, stroke, transform));
            nodes.Add(CreateLinePath(cx + r, cy - r * 0.7, cx + r, cy + r * 0.7, stroke, transform));
            break;

        case GdtSymbol.Perpendicularity: // Perpendicular symbol (inverted T)
            nodes.Add(CreateLinePath(cx, cy - r, cx, cy + r * 0.5, stroke, transform));
            nodes.Add(CreateLinePath(cx - r * 0.7, cy + r * 0.5, cx + r * 0.7, cy + r * 0.5, stroke, transform));
            break;

        case GdtSymbol.Parallelism: // Two parallel slanted lines
            double offset = r * 0.25;
            nodes.Add(CreateLinePath(cx - offset, cy - r, cx - offset, cy + r, stroke, transform));
            nodes.Add(CreateLinePath(cx + offset, cy - r, cx + offset, cy + r, stroke, transform));
            break;

        case GdtSymbol.Angularity: // Angle symbol
            nodes.Add(CreateLinePath(cx - r, cy - r * 0.5, cx + r * 0.5, cy + r * 0.8, stroke, transform));
            nodes.Add(CreateLinePath(cx - r, cy - r * 0.5, cx + r, cy - r * 0.5, stroke, transform));
            break;

        case GdtSymbol.CircularRunout: // Arrow at angle
            // Simplified: diagonal line with arrow
            nodes.Add(CreateLinePath(cx - r, cy - r, cx + r, cy + r, stroke, transform));
            // Arrow tip at top-right
            nodes.Add(CreateArrowTip(cx + r, cy + r, -r, -r, r * 0.3, stroke, transform));
            break;

        case GdtSymbol.TotalRunout: // Double arrow
            nodes.Add(CreateLinePath(cx - r, cy - r, cx + r, cy + r, stroke, transform));
            nodes.Add(CreateArrowTip(cx + r, cy + r, -r, -r, r * 0.3, stroke, transform));
            nodes.Add(CreateLinePath(cx - r * 0.7, cy - r, cx + r * 0.7, cy + r, stroke, transform));
            break;

        case GdtSymbol.Concentricity: // Bullseye (two concentric circles)
            nodes.Add(CreateCirclePath(cx, cy, r, stroke, transform));
            nodes.Add(CreateCirclePath(cx, cy, r * 0.4, stroke, transform));
            break;

        case GdtSymbol.Symmetry: // Three horizontal lines
            nodes.Add(CreateLinePath(cx - r, cy - r * 0.5, cx + r, cy - r * 0.5, stroke, transform));
            nodes.Add(CreateLinePath(cx - r, cy, cx + r, cy, stroke, transform));
            nodes.Add(CreateLinePath(cx - r, cy + r * 0.5, cx + r, cy + r * 0.5, stroke, transform));
            break;

        case GdtSymbol.ProfileOfALine: // Arc (semicircle)
            nodes.Add(CreateArcPath(cx, cy + r * 0.3, r * 0.8,
                Math.PI * 0.15, Math.PI * 0.85, stroke, transform));
            break;

        default: // Unknown: render character as text
            nodes.Add(new TextRunNode
            {
                Text = "?",
                Position = Transform(new XY(cx, cy), transform),
                FontSize = size * 0.6,
                FontName = "Arial",
                Color = color,
            });
            break;
    }

    return nodes;
}
```

**Alternative approach**: Use Unicode characters instead of programmatic drawing:
```csharp
string GetUnicodeSymbol(GdtSymbol symbol)
{
    return symbol switch
    {
        GdtSymbol.Position => "\u2316",        // Position indicator
        GdtSymbol.Flatness => "\u23E5",        // Flatness
        GdtSymbol.Perpendicularity => "\u27C2", // Perpendicular
        GdtSymbol.Parallelism => "\u2225",      // Parallel to
        GdtSymbol.Circularity => "\u25CB",      // Circle
        GdtSymbol.Angularity => "\u2220",       // Angle
        GdtSymbol.Concentricity => "\u25CE",    // Bullseye
        _ => "?",
    };
}
```

Unicode approach is simpler but depends on font support. The programmatic approach is more reliable.

**Input**: Symbol type, position, size, color, transform.

**Output**: PathNode primitives forming the symbol.

**Edge cases**:
- Unknown symbol: render as question mark or empty compartment
- Very small symbol (text height < 1): simplify geometry

---

### Step 5: Implement Border Rendering

**What**: Draw the rectangular borders around each compartment.

**Algorithm**:
```csharp
List<RenderNode> RenderBorders(FrameLayout layout, Matrix4 transform,
    DimProperties dimProps)
{
    var nodes = new List<RenderNode>();
    var stroke = new StrokeStyle
    {
        Color = dimProps.DimLineColor,
        Width = ResolveLineweight(dimProps),
    };

    foreach (var row in layout.Rows)
    {
        foreach (var comp in row.Compartments)
        {
            // Draw compartment rectangle
            var rect = new PathNode
            {
                Segments =
                {
                    MoveTo(Transform(new XY(comp.X, comp.Y), transform)),
                    LineTo(Transform(new XY(comp.X + comp.Width, comp.Y), transform)),
                    LineTo(Transform(new XY(comp.X + comp.Width, comp.Y + comp.Height), transform)),
                    LineTo(Transform(new XY(comp.X, comp.Y + comp.Height), transform)),
                    new CloseSegment(),
                },
                Stroke = stroke,
            };
            nodes.Add(rect);
        }
    }

    return nodes;
}
```

**Input**: Frame layout, transform, style properties.

**Output**: PathNode for each compartment border.

**Edge cases**:
- Adjacent compartments share edges: the shared border is drawn twice, which is visually identical but slightly wasteful. Optimization: draw the outer frame and internal dividers separately.

---

### Step 6: Implement Content Text Rendering

**What**: Render the text content of each compartment (tolerance values, datum letters).

**Algorithm**:
```csharp
List<RenderNode> RenderContent(FeatureControlFrame frame, FrameLayout layout,
    Matrix4 transform, DimProperties dimProps)
{
    var nodes = new List<RenderNode>();

    for (int rowIdx = 0; rowIdx < layout.Rows.Count; rowIdx++)
    {
        var row = layout.Rows[rowIdx];

        for (int compIdx = 0; compIdx < row.Compartments.Count; compIdx++)
        {
            var comp = row.Compartments[compIdx];
            var content = comp.Content;

            if (content.Type == CompartmentType.Symbol)
            {
                // Draw GD&T symbol
                nodes.AddRange(DrawGdtSymbol(content.GdtSymbol,
                    comp.X, comp.Y, comp.Height, dimProps.TextColor, transform));
            }
            else
            {
                // Draw text content
                string displayText = BuildDisplayText(content);

                if (!string.IsNullOrEmpty(displayText))
                {
                    // Center text in compartment
                    double textWidth = _textEngine.MeasureWidth(displayText,
                        dimProps.FontName, dimProps.TextHeight);
                    double textX = comp.X + (comp.Width - textWidth) / 2;
                    double textY = comp.Y + (comp.Height - dimProps.TextHeight) / 2;

                    var textPos = Transform(new XY(textX, textY), transform);

                    nodes.Add(new TextRunNode
                    {
                        Text = displayText,
                        FontName = dimProps.FontName,
                        FontSize = dimProps.TextHeight,
                        Position = textPos,
                        Rotation = ExtractRotation(transform),
                        Color = dimProps.TextColor,
                        SourceHandle = 0, // No individual handle for compartment text
                    });
                }
            }
        }
    }

    return nodes;
}

string BuildDisplayText(Compartment compartment)
{
    string text = "";

    if (compartment.HasDiameterPrefix)
        text += "\u2300"; // Diameter symbol

    text += compartment.Text;

    if (compartment.MaterialCondition != MaterialCondition.None)
    {
        text += compartment.MaterialCondition switch
        {
            MaterialCondition.MMC => "\u24C2", // Circled M
            MaterialCondition.LMC => "\u24C1", // Circled L
            MaterialCondition.RFS => "\u24C8", // Circled S
            _ => "",
        };
    }

    return text;
}
```

**Input**: Frame content, layout, transform, style.

**Output**: TextRunNode for each compartment's text.

**Edge cases**:
- Unicode symbols not available in the chosen font: fall back to ASCII approximation (e.g., "dia" for diameter, "M" for MMC)
- Very long tolerance values: compartment width already accounts for this in layout step

---

### Step 7: Implement Frame Transform

**What**: Compute the transformation matrix for the entire frame based on insertion point and direction.

**Algorithm**:
```csharp
Matrix4 ComputeFrameTransform(Tolerance tolerance, Matrix4 parentTransform)
{
    XYZ insertPoint = tolerance.InsertionPoint;
    XYZ direction = tolerance.DirectionVector;

    // Compute rotation from direction vector
    double rotation = Math.Atan2(direction.Y, direction.X);

    // Build transform: translate to insertion point, rotate to match direction
    var rotateM = Matrix4.CreateFromAxisAngle(XYZ.AxisZ, rotation);
    var translateM = Matrix4.CreateTranslation(insertPoint);

    return parentTransform * translateM * rotateM;
}
```

**Input**: Tolerance entity, parent transform.

**Output**: Frame transform matrix.

**Edge cases**:
- Direction vector is zero: use default horizontal (1, 0, 0)
- Direction vector is not unit length: normalize it

---

### Step 8: Handle LEADER Connection

**What**: If the TOLERANCE is associated with a LEADER entity, ensure correct positioning.

**Algorithm**:
```csharp
// In EntityFrontend or wherever LEADER is processed:
// When rendering a LEADER with an annotation handle:
if (leader.AnnotationHandle != 0)
{
    var annotation = document.GetObjectByHandle(leader.AnnotationHandle);
    if (annotation is Tolerance tolerance)
    {
        // The TOLERANCE's insertion point should align with the LEADER's landing point
        // Usually this is already the case, but verify
    }
}
```

The LEADER entity itself handles drawing the leader line. The TOLERANCE entity draws the frame at its insertion point. If they are properly linked, the frame appears at the end of the leader.

---

### Step 9: Integrate into EntityFrontend

**What**: Add the TOLERANCE case to the EntityFrontend dispatcher.

```csharp
case Tolerance tolerance:
    return _toleranceRenderer.RenderTolerance(tolerance, worldTransform);
```

---

## Testing Strategy

### Unit Tests

1. **Content string parsing**: `"{\\Fgdt;j}%%v0.05%%vA%%v%%v%%v%%v"` -> Position symbol, tolerance 0.05, datum A.
2. **Symbol mapping**: Character 'j' -> Position, 'e' -> Flatness, 'b' -> Perpendicularity.
3. **Tolerance with diameter**: `"%%c0.05"` -> HasDiameterPrefix=true, Text="0.05".
4. **Material condition**: `"0.05%%cm"` -> MaterialCondition=MMC, Text="0.05".
5. **Datum with modifier**: `"A%%cm"` -> Text="A", MaterialCondition=MMC.
6. **Empty compartments**: `"%%v%%v%%v"` -> Skipped after last non-empty.
7. **Layout computation**: Frame with 3 compartments. Verify total width = sum of compartment widths.
8. **Symbol compartment is square**: Height = 2 * textHeight, Width = Height.
9. **Text centering**: Text centered horizontally and vertically in compartment.
10. **Frame orientation**: Direction (0,1,0) -> frame rotated 90 degrees.
11. **Default direction**: Direction (1,0,0) -> horizontal frame.
12. **Two-row frame**: Two rows of compartments stacked vertically.
13. **Position symbol drawing**: Verify circle with crosshairs geometry.
14. **Flatness symbol drawing**: Verify parallelogram geometry.

### Integration Tests

15. **Simple TOLERANCE DXF**: Position tolerance with datum A. Compare with oracle.
16. **Multiple symbols**: DXF with flatness, perpendicularity, and position tolerances.
17. **TOLERANCE with LEADER**: Leader pointing to a feature with tolerance frame at landing.
18. **Rotated TOLERANCE**: Frame at 45-degree angle.
19. **TOLERANCE in INSERT**: Tolerance frame inside a block.

### Test DXF Generation

```python
import ezdxf

doc = ezdxf.new()
msp = doc.modelspace()

# Note: ezdxf tolerance creation is limited
# Use direct DXF writing for test cases
# Or create via AutoCAD and save as DXF

doc.saveas('test_tolerance.dxf')
```

Since ezdxf's TOLERANCE creation API is limited, test DXF files are best created using AutoCAD or by directly constructing the DXF entities programmatically.

---

## Dependencies

### Depends On
- **Stage 00 (Render Infrastructure)**: PathNode, TextRunNode, Transform, PropertyResolver
- **Stage 02 (TEXT/MTEXT)**: TextLayoutEngine for measuring text widths and rendering text in compartments
- **Stage 03 (DIMENSIONS)**: DimStyle resolution (shares the same DimStyle variable infrastructure)

### Enables
- No other stages depend on TOLERANCE

### External Dependencies
- ACadSharp `Tolerance` entity class
- Text measurement from Stage 02 (TextLayoutEngine/TextMetrics)
- DimStyle resolution infrastructure from Stage 03
