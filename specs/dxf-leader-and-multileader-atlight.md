# Demystifying DXF: LEADER and MULTILEADER Implementation Notes

**Source:** https://atlight.github.io/formats/dxf-leader.html
**Author:** Alan Thomas, ThinkSpatial, July 2018
**License:** Creative Commons Attribution-ShareAlike 4.0 International License

---

## Introduction

The author implemented support for leader elements as part of work on the DXF driver in GDAL. Leaders are arrows extending from text labels or symbols to highlight important aspects of drawings.

AutoCAD provides two leader types:

1. **LEADER** - The classic entity sharing infrastructure with DIMENSION, including DIMSTYLE and override systems. Documentation in the DXF specification is reasonably comprehensive.

2. **MULTILEADER (MLEADER)** - Introduced in AutoCAD 2008, conceptually combining MTEXT, INSERT, ATTRIB, and LWPOLYLINE. This entity is "one of the worst-documented DXF entities; the description in the DXF spec is next to useless."

### My Implementation

The author provided C++ code for the GDAL/OGR DXF LEADER and MULTILEADER translator on GitHub, along with a demo DXF file containing various leader and multileader objects for testing implementations.

## LEADER

The LEADER entity represents an arrow made of vertices (or spline fit points) and an arrowhead. Associated labels or content are stored as separate entities, not as part of the LEADER itself.

LEADER shares styling infrastructure with DIMENSION. Correct styling begins with dimension style properties, then applies entity-level overrides. Rendering a simple LEADER involves connecting vertices with line segments and attaching an arrowhead at the end.

### The Hook Line

When the DIMTAD dimension style property is set to anything other than "Centered" (0), the leader line extends beneath the text as a "hook line."

The hook line endpoint is not stored in the DXF file and must be calculated using:
- The (211,221,231) direction vector
- Text width stored in group code 41
- A "flip" boolean in group code 74

The pseudocode calculation:

```
if ( DIMTAD != 0 && gc73 == 0 && gc41 > 0 && count(vertices) >= 2 )
{
    directionVector = (gc211, gc221, gc231) or (1.0, 0.0, 0.0) if not present
    if ( gc74 == 1 )
        directionVector = -directionVector

    lastVertex = the last (gc10, gc20, gc30) present
    vertices.append( lastVertex + ( DIMGAP * DIMSCALE + gc41 ) * directionVector )
}
```

The author notes that group code 74 flipping contradicts the DXF spec but matches AutoCAD's behavior, suggesting the specification contains an error.

### Splines

When group code 72 equals 1, LEADERs render as splines using this approach:

- LEADER vertices are treated as equally-weighted fit points of a degree 3 spline
- The spline is periodic but not planar or closed
- Fit tolerance is 0
- Start and end tangent directions follow the directions of the first and last line segments (including hook line, if present) in line-mode rendering
- If an arrowhead exists, the final fit point locates at the arrowhead's back, but since this isn't given in DXF data, implementations must calculate or approximate it

The author notes vertices function as fit points, unlike elsewhere in DXF where splines generate from control points.

### LEADERs are Legacy

Autodesk treats LEADER and QLEADER as legacy features. The LEADER user documentation recommends switching to MLEADER workflow, and default AutoCAD toolbars lack a LEADER insertion button. Unless users downgrade drawings to pre-2008 DXF versions, MULTILEADER support is necessary.

## MULTILEADER

MULTILEADERs differ fundamentally from LEADERs in several important ways:

1. **Styling System** - MULTILEADERs use a separate MLEADERSTYLE system (unrelated to DIMENSION infrastructure). Unlike LEADER (which stores only style overrides), all styling properties are stored directly in the MULTILEADER entity.

2. **Content Incorporation** - The content (text label or block) is described using group codes within the MULTILEADER entity itself, similar to how DIMENSION incorporates dimension text, rather than as a separate entity.

3. **Multiple Leader Lines** - MULTILEADERs can have zero or more leader lines, grouped into zero, one, or two leaders. Styling applies to all leader lines; different coloring for individual leader lines is impossible.

4. **Dogleg Instead of Hook Line** - Rather than LEADER's hook line, MULTILEADER uses a "dogleg" (AutoCAD UI calls it "landing"), the common final segment before reaching content.

5. **Documentation Issues** - The DXF specification documentation is problematic, beginning with referring to the entity as "MLEADER" when AutoCAD's DXF writer outputs "MULTILEADER."

### Sources of Information

The DXF specification alone is insufficient. Valuable supplementary sources include:

- **ObjectARX Documentation** - The `AcDbMLeader` C++ class documentation reveals internal structure. For example, `AcDbMLeader::leaderLineType()` returns values corresponding to `AcDbMLeaderStyle::LeaderType`, clarifying interpretation of common group code 170.

- **Open Design Alliance DWG Specification** - Sections 19.4.46 and 19.4.83 provide a DWG format reverse-engineer that cross-references DXF group codes. Although copyright statements are stern, typical usage unlikely exceeds copyright concerns. The DWG and DXF formats share such similar structure that the ODA specification references DXF group codes where possible.

### Structure

The MULTILEADER entity is divided into sections using 30x group codes with specific text values:

```
...              // common group codes
300
CONTEXT_DATA{
  ...            // context data group codes
  302
  LEADER{
    ...          // leader group codes (referred to as "Leader Node" in DXF spec)
    304
    LEADER_LINE{
      ...        // leader line group codes
    305
    }
    304
    LEADER_LINE{
      ...        // leader line group codes
    305
    }
    ...          // further leader group codes
  303
  }
  302
  LEADER{
    ...          // leader group codes with leader line section(s)
  303
  }
  ...            // further context data group codes
301
}
...              // further common group codes
```

Zero or more leader and leader line sections may exist. Group codes have different meanings depending on their section (the DXF spec correctly documents this aspect).

### Entity Handles

MULTILEADER entities reference other DXF objects by handle rather than name. Even text styles and ATTDEF entities, always referenced by name in other entities, use handle references here. Common group codes 342, 344, 330, and context data group codes 340, 341 store handle references.

### Geometry

Three vertex types exist in a MULTILEADER:

1. **Ordinary vertices** - Comprise the leader line, given by (10,20,30) group code sequences in the leader line section.

2. **Landing point** - Given by (10,20,30) group codes in the leader section.

3. **Dogleg endpoint** - Must be calculated (see below).

Common group code 170 determines rendering type:
- **Straight (1)** - Join relevant vertices of each leader line with a dogleg, interrupting lines at breaks
- **Spline (2)** - Render using the same method as LEADER
- **None (0)** - Leaders don't render; only content (text or block) displays

#### Calculating the Dogleg/Landing

The dogleg endpoint calculation is: landing point + (dogleg length x dogleg direction vector).

A dogleg draws for a particular leader within the MULTILEADER if:

- MULTILEADER has doglegs enabled (common group code 291 is nonzero) _AND_
- MULTILEADER is straight (common group code 170 is 1) _AND_
- dogleg length (leader group code 40) is nonzero _AND_
- dogleg direction vector (leader group codes 11,21,31) is not a zero vector

Even when the dogleg doesn't draw, the endpoint must be calculated because the landing point is ignored and leader lines terminate at the dogleg endpoint instead.

#### Breaks

Straight MULTILEADER leader lines can be "broken" (DIMBREAK in AutoCAD command terms) to avoid intersections with other linework. Each break is stored as a point pair. The gap begins at the first point and ends at the second.

Breaks between vertex _n_ and vertex _n_ + 1 are stored after vertex _n_ in the leader line section. Leader line group codes (11,21,31) give the start point and codes (12,22,32) give the end point. The 11,21,31,12,22,32 sequence repeats for each break in that segment.

Breaks in the dogleg are stored in the leader section. The start point uses leader group codes (12,22,32) and the end point uses (13,23,33), repeating as needed.

Spline MULTILEADERs do not use breaks.

### Content

Common group code 172 determines content type:
- **2** - Text content
- **1** - Block content
- **0** - No content

#### Text Content

MULTILEADER text is internally represented as an `AcDbMText` (MTEXT) data member of the `AcDbMLeader` class. Although group codes differ, implementations can likely share code between MTEXT and MLEADER.

Text anchors at the point given by context data group codes (12,22,32), corresponding to MTEXT group codes (10,20,30).

Where the same value appears in both common and context data sections (such as text color), AutoCAD uses the context data section value.

#### Block Content

A MULTILEADER may contain a block reference instead of text. Common group code 344 stores the BLOCK_RECORD handle. The renderer should insert this block at the position given by context data group codes (15,25,35). Context data section parameters (block normal direction, block scale, block rotation) interpret as for INSERT.

No mechanism exists to set a specific block color for a multileader in AutoCAD, so the purpose of group code 93 is unclear.

Group code 47 appears 16 times providing a matrix. Since the last four values consistently equal zero, this appears to be a 3D affine transformation, but the rationale for providing this matrix alongside independently present rotation, scale, and translation parameters remains unclear. The author's implementation ignores this matrix, assuming it's only useful with extrusions, which the implementation doesn't support.

Block attributes in MULTILEADERs differ significantly from ordinary INSERT entities. The relevant ATTDEF's handle (not name) appears in common group code 330, followed by an index (177), a "width" value (44), and the attribute value (302). This four-code sequence repeats as needed.

Implementing block attributes properly is significant. Though seemingly minor, this feature is important -- MULTILEADERs commonly consist of leader labels (like key numbers) enclosed in circles, typically structured as blocks containing circles and ATTDEFs requiring text substitution.

### Colors

MULTILEADER color is stored conventionally (62/440 group codes in the common section). However, other color values like common group code 91 use a different approach. The raw value of the `RGBM` union from the `AcCmEntityColor` class writes directly to DXF as a signed 32-bit integer. For example, ByBlock becomes -1056964608 (0xC1000000) instead of the familiar 0.

### Extrusions (OCS)

Extrusions (object coordinate systems/OCS) are an unexplored aspect of MULTILEADER. The author solicits contributions from those who have implemented this feature.
