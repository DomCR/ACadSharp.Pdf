# Stage 01: INSERT (BlockReference) + ATTRIB/ATTDEF

## Overview

The INSERT entity (also known as BlockReference) is a reference to a named block definition (`BlockRecord`). It places the block's contents at a specified insertion point with optional scale, rotation, and extrusion. INSERT is one of the most frequently used DXF entities and is a prerequisite for DIMENSION (whose arrows and symbols are often blocks), MULTILEADER (which can have block content), and any drawing that uses symbols, title blocks, or repeated components.

ATTRIB entities are attribute values attached to an INSERT. They provide variable text content (e.g., part numbers, revision dates) that differs per insertion. ATTDEF entities inside the block definition serve as templates that define the tag name, default value, and text properties.

The target module for this stage is `BlockExpander.cs`.

---

## Domain Knowledge

### INSERT Entity Group Codes

| Group Code | Field | Description |
|-----------|-------|-------------|
| 2 | Block name | Name of the BlockRecord to insert |
| 10/20/30 | Insertion point | OCS coordinates of the insertion point |
| 41 | X scale | Scale factor in X direction (default 1.0) |
| 42 | Y scale | Scale factor in Y direction (default 1.0) |
| 43 | Z scale | Scale factor in Z direction (default 1.0) |
| 50 | Rotation | DXF stores degrees; ACadSharp exposes radians via `Insert.Rotation` (default 0.0) |
| 66 | Attributes-follow flag | 1 if ATTRIB entities follow the INSERT |
| 70 | Column count (MINSERT) | Number of columns (default 1) |
| 71 | Row count (MINSERT) | Number of rows (default 1) |
| 44 | Column spacing (MINSERT) | Distance between columns |
| 45 | Row spacing (MINSERT) | Distance between rows |
| 210/220/230 | Extrusion vector | OCS normal (default 0,0,1) |

### BlockRecord and Base Point

A `BlockRecord` (accessed via `CadDocument.BlockRecords`) contains:
- A collection of entities that make up the block definition
- A `BasePoint` (also called `Origin`): the reference point of the block. When inserting, the block's base point aligns with the INSERT's insertion point.

The effective transform is: for each entity in the block, translate by `-BasePoint`, then apply the INSERT's transform (scale, rotate, translate to insertion point).

### Transformation Matrix Composition

The order of transformation composition for INSERT is critical. Follow the matrix conventions defined in Stage 00 (use `CSMath.Matrix4`, apply points as `matrix * point`, and compose transforms consistently with `CSMath.Transform`).

```
WorldPoint = OCS_to_WCS(extrusion) * Translate(insertionPoint) * Rotate(angle) * Scale(sx, sy, sz) * Translate(-basePoint) * EntityOCSPoint
```

Broken down:
1. **Translate(-basePoint)**: Move block entities so the base point is at origin
2. **Scale(sx, sy, sz)**: Apply the INSERT's scale factors
3. **Rotate(angle)**: Apply the INSERT's rotation (around Z in OCS)
4. **Translate(insertionPoint)**: Move to the insertion point (in OCS)
5. **OCS_to_WCS**: Convert from OCS to world coordinates using the extrusion vector

As a single 4x4 matrix (composition order depends on the library conventions; implement via Stage 00 helpers rather than relying on “right-to-left” mental models):
```
M = OcsToWcs * T(insert) * Rz(angle) * S(sx,sy,sz) * T(-base)
```

### Recursive Block Expansion

Blocks can contain INSERT entities that reference other blocks, creating a tree structure. The expansion algorithm must:
1. Look up the BlockRecord by name
2. For each entity in the block's entities:
   - If it is an INSERT, recursively expand it
   - Otherwise, transform the entity and convert to render primitives
3. Guard against infinite recursion via a visited set (cycle detection)

### MINSERT (Grid Expansion)

MINSERT is an INSERT with `ColumnCount > 1` or `RowCount > 1`. It creates a grid of block instances:

```
for row in 0..RowCount-1:
    for col in 0..ColumnCount-1:
        offset = (col * ColumnSpacing, row * RowSpacing, 0)
        // Transform: same as INSERT but with additional offset translation
        // The offset is applied BEFORE the INSERT's rotation/scale
```

The grid is expanded before entity decomposition. Each grid cell is an independent copy of the block.

The actual offset application order: the spacing is applied in the INSERT's local coordinate system (after scale/rotation), so:
```
M_cell = OcsToWcs * T(insert) * Rz(angle) * T(col*colSpace, row*rowSpace, 0) * S(sx,sy,sz) * T(-base)
```

⚠️ Note: The two paragraphs above were historically easy to contradict. The intended behavior is: **grid spacing is specified in the INSERT’s local OCS units and should be affected by the INSERT’s scale and rotation** (i.e., the grid “rotates/scales with the block”). If AutoCAD parity testing shows different behavior, adjust this section and add a dedicated regression test.

### ATTRIB vs ATTDEF

- **ATTDEF** (AttributeDefinition): Lives inside the block definition. Defines a tag name (group 2), prompt text, default value, and text formatting properties (height, style, alignment, etc.). ATTDEFs are templates; they are NOT rendered directly when expanding a block.
- **ATTRIB** (AttributeEntity): Lives as a child of the INSERT entity (following it, before SEQEND). Contains the actual value for a specific tag. Each ATTRIB's tag (group 2) matches an ATTDEF's tag in the block definition.

When rendering an INSERT:
1. Skip all ATTDEFs in the block definition (they are templates, not visible)
2. For each ATTRIB attached to the INSERT:
   - Find the matching ATTDEF by tag name to get formatting defaults
   - Use the ATTRIB's own value (group 1) and position
   - Apply the INSERT's transform to the ATTRIB's position
   - Render as a TextRun

### Negative Scale (Mirroring)

A negative X or Y scale factor causes a mirror transformation:
- Negative X scale: horizontal mirror (like a `MIRROR` command about the Y-axis)
- Negative Y scale: vertical mirror

This affects:
- Text orientation: text should remain readable (not mirrored), so text entities within a mirrored block need special handling
- Arc direction: arcs in a mirrored block reverse their winding direction
- Hatches: pattern angles are affected by mirroring

### Anonymous Blocks

Block names starting with `*` are anonymous (system-generated):
- `*U` blocks: unnamed blocks created by various operations
- `*D` blocks: dimension anonymous blocks (pre-rendered dimension geometry)
- `*X` blocks: other system uses

Anonymous blocks are treated identically to named blocks in terms of expansion -- the only difference is they are not listed in the block table for user selection.

---

## External Reference Code

### ezdxf virtual_block_reference_entities() (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/entities/insert.py
- **What to study**: The `virtual_entities()` method that yields transformed copies of block entities. Pay attention to how it handles ATTRIBs, the transformation order, and the skip list for ATTDEFs.

### ezdxf Frontend INSERT handling (MIT License)
- **URL**: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/addons/drawing/frontend.py
- **What to study**: `draw_insert_entity()` method -- how it resolves properties for nested blocks, handles ByBlock color/lineweight inheritance, and guards against recursion.

### GDAL OGR DXF Driver INSERT Inlining (MIT/X-style License)
- **URL**: https://github.com/OSGeo/gdal/blob/master/ogr/ogrsf_frmts/dxf/ogrdxf_blocksinlayer.cpp
- **What to study**: The `DXF_INLINE_BLOCKS` configuration option causes INSERT entities to be expanded inline. Shows the full transformation pipeline including MINSERT grids.
- **Configuration**: `DXF_INLINE_BLOCKS=TRUE` triggers inline expansion

### ACadSharp Insert Entity
- **File**: Within the ACadSharp submodule, look at the `Insert` entity class for properties like `InsertPoint`, `XScale`, `YScale`, `ZScale`, `Rotation`, `Block` (reference to BlockRecord), `Attributes` (collection of ATTRIB).

---

## Step-by-Step Implementation Plan

### Step 1: Create BlockExpander Class

**What**: A static utility class that expands an INSERT entity into a list of render primitives.

**Key structure**:
```csharp
class BlockExpander
{
    private EntityFrontend _frontend;
    private PropertyResolver _resolver;
    private HashSet<string> _visitedBlocks; // cycle detection
    private int _maxDepth = 64; // recursion limit

    List<RenderNode> ExpandInsert(Insert insert, Matrix4 parentTransform, Insert? containingInsert)
    {
        // 1. Check cycle detection
        // 2. Compute INSERT transform matrix
        // 3. Handle MINSERT grid
        // 4. Expand block entities
        // 5. Process ATTRIBs
        // 6. Return GroupNode with children
    }
}
```

**Input**: INSERT entity, parent transform, optional containing INSERT (for ByBlock resolution).

**Output**: `GroupNode` containing all expanded render primitives.

**Edge cases**:
- `insert.Block` is null (block not found in document): log warning, skip
- Block has no entities: return empty group
- Recursion depth exceeds limit: log error, skip

---

### Step 2: Implement INSERT Transform Matrix Computation

**What**: Compute the full 4x4 transformation matrix for an INSERT.

**Algorithm**:
```csharp
Matrix4 ComputeInsertTransform(Insert insert)
{
    XYZ basePoint = insert.Block?.BasePoint ?? XYZ.Zero;

    // Use CSMath.Matrix4 throughout (angles in radians in ACadSharp).
    var translateBase = Matrix4.CreateTranslation(-basePoint);
    var scale = Matrix4.CreateScale(new XYZ(insert.XScale, insert.YScale, insert.ZScale));
    var rotate = Matrix4.CreateFromAxisAngle(XYZ.AxisZ, insert.Rotation);
    var translateInsert = Matrix4.CreateTranslation(insert.InsertPoint);
    var ocsToWcs = Matrix4.GetArbitraryAxis(insert.Normal); // OCS→WCS

    return ocsToWcs * translateInsert * rotate * scale * translateBase;
}
```

**Input**: INSERT entity.

**Output**: 4x4 matrix.

**Edge cases**:
- Scale of 0 in any axis: skip entity (degenerate, produces collapsed geometry)
- Rotation of exactly 2π radians (or multiples): normalize to a stable range (e.g., [0, 2π))
- Extrusion vector (0,0,-1): produces mirrored result; verify winding is correct

---

### Step 3: Implement MINSERT Grid Expansion

**What**: For INSERT entities with `ColumnCount > 1` or `RowCount > 1`, generate a grid of block instances.

**Algorithm**:
```csharp
List<Matrix4> ComputeMInsertTransforms(Insert insert)
{
    var transforms = new List<Matrix4>();
    int cols = Math.Max(1, insert.ColumnCount);
    int rows = Math.Max(1, insert.RowCount);

    for (int row = 0; row < rows; row++)
    {
        for (int col = 0; col < cols; col++)
        {
            double offsetX = col * insert.ColumnSpacing;
            double offsetY = row * insert.RowSpacing;

            // Offset is in the INSERT's local space, after scale, before rotation
            var gridOffset = Matrix4.CreateTranslation(offsetX, offsetY, 0);

            // Full cell transform:
            // OcsToWcs * T(insert) * Rz(angle) * T(offset) * S(sx,sy,sz) * T(-base)
            var baseT = Matrix4.CreateTranslation(-insert.Block.BasePoint);
            var scaleT = Matrix4.CreateScale(new XYZ(insert.XScale, insert.YScale, insert.ZScale));
            var rotateT = Matrix4.CreateFromAxisAngle(XYZ.AxisZ, insert.Rotation);
            var insertT = Matrix4.CreateTranslation(insert.InsertPoint);
            var ocsT = Matrix4.GetArbitraryAxis(insert.Normal);

            transforms.Add(ocsT * insertT * rotateT * gridOffset * scaleT * baseT);
        }
    }
    return transforms;
}
```

**Input**: INSERT entity with MINSERT properties.

**Output**: List of transform matrices, one per grid cell.

**Edge cases**:
- ColumnSpacing or RowSpacing of 0: overlapping instances (valid but unusual)
- Very large grids (1000x1000): may generate millions of entities; consider a rendering limit

---

### Step 4: Implement Recursive Block Entity Expansion

**What**: Walk the block's entity collection, recursively expanding nested INSERTs.

**Algorithm**:
```csharp
List<RenderNode> ExpandBlockEntities(BlockRecord block, Matrix4 transform,
    Insert containingInsert, int depth)
{
    if (depth > _maxDepth)
    {
        _log.Skip(containingInsert, "max recursion depth exceeded");
        return empty;
    }

    string blockKey = block.Name;
    if (_visitedBlocks.Contains(blockKey))
    {
        _log.Skip(containingInsert, $"circular reference to block '{blockKey}'");
        return empty;
    }

    _visitedBlocks.Add(blockKey);
    var results = new List<RenderNode>();

    foreach (Entity entity in block.Entities)
    {
        // Skip ATTDEFs (they are templates, not rendered directly)
        if (entity is AttributeDefinition)
            continue;

        if (entity is Insert nestedInsert)
        {
            // Recursive expansion
            var nestedTransform = transform * ComputeInsertTransform(nestedInsert);
            results.AddRange(ExpandBlockEntities(
                nestedInsert.Block, nestedTransform, nestedInsert, depth + 1));
            // Also process nested ATTRIBs
            results.AddRange(ProcessAttribs(nestedInsert, nestedTransform));
        }
        else
        {
            // Regular entity: process through frontend
            results.AddRange(_frontend.ProcessEntity(entity, transform, containingInsert));
        }
    }

    _visitedBlocks.Remove(blockKey);
    return results;
}
```

**Input**: BlockRecord, transform matrix, containing INSERT, recursion depth.

**Output**: List of render primitives for all entities in the block.

**Edge cases**:
- Block contains only ATTDEFs and no other entities: returns empty list (valid)
- Self-referencing block (A contains INSERT of A): caught by cycle detection
- Mutual recursion (A references B, B references A): caught by visited set
- Entity in block is on a frozen layer: still check visibility per entity

---

### Step 5: Implement ATTRIB Processing

**What**: Process ATTRIB entities attached to an INSERT, matching them with ATTDEFs for formatting.

**Algorithm**:
```csharp
List<RenderNode> ProcessAttribs(Insert insert, Matrix4 insertTransform)
{
    var results = new List<RenderNode>();

    foreach (AttributeEntity attrib in insert.Attributes)
    {
        // Check visibility flag (bit 0 of group 70)
        if (attrib.IsInvisible)
            continue;

        // Find matching ATTDEF in block for default properties
        AttributeDefinition attdef = insert.Block?.Entities
            .OfType<AttributeDefinition>()
            .FirstOrDefault(d => d.Tag == attrib.Tag);

        // Use ATTRIB's own value (group 1)
        string textValue = attrib.Value;

        // Text properties: prefer ATTRIB's own values, fall back to ATTDEF
        double height = attrib.Height > 0 ? attrib.Height : (attdef?.Height ?? 2.5);
        string style = attrib.Style?.Name ?? attdef?.Style?.Name ?? "Standard";

        // Position: ATTRIB has its own insertion point, in OCS of the INSERT
        XYZ position = attrib.InsertPoint;

        // Transform position through INSERT matrix
        var worldPos = Vector3.Transform(position.ToVector3(), insertTransform);

        // Create TextRunNode
        var textNode = new TextRunNode
        {
            Text = textValue,
            FontName = ResolveFontName(style),
            FontSize = height * ExtractScaleFactor(insertTransform),
            Position = new XY(worldPos.X, worldPos.Y),
            Rotation = attrib.Rotation + ExtractRotation(insertTransform),
            Color = _resolver.ResolveColor(attrib, insert),
            // Alignment from ATTRIB
        };

        results.Add(textNode);
    }

    return results;
}
```

**Input**: INSERT entity with its ATTRIB collection, INSERT transform.

**Output**: List of TextRunNode primitives for visible attributes.

**Edge cases**:
- ATTRIB with no matching ATTDEF: use ATTRIB's own properties exclusively
- ATTRIB with empty value: skip rendering (nothing to display)
- Constant ATTDEF (bit 1 of group 70): value cannot be changed per INSERT, use ATTDEF's default
- ATTRIB alignment modes: same complexity as TEXT alignment (see Stage 02)
- Multi-line ATTRIB (MTEXT-based attributes in newer DXF versions)

---

### Step 6: Integrate into EntityFrontend

**What**: Add the INSERT case to the `EntityFrontend` type-switch.

**Implementation**:
```csharp
// In EntityFrontend.ProcessEntity():
case Insert insert:
    var expander = new BlockExpander(_frontend, _resolver, _log);
    return expander.ExpandInsert(insert, worldTransform, containingInsert);
```

**Input**: INSERT entity arriving at the frontend.

**Output**: Expanded render primitives wrapped in a GroupNode.

**Edge cases**:
- INSERT with missing block reference: log "block not found", skip
- INSERT of an empty block: return empty group

---

### Step 7: Handle ByBlock Property Inheritance

**What**: When an entity inside a block has color/lineweight/linetype set to ByBlock, it inherits from the INSERT entity that references the block.

**Algorithm enhancement to PropertyResolver**:
```csharp
Color ResolveColor(Entity entity, Insert? containingInsert)
{
    if (entity.Color.IsByBlock) // ACI = 0
    {
        if (containingInsert != null)
        {
            // Recursively resolve: the INSERT might also be ByBlock or ByLayer
            return ResolveColor(containingInsert, containingInsert.ParentInsert);
        }
        return Color.White; // Default when no containing INSERT
    }
    // ... rest of resolution chain
}
```

**Input**: Entity color value, containing INSERT chain.

**Output**: Resolved RGB color.

**Edge cases**:
- Layer 0 entities in blocks: when a block entity is on layer "0", it inherits the INSERT's layer (not the literal layer "0"). This is a DXF convention for "use the inserter's layer."
- ByBlock inside ByBlock inside ByLayer: chains must resolve fully
- Maximum chain depth: add a guard (e.g., 32 levels)

---

## Testing Strategy

### Unit Tests

1. **Transform composition test**: Create an INSERT at (100, 50) with scale (2, 2, 1) and rotation π/4 rad (45° in DXF). Verify a point at block's base point maps to (100, 50) in world space.

2. **MINSERT grid test**: INSERT with 3 columns, 2 rows, spacing (10, 20). Verify 6 transform matrices are produced with correct offsets.

3. **Cycle detection test**: Create a block A that contains an INSERT of block A. Verify expansion terminates and logs an error.

4. **Mutual recursion test**: Block A references B, block B references A. Verify both are detected.

5. **ATTRIB matching test**: Block has ATTDEFs with tags "PART_NO" and "REV". INSERT has ATTRIBs for "PART_NO" and "REV" with specific values. Verify correct text nodes are produced.

6. **ATTDEF skip test**: Verify that ATTDEFs in the block definition are NOT rendered as visible text.

7. **Invisible ATTRIB test**: ATTRIB with invisible flag set. Verify it is skipped.

8. **Negative scale test**: INSERT with XScale = -1. Verify mirror transform is correct. Verify text inside the block is not rendered mirrored.

9. **ByBlock color test**: Entity in block with color ByBlock. INSERT with color Red. Verify entity renders red.

10. **Layer 0 inheritance test**: Entity in block on layer "0". INSERT on layer "DIM". Verify entity uses layer "DIM" properties.

### Integration Tests

11. **Simple block expansion**: DXF with a block containing a rectangle (4 lines) and an INSERT of that block. Verify 4 PathNode primitives are produced.

12. **Nested blocks**: Block A contains a circle. Block B contains an INSERT of A. Drawing has INSERT of B. Verify the circle is correctly transformed through both levels.

13. **Title block with attributes**: Real-world title block with multiple ATTRIBs. Verify all attribute text is correctly positioned and valued.

14. **Anonymous block (*D) expansion**: Dimension with pre-rendered anonymous block. Verify expansion produces the dimension geometry.

### Test DXF Generation (using ezdxf)

```python
import ezdxf

doc = ezdxf.new()
msp = doc.modelspace()

# Create a simple block
block = doc.blocks.new(name='RECT')
block.add_line((0, 0), (10, 0))
block.add_line((10, 0), (10, 5))
block.add_line((10, 5), (0, 5))
block.add_line((0, 5), (0, 0))
block.add_attdef('LABEL', (5, 2.5), dxfattribs={'height': 1.0})

# Insert with attributes
insert = msp.add_blockref('RECT', (50, 50), dxfattribs={
    'xscale': 2.0, 'yscale': 1.5, 'rotation': 30
})
insert.add_auto_attribs({'LABEL': 'Test Part'})

doc.saveas('test_insert.dxf')
```

---

## Dependencies

### Depends On
- **Stage 00 (Render Infrastructure)**: Uses GroupNode, Transform matrices, PropertyResolver (ByBlock resolution), EntityFrontend, RenderLog

### Enables
- **Stage 02 (TEXT/MTEXT)**: ATTRIB rendering requires text layout capabilities. Basic ATTRIB can be rendered with simple TextRunNode from Stage 00, but full alignment support comes from Stage 02.
- **Stage 03 (DIMENSIONS)**: Dimension arrowheads and symbols are often defined as blocks and inserted via INSERT. Also, dimensions have pre-rendered anonymous blocks (*D).
- **Stage 04 (MULTILEADER)**: MLEADER block content mode uses INSERT/BlockRecord.
- **Stage 09 (TOLERANCE)**: May reference blocks for GD&T symbols.

### External Dependencies
- ACadSharp `Insert`, `BlockRecord`, `AttributeDefinition`, `AttributeEntity` classes (already available)
- `CSMath.Matrix4` for transform computation (aligned with Stage 00)
