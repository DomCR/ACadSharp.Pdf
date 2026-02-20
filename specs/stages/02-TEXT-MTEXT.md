# Stage 02: TEXT / MTEXT / ATT (Text Layout Engine)

## Overview

TEXT and MTEXT are the two primary text entities in DXF. TEXT represents a single-line text string with precise alignment and positioning. MTEXT represents multi-line formatted text with word wrapping, inline formatting codes, and paragraph control. ATTRIB and ATTDEF entities (handled partly in Stage 01) inherit from TEXT and share its alignment and rendering logic.

Correct text rendering is the single most impactful missing feature. It directly affects DIMENSION text (Stage 03), MULTILEADER text content (Stage 04), TOLERANCE frames (Stage 09), and any drawing that contains annotations, labels, or notes. Text positioning errors (wrong alignment point, missing rotation, incorrect baseline) produce the most visible discrepancies in oracle-driven validation.

The main challenge is fonts: DXF drawings often use SHX fonts (AutoCAD's proprietary vector font format) which are not available outside AutoCAD. A substitution policy mapping SHX to TTF equivalents is required. Additionally, computing accurate text metrics (character widths, ascent, descent, cap height) requires access to the actual font files or a reasonable approximation.

The target module for this stage is `TextLayoutEngine.cs`.

---

## Domain Knowledge

### Conventions used in implementation

- In DXF, angles are stored in degrees. In ACadSharp, many angle-valued properties are exposed as **radians** when tagged with `DxfReferenceType.IsAngle` (e.g., `TextEntity.Rotation`, `TextEntity.ObliqueAngle`).
- Follow Stage 00 conventions for transforms: prefer `CSMath.Matrix4`/`CSMath.Transform` and apply points as `matrix * point`.

### TEXT Entity Group Codes

| Group Code | Field | Description |
|-----------|-------|-------------|
| 1 | Value | The text string content |
| 10/20/30 | Insertion point | First alignment point (OCS) |
| 11/21/31 | Second alignment point | Used when h/v alignment is non-default (OCS) |
| 40 | Height | Text height in drawing units |
| 50 | Rotation | DXF stores degrees; ACadSharp exposes radians via `TextEntity.Rotation` |
| 51 | Oblique angle | DXF stores degrees; ACadSharp exposes radians via `TextEntity.ObliqueAngle` |
| 41 | Width factor | Relative width scaling (1.0 = normal) |
| 7 | Style name | Reference to TextStyle table entry |
| 71 | Generation flags | Bit 2 = backward (mirror X), bit 4 = upside-down (mirror Y) |
| 72 | Horizontal alignment | 0=Left, 1=Center, 2=Right, 3=Aligned, 4=Middle, 5=Fit |
| 73 | Vertical alignment | 0=Baseline, 1=Bottom, 2=Middle, 3=Top |

### TEXT Alignment Rules

The alignment system uses two points and two mode codes:

**When `HorizontalAlignment == 0` AND `VerticalAlignment == 0`** (the default Left-Baseline):
- Use the **first alignment point** (group 10/20/30) as the insertion point
- The second alignment point (group 11/21/31) is ignored

**When `HorizontalAlignment != 0` OR `VerticalAlignment != 0`**:
- Use the **second alignment point** (group 11/21/31) as the alignment reference
- The first alignment point is ignored
- The text is positioned so that the specified alignment point on the text bounding box coincides with the second alignment point

**Horizontal modes**:
| Value | Mode | Behavior |
|-------|------|----------|
| 0 | Left | Left edge of text at alignment point |
| 1 | Center | Horizontal center at alignment point |
| 2 | Right | Right edge at alignment point |
| 3 | Aligned | Text stretched between first and second alignment points (both used) |
| 4 | Middle | Center of text bbox (not baseline center, but true geometric center) |
| 5 | Fit | Text height preserved, width adjusted to fit between first and second points |

**Vertical modes** (when horizontal is 0/1/2):
| Value | Mode | Behavior |
|-------|------|----------|
| 0 | Baseline | Text sits on the baseline |
| 1 | Bottom | Bottom of descenders at alignment point |
| 2 | Middle | Vertical center at alignment point |
| 3 | Top | Top of ascenders/cap height at alignment point |

**Special modes Aligned (3) and Fit (5)**:
- Both use the FIRST and SECOND alignment points
- **Aligned**: text is rotated to the angle between the two points, and scaled uniformly to fit the distance
- **Fit**: text is rotated to the angle between the two points, height is preserved, but width factor is adjusted to span the distance

### Generation Flags

- **Bit 2 (backward)**: Mirror the text horizontally (left-right flip). In PDF, apply a negative X scale to the text matrix.
- **Bit 4 (upside-down)**: Mirror the text vertically (top-bottom flip). In PDF, apply a negative Y scale.

These are rarely used but must be supported for correctness.

### MTEXT Entity Group Codes

| Group Code | Field | Description |
|-----------|-------|-------------|
| 1 | Content | Text content with inline formatting codes |
| 3 | Additional content | Continuation of content (for long text) |
| 10/20/30 | Insertion point | Attachment point location (WCS for MTEXT) |
| 40 | Text height | Default character height |
| 41 | Reference rectangle width | Width for word wrapping (0 = no wrapping) |
| 50 | Rotation | DXF stores degrees, but ACadSharp’s current `MText` model derives rotation from `AlignmentPoint` and marks group 50 as ignored |
| 71 | Attachment point | 1-9, defines which point of the text box is at the insertion point |
| 72 | Drawing direction | 1=Left-to-right, 3=Top-to-bottom, 5=By style |
| 44 | Line spacing factor | Multiplier for default line spacing |
| 73 | Line spacing style | 1=At Least (minimum), 2=Exact |

### MTEXT Attachment Points

The attachment point defines which corner/edge of the text bounding box is at the insertion point:

```
TL(1)  TC(2)  TR(3)
ML(4)  MC(5)  MR(6)
BL(7)  BC(8)  BR(9)
```

- T = Top, M = Middle (vertical center), B = Bottom
- L = Left, C = Center (horizontal), R = Right

### MTEXT Formatting Codes

MTEXT content uses inline formatting codes (backslash sequences):

| Code | Meaning | Example |
|------|---------|---------|
| `\P` | Paragraph break (new line) | `Line1\PLine2` |
| `\N` | New column (in column mode) | |
| `\\` | Literal backslash | |
| `\{` / `\}` | Literal brace | |
| `\~` | Non-breaking space | |
| `\O` | Toggle overline on/off | `\OOverlined\o` |
| `\L` | Toggle underline on/off | `\LUnderlined\l` |
| `\K` | Toggle strikethrough on/off | |
| `\fFontName\|b1\|i1;` | Font change (name, bold, italic) | `\fArial\|b1;Bold text` |
| `\F` | Font file name | `\Ftxt.shx;` |
| `\Hvalue;` | Text height change | `\H2.5;` |
| `\Hvaluex;` | Text height as factor of current | `\H0.5x;` |
| `\Stext1^text2;` | Stacking (fraction): `^` for tolerance, `/` for fraction, `#` for diagonal | `\S1/2;` |
| `\Qangle;` | Oblique angle change | `\Q15;` |
| `\Wfactor;` | Width factor change | `\W0.8;` |
| `\Tvalue;` | Tracking (character spacing) | `\T1.5;` |
| `\Cvalue;` | Color change (ACI index) | `\C1;` (red) |
| `\pAlignment;` | Paragraph alignment | `\pi2;` (indent), `\pxql;` (left), `\pxqc;` (center), `\pxqr;` (right) |
| `{ }` | Grouping (scope delimiter) | `{\fArial;text}` |

### MTEXT Line Spacing

Default line spacing = `1.666 * text_height` (this factor comes from AutoCAD's internal convention).

With line spacing factor `f`:
- **At Least** (style 1): line spacing = max(f * 1.666 * height, content_height)
- **Exact** (style 2): line spacing = f * 1.666 * height (content may overlap)

Default factor = 1.0, giving line spacing = 1.666 * text_height.

### SHX to TTF Font Substitution

SHX fonts are AutoCAD-proprietary vector fonts. For PDF rendering, they must be substituted with equivalent TrueType fonts. Common mappings:

| SHX Font | TTF Substitute | Notes |
|----------|---------------|-------|
| txt.shx | Arial / Helvetica | Basic simplex font |
| simplex.shx | Arial | |
| romans.shx | Times New Roman | Roman simplex |
| romand.shx | Times New Roman | Roman duplex |
| romanc.shx | Times New Roman | Roman complex |
| romant.shx | Times New Roman | Roman triplex |
| isocp.shx | ISOCPEUR / Arial | ISO standard |
| isocp2.shx | ISOCPEUR / Arial | |
| isocp3.shx | ISOCPEUR / Arial | |
| isoct.shx | ISOCTEUR / Arial | ISO standard technical |
| isoct2.shx | ISOCTEUR / Arial | |
| monotxt.shx | Courier New | Monospaced |
| gothic.shx | Century Gothic | |
| gothicg.shx | Century Gothic | |
| gothice.shx | Century Gothic | |
| syastro.shx | Symbol | Astronomical symbols |
| symath.shx | Symbol | Math symbols |
| symap.shx | Symbol | Map symbols |
| symeteo.shx | Symbol | Meteorological |
| gdt.shx | (special handling) | GD&T symbols - see Stage 09 |
| amgdt.shx | (special handling) | GD&T symbols |

The substitution should be configurable via `PdfConfiguration` so users can provide custom mappings.

### Text Metrics

For correct positioning, the renderer needs font metrics:

- **Cap height**: Height of capital letters. In DXF, `TextHeight` is the cap height, NOT the em-square height.
- **Ascent**: Distance from baseline to top of tallest character (including accents)
- **Descent**: Distance from baseline to bottom of lowest descender (negative value)
- **Advance width**: Width of each character, needed for alignment and wrapping

**Approximation approach** (without full TTF parsing):
- Cap height ratio: approximately 0.72 * em_size for most fonts
- Average character width: approximately 0.6 * text_height for proportional fonts, 0.6 * text_height for monospaced
- Ascent: approximately 1.0 * text_height
- Descent: approximately -0.3 * text_height
- These approximations allow positioning but will have alignment errors of 5-15%

**Accurate approach** (recommended for production):
- Use a TTF/OTF parser (e.g., `Typography.OpenFont` NuGet package, or `SixLabors.Fonts`)
- Read the OS/2 table for ascent/descent/cap height
- Read the hmtx table for per-character advance widths
- Apply width factor and oblique angle to the metrics

### ATTRIB / ATTDEF Text Properties

ATTRIB and ATTDEF entities inherit from TEXT:
- Same alignment system (group 72/73 and dual insertion points)
- Tag name (group 2): identifies the attribute, not rendered
- Invisible flag (group 70, bit 0): if set, the attribute is not displayed
- Constant flag (group 70, bit 1): value cannot be changed per INSERT
- Verify flag (group 70, bit 2): prompt user for verification
- Preset flag (group 70, bit 3): insert without prompting

For rendering purposes, only the invisible flag matters -- all others are editing concerns.

Newer DXF versions support MTEXT-based attributes (the ATTRIB contains MTEXT formatting). Check for the presence of embedded MTEXT content in the ATTRIB entity.

---

## External Reference Code

### ezdxf text2path Add-on (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/addons/text2path.py
- **What to study**: How text entities are decomposed into path geometry. Shows the complete text-to-geometry pipeline including font loading, metric computation, and alignment offset calculation.

### ezdxf MText Parser (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/entities/mtext.py (and related modules)
- **What to study**: The `plain_mtext()` function that strips formatting codes, `MTextParser` that tokenizes MTEXT content into formatting commands and text runs.

### ezdxf Drawing Frontend Text Handling (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/addons/drawing/text.py
- **What to study**: `simplified_text_chunks()` function that breaks MTEXT into positioned text runs with resolved formatting. Shows how attachment points and line spacing are applied.

### dxfom MTEXT Parser (MIT License, npm)
- **URL**: https://github.com/nicknisi/dxfom (or search npm for `dxf-parser`)
- **What to study**: JavaScript implementation of MTEXT formatting code parsing. Good cross-reference for understanding the formatting code grammar.

### GDAL OGR DXF Text Handling (MIT/X-style License)
- **URL**: https://github.com/OSGeo/gdal/blob/master/ogr/ogrsf_frmts/dxf/ogrdxf_feature.cpp
- **What to study**: `TranslateTEXT()` and `TranslateMTEXT()` functions. GDAL converts text to point geometry with label styling (OGR style string), which shows how they resolve alignment and positioning.

### Existing ACadSharp.Pdf Text Code
- **File**: `/Users/rodio/my-projects/ACadSharp.Pdf/src/ACadSharp.Pdf/Core/IO/PdfPen.cs` lines 259-287
- **What to study**: The current `drawText()` method is minimal: it outputs BT/ET blocks with font selection, position, and text string. It does not handle alignment (always uses InsertPoint), rotation, or MTEXT formatting codes. This is what needs to be replaced.

---

## Step-by-Step Implementation Plan

### Step 1: Create the TextLayoutEngine Class

**What**: A class responsible for converting TEXT and MTEXT entities into positioned, styled `TextRunNode` primitives.

**Key structure**:
```csharp
class TextLayoutEngine
{
    private FontResolver _fontResolver;
    private TextMetrics _metrics;

    // Convert a TEXT entity to render primitives
    List<RenderNode> LayoutText(TextEntity text, Matrix4 parentTransform, StrokeStyle style);

    // Convert an MTEXT entity to render primitives
    List<RenderNode> LayoutMText(MText mtext, Matrix4 parentTransform, StrokeStyle style);

    // Convert an ATTRIB to render primitives (delegates to LayoutText with ATTRIB properties)
    List<RenderNode> LayoutAttrib(AttributeEntity attrib, Matrix4 parentTransform, StrokeStyle style);
}
```

**Input**: Text entity, parent transform, resolved style.

**Output**: List of TextRunNode (and possibly PathNode for underline/overline/strikethrough).

**Edge cases**: Empty text string (skip), text height of 0 (skip).

---

### Step 2: Implement TEXT Alignment Point Resolution

**What**: Determine the correct anchor point and offset for a TEXT entity based on its alignment modes.

**Algorithm**:
```csharp
(XYZ anchorPoint, XY offset) ResolveTextAlignment(TextEntity text, double textWidth, double textHeight)
{
    int hAlign = text.HorizontalAlignment; // 0-5
    int vAlign = text.VerticalAlignment;   // 0-3

    XYZ anchorPoint;
    if (hAlign == 0 && vAlign == 0)
    {
        // Default: Left-Baseline, use first alignment point
        anchorPoint = text.InsertPoint;
    }
    else
    {
        // Non-default: use second alignment point
        anchorPoint = text.AlignmentPoint; // group 11/21/31
    }

    // Compute offset from anchor to bottom-left of text bbox
    double xOffset = 0, yOffset = 0;

    switch (hAlign)
    {
        case 0: xOffset = 0; break;                          // Left
        case 1: xOffset = -textWidth / 2; break;             // Center
        case 2: xOffset = -textWidth; break;                 // Right
        case 3: /* Aligned: special handling */ break;
        case 4: xOffset = -textWidth / 2; yOffset = -textHeight / 2; break; // Middle
        case 5: /* Fit: special handling */ break;
    }

    if (hAlign != 4) // Middle already handles vertical
    {
        switch (vAlign)
        {
            case 0: yOffset = 0; break;                     // Baseline (y=0 is baseline)
            case 1: yOffset = descent; break;               // Bottom (below descenders)
            case 2: yOffset = -(capHeight / 2); break;      // Middle
            case 3: yOffset = -capHeight; break;             // Top
        }
    }

    return (anchorPoint, new XY(xOffset, yOffset));
}
```

**Input**: TEXT entity, computed text dimensions.

**Output**: World-space anchor point and local offset.

**Edge cases**:
- Aligned mode: requires both alignment points and distance calculation
- Fit mode: requires adjusting width factor to match inter-point distance
- Middle mode: uses true geometric center (not baseline center)

---

### Step 3: Implement TEXT Transform Computation

**What**: Build the full transformation for a TEXT entity: position + rotation + oblique + width factor + generation flags.

**Algorithm**:
```csharp
Matrix4 ComputeTextTransform(TextEntity text, XYZ anchorPoint, XY alignmentOffset)
{
    // Start with alignment offset
    var offsetM = Matrix4.CreateTranslation(alignmentOffset.X, alignmentOffset.Y, 0);

    // Width factor (horizontal stretch)
    var widthM = Matrix4.CreateScale(new XYZ(text.WidthFactor, 1, 1));

    // Oblique angle (shear transform). In CAD text, oblique is typically implemented as:
    //   x' = x + tan(oblique) * y   (shear X by Y)
    // Prefer implementing this via a dedicated helper to avoid matrix-index confusion.
    var obliqueM = Matrix4.Identity;
    if (text.ObliqueAngle != 0)
    {
        double shear = Math.Tan(text.ObliqueAngle); // radians in ACadSharp
        obliqueM = TransformHelper.CreateShearXByY(shear);
    }

    // Mirror flags (group 71). In ACadSharp this is `TextEntity.Mirror` (flags 2 and 4).
    var mirrorM = Matrix4.Identity;
    if ((text.Mirror & TextMirrorFlag.Backward) != 0)
        mirrorM = Matrix4.CreateScale(new XYZ(-1, 1, 1));
    if ((text.Mirror & TextMirrorFlag.UpsideDown) != 0)
        mirrorM *= Matrix4.CreateScale(new XYZ(1, -1, 1));

    // Rotation
    var rotateM = Matrix4.CreateFromAxisAngle(XYZ.AxisZ, text.Rotation); // radians in ACadSharp

    // Translation to anchor point
    var translateM = Matrix4.CreateTranslation(anchorPoint);

    // OCS to WCS
    var ocsM = Matrix4.GetArbitraryAxis(text.Normal);

    // Compose: ocsM * translateM * rotateM * mirrorM * obliqueM * widthM * offsetM
    return ocsM * translateM * rotateM * mirrorM * obliqueM * widthM * offsetM;
}
```

**Input**: TEXT entity properties, resolved anchor point and offset.

**Output**: Complete 4x4 transform matrix for the text.

**Edge cases**:
- Oblique angle of 90 degrees (degenerate, text collapses)
- Width factor of 0 (degenerate, skip)
- Combined backward + upside-down + rotation: verify all compose correctly

---

### Step 4: Implement MTEXT Formatting Parser

**What**: Parse MTEXT content string into a sequence of formatting commands and text runs.

**Algorithm**:
```csharp
class MTextParser
{
    List<MTextToken> Parse(string content)
    {
        var tokens = new List<MTextToken>();
        int i = 0;
        while (i < content.Length)
        {
            if (content[i] == '\\')
            {
                i++;
                switch (content[i])
                {
                    case 'P': tokens.Add(new LineBreakToken()); i++; break;
                    case 'f': /* parse font name until ; */ break;
                    case 'H': /* parse height value until ; */ break;
                    case 'S': /* parse stacking until ; */ break;
                    case 'Q': /* parse oblique until ; */ break;
                    case 'W': /* parse width factor until ; */ break;
                    case 'O': tokens.Add(new OverlineToggleToken()); i++; break;
                    case 'L': tokens.Add(new UnderlineToggleToken()); i++; break;
                    case 'C': /* parse color index until ; */ break;
                    case '~': tokens.Add(new TextToken("\u00A0")); i++; break; // NBSP
                    case '\\': tokens.Add(new TextToken("\\")); i++; break;
                    case '{': tokens.Add(new TextToken("{")); i++; break;
                    case '}': tokens.Add(new TextToken("}")); i++; break;
                    default: tokens.Add(new TextToken(content[i].ToString())); i++; break;
                }
            }
            else if (content[i] == '{')
            {
                tokens.Add(new GroupStartToken()); i++;
            }
            else if (content[i] == '}')
            {
                tokens.Add(new GroupEndToken()); i++;
            }
            else
            {
                // Accumulate plain text until next special character
                int start = i;
                while (i < content.Length && content[i] != '\\' && content[i] != '{' && content[i] != '}')
                    i++;
                tokens.Add(new TextToken(content[start..i]));
            }
        }
        return tokens;
    }
}
```

**Input**: MTEXT content string.

**Output**: List of tokens (TextToken, LineBreakToken, FontChangeToken, HeightChangeToken, etc.).

**Edge cases**:
- Unterminated formatting codes (missing `;`): treat rest of string as the value
- Nested braces: maintain a stack for formatting scope
- Content split across group 1 and group 3: concatenate all content groups before parsing
- Unicode escape sequences: `\U+XXXX` for Unicode characters
- Empty content: return empty token list

---

### Step 5: Implement MTEXT Line Layout

**What**: Given parsed MTEXT tokens, lay out text into lines with word wrapping, then position each line according to the attachment point.

**Algorithm**:
```csharp
List<TextLine> LayoutMTextLines(List<MTextToken> tokens, double referenceWidth,
    double defaultHeight, double lineSpacingFactor, int lineSpacingStyle)
{
    var lines = new List<TextLine>();
    var currentLine = new TextLine();
    var currentRun = new TextRun();
    double currentX = 0;

    foreach (var token in tokens)
    {
        if (token is LineBreakToken)
        {
            currentLine.Runs.Add(currentRun);
            lines.Add(currentLine);
            currentLine = new TextLine();
            currentRun = new TextRun();
            currentX = 0;
        }
        else if (token is TextToken text)
        {
            // Measure text width
            double wordWidth = MeasureText(text.Content, currentRun.Font, currentRun.Height);

            // Word wrap if referenceWidth > 0
            if (referenceWidth > 0 && currentX + wordWidth > referenceWidth)
            {
                // Break at word boundary
                currentLine.Runs.Add(currentRun);
                lines.Add(currentLine);
                currentLine = new TextLine();
                currentRun = new TextRun { /* inherit current formatting */ };
                currentX = 0;
            }

            currentRun.Text += text.Content;
            currentX += wordWidth;
        }
        else if (token is FontChangeToken fontChange)
        {
            // Save current run, start new run with new font
            currentLine.Runs.Add(currentRun);
            currentRun = new TextRun { Font = fontChange.FontName, /* inherit other props */ };
        }
        // ... handle other formatting tokens
    }

    // Finalize last line
    currentLine.Runs.Add(currentRun);
    lines.Add(currentLine);

    // Position lines according to attachment point and line spacing
    double lineHeight = lineSpacingFactor * 1.666 * defaultHeight;
    PositionLines(lines, lineHeight, attachmentPoint, referenceWidth);

    return lines;
}
```

**Input**: Parsed tokens, MTEXT properties (width, height, spacing, attachment).

**Output**: List of positioned text lines, each containing positioned text runs.

**Edge cases**:
- Width of 0 (no wrapping): single-line layout, wrap only on `\P`
- Very long words that exceed referenceWidth: do not break mid-word (or implement character-level break as fallback)
- Empty lines from consecutive `\P\P`: produce blank lines with correct spacing
- Right-to-left text: not commonly used in technical drawings, defer

---

### Step 6: Implement MTEXT Attachment Point Positioning

**What**: Offset the text block so that the specified attachment point coincides with the insertion point.

**Algorithm**:
```csharp
void PositionLines(List<TextLine> lines, double lineHeight, int attachmentPoint, double refWidth)
{
    double totalHeight = lines.Count * lineHeight;
    double maxWidth = lines.Max(l => l.Width);
    double effectiveWidth = refWidth > 0 ? refWidth : maxWidth;

    // Vertical offset (attachment point row: T=1-3, M=4-6, B=7-9)
    double yOffset;
    if (attachmentPoint <= 3)       // Top
        yOffset = 0;
    else if (attachmentPoint <= 6)  // Middle
        yOffset = totalHeight / 2;
    else                             // Bottom
        yOffset = totalHeight;

    // Horizontal offset per line (attachment point column: L=1/4/7, C=2/5/8, R=3/6/9)
    int col = (attachmentPoint - 1) % 3; // 0=Left, 1=Center, 2=Right

    for (int i = 0; i < lines.Count; i++)
    {
        double lineY = yOffset - (i * lineHeight) - lineHeight; // top-down layout
        double lineX;
        switch (col)
        {
            case 0: lineX = 0; break;
            case 1: lineX = (effectiveWidth - lines[i].Width) / 2; break;
            case 2: lineX = effectiveWidth - lines[i].Width; break;
        }
        lines[i].OffsetX = lineX;
        lines[i].OffsetY = lineY;
    }
}
```

**Input**: Lines with measured widths, attachment point code, reference width.

**Output**: Each line receives X/Y offset relative to insertion point.

**Edge cases**:
- Attachment point out of range (clamp to 1-9)
- All lines empty: total height is 0, offset is 0

---

### Step 7: Implement SHX-to-TTF Font Substitution

**What**: A configurable mapping from SHX font names to TTF equivalents.

**Implementation**:
```csharp
class FontResolver
{
    private Dictionary<string, string> _shxToTtf = new(StringComparer.OrdinalIgnoreCase)
    {
        { "txt.shx", "Arial" },
        { "simplex.shx", "Arial" },
        { "romans.shx", "Times New Roman" },
        { "romand.shx", "Times New Roman" },
        { "romanc.shx", "Times New Roman" },
        { "romant.shx", "Times New Roman" },
        { "isocp.shx", "Arial" },     // ISOCPEUR if available
        { "isocp2.shx", "Arial" },
        { "isocp3.shx", "Arial" },
        { "isoct.shx", "Arial" },
        { "isoct2.shx", "Arial" },
        { "monotxt.shx", "Courier New" },
        { "gothic.shx", "Century Gothic" },
        { "gothicg.shx", "Century Gothic" },
        { "gothice.shx", "Century Gothic" },
    };

    // User-configurable overrides
    private Dictionary<string, string> _customMappings;

    string ResolveFontName(TextStyle style)
    {
        string fontFile = style.Filename ?? "";

        // Check custom mappings first
        if (_customMappings.TryGetValue(fontFile, out string custom))
            return custom;

        // Check if it is a TTF (has .ttf extension or BigFontFilename set)
        if (fontFile.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
            fontFile.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(fontFile);
        }

        // SHX substitution
        if (_shxToTtf.TryGetValue(fontFile, out string substitute))
            return substitute;

        // Default fallback
        return "Arial";
    }
}
```

**Input**: TextStyle entity with font file reference.

**Output**: TTF font family name.

**Edge cases**:
- Style with no filename: use "Standard" -> "Arial"
- BigFont (CJK fonts): requires special handling, map to appropriate CJK TTF (e.g., SimSun, MS Gothic)
- Font not installed on rendering system: PDF viewers will substitute, but log a warning

---

### Step 8: Implement Text Metrics (Approximation or TTF-Based)

**What**: Compute text width and height metrics for alignment and wrapping calculations.

**Approximation approach** (Phase 1):
```csharp
class ApproximateTextMetrics : TextMetrics
{
    // Average character width ratios for common fonts (proportional)
    // These are rough averages; actual widths vary per character
    double MeasureStringWidth(string text, string fontName, double fontSize, double widthFactor)
    {
        // Proportional fonts: average char width ~ 0.55 * fontSize
        // Monospaced fonts: char width ~ 0.60 * fontSize
        double avgCharWidth = IsMonospaced(fontName) ? 0.60 : 0.55;
        return text.Length * avgCharWidth * fontSize * widthFactor;
    }

    double GetCapHeight(string fontName, double fontSize)
    {
        return fontSize; // By DXF convention, text height IS the cap height
    }

    double GetAscent(string fontName, double fontSize)
    {
        return fontSize * 1.0; // Slightly above cap height for accents
    }

    double GetDescent(string fontName, double fontSize)
    {
        return fontSize * -0.25; // Below baseline
    }
}
```

**Accurate approach** (Phase 2, recommended):
Use `SixLabors.Fonts` or `Typography.OpenFont` NuGet package to read actual TTF metrics:
```csharp
class TtfTextMetrics : TextMetrics
{
    private FontCollection _fonts;

    double MeasureStringWidth(string text, string fontName, double fontSize, double widthFactor)
    {
        var font = _fonts.Get(fontName).CreateFont((float)fontSize);
        var bounds = TextMeasurer.MeasureSize(text, new TextOptions(font));
        return bounds.Width * widthFactor;
    }
}
```

**Input**: Text string, font name, size.

**Output**: Width in drawing units, cap height, ascent, descent.

**Edge cases**:
- Empty string: width = 0
- Characters not in font: use replacement character width
- Very large text strings (thousands of characters): performance of per-character measurement

---

### Step 9: Generate TextRunNode Primitives

**What**: Convert the laid-out text (positioned lines and runs) into TextRunNode render primitives.

**For TEXT**:
```csharp
List<RenderNode> LayoutText(TextEntity text, Matrix4 parentTransform, StrokeStyle style)
{
    double textWidth = _metrics.MeasureStringWidth(text.Value, fontName, text.Height, text.WidthFactor);
    var (anchor, offset) = ResolveTextAlignment(text, textWidth, text.Height);
    var textTransform = ComputeTextTransform(text, anchor, offset);
    var worldTransform = parentTransform * textTransform;

    return new List<RenderNode>
    {
        new TextRunNode
        {
            Text = text.Value,
            FontName = _fontResolver.ResolveFontName(text.Style),
            FontSize = text.Height,
            Position = ExtractPosition(worldTransform),
            Rotation = ExtractRotation(worldTransform),
            ObliqueAngle = text.ObliqueAngle,
            WidthFactor = text.WidthFactor,
            Color = style.Color,
            SourceHandle = text.Handle,
        }
    };
}
```

**For MTEXT**:
```csharp
List<RenderNode> LayoutMText(MText mtext, Matrix4 parentTransform, StrokeStyle style)
{
    var tokens = _parser.Parse(mtext.Value);
    var lines = LayoutMTextLines(tokens, mtext.ReferenceRectangleWidth, mtext.Height,
        mtext.LineSpacingFactor, mtext.LineSpacingStyle);

    var nodes = new List<RenderNode>();
    var textTransform = ComputeMTextTransform(mtext);

    foreach (var line in lines)
    {
        foreach (var run in line.Runs)
        {
            if (string.IsNullOrEmpty(run.Text)) continue;

            var runPosition = new XY(line.OffsetX + run.OffsetX, line.OffsetY);
            var worldPos = Vector3.Transform(runPosition.ToVector3(), parentTransform * textTransform);

            nodes.Add(new TextRunNode
            {
                Text = run.Text,
                FontName = run.Font ?? _fontResolver.ResolveFontName(mtext.Style),
                FontSize = run.Height ?? mtext.Height,
                Position = new XY(worldPos.X, worldPos.Y),
                Rotation = ExtractRotation(parentTransform * textTransform),
                Color = run.Color ?? style.Color,
                SourceHandle = mtext.Handle,
            });

            // Add underline/overline PathNodes if active
            if (run.Underline)
            {
                // Draw a line under the text
                nodes.Add(CreateUnderlinePath(worldPos, run));
            }
        }
    }

    return nodes;
}
```

**Input**: Laid-out text lines.

**Output**: TextRunNode primitives positioned in world space.

**Edge cases**:
- Text with special PDF characters (parentheses, backslash): must be escaped in PDF string
- Unicode text: ensure PDF font supports the characters or embed the font

---

### Step 10: Integrate into EntityFrontend

**What**: Add TEXT, MTEXT, and ATTRIB cases to the `EntityFrontend` dispatcher.

```csharp
case TextEntity text:
    return _textEngine.LayoutText(text, worldTransform, style);
case MText mtext:
    return _textEngine.LayoutMText(mtext, worldTransform, style);
```

---

## Testing Strategy

### Unit Tests

1. **Alignment point resolution**: TEXT with Left-Baseline uses first point. TEXT with Center-Middle uses second point.
2. **All 15 alignment combinations**: Generate TEXT for each (hAlign 0-5 x vAlign 0-3, minus invalid combos), verify anchor point selection.
3. **Aligned mode**: Two points define direction and length. Verify rotation and scale.
4. **Fit mode**: Verify height preserved, width factor adjusted.
5. **Rotation**: TEXT rotated 45 degrees. Verify transform matrix.
6. **Generation flags**: Backward text, upside-down text, both combined.
7. **MTEXT parser**: Parse `"Hello\\PWorld"` -> two text tokens with line break.
8. **MTEXT parser font**: Parse `"{\\fArial;Bold text}"` -> font change + text + group end.
9. **MTEXT parser stacking**: Parse `"\\S1/2;"` -> stacking token with numerator/denominator.
10. **MTEXT line wrapping**: Width 100, text "AAAA BBBB CCCC" with char width ~10 -> wraps appropriately.
11. **MTEXT attachment**: All 9 attachment points verified for a 3-line text block.
12. **Font substitution**: `txt.shx` -> `Arial`, `romans.shx` -> `Times New Roman`.
13. **Oblique angle**: TEXT with 15-degree oblique, verify shear in transform.
14. **Width factor**: TEXT with width factor 0.8, verify horizontal compression.

### Integration Tests

15. **Simple TEXT rendering**: DXF with a centered TEXT entity. Compare output position with expected.
16. **MTEXT multi-line**: DXF with a 3-line MTEXT using `\P` breaks and different attachment points.
17. **ATTRIB in INSERT**: INSERT with ATTRIB values, verify text appears at correct position with correct value.
18. **Mixed fonts in MTEXT**: MTEXT with font changes mid-line.

### Test DXF Generation

```python
import ezdxf

doc = ezdxf.new()
msp = doc.modelspace()

# TEXT with all alignments
for h in range(6):
    for v in range(4):
        if h >= 3 and v > 0: continue  # Invalid combos
        msp.add_text(f"H{h}V{v}", dxfattribs={
            'height': 2.5,
            'insert': (h * 30, v * 10),
            'align_point': (h * 30, v * 10),
            'halign': h, 'valign': v
        })

# MTEXT with formatting
msp.add_mtext("Line 1\\PLine 2\\P{\\fArial|b1;Bold Line 3}",
    dxfattribs={'insert': (0, 50), 'char_height': 2.5, 'width': 50, 'attachment_point': 1})

doc.saveas('test_text.dxf')
```

---

## Dependencies

### Depends On
- **Stage 00 (Render Infrastructure)**: TextRunNode primitive, PropertyResolver for color, EntityFrontend integration
- **Stage 01 (INSERT/Blocks)**: ATTRIB processing calls into TextLayoutEngine for alignment

### Enables
- **Stage 03 (DIMENSIONS)**: Dimension text is placed using the text layout engine
- **Stage 04 (MULTILEADER)**: MLEADER text content uses MTEXT layout
- **Stage 09 (TOLERANCE)**: Tolerance frame text uses the text engine
- All future stages that involve any text rendering

### External Dependencies
- (Optional) `SixLabors.Fonts` or `Typography.OpenFont` NuGet package for accurate text metrics
- System fonts must be accessible for font resolution
