"""Generate DXF fixture files for ACadSharp.Pdf integration tests.

Each function creates a targeted DXF file for a specific rendering stage.
Output: samples/fixtures/stage{00-09}_*.dxf
"""

import math
import os
import struct

import ezdxf
from ezdxf.math import Vec3


OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "..", "samples", "fixtures")


def stage00_primitives():
    """Lines, Arcs, Circles, Ellipses, LwPolyline, Polyline2D, Polyline3D, Points."""
    doc = ezdxf.new("R2010")
    msp = doc.modelspace()

    # Lines (3+)
    msp.add_line((0, 0), (100, 0), dxfattribs={"color": 1})
    msp.add_line((0, 0), (0, 100), dxfattribs={"color": 2, "lineweight": 50})
    msp.add_line((0, 0), (70, 70), dxfattribs={"color": 3, "lineweight": 100})

    # Arcs (2+)
    msp.add_arc(center=(50, 50), radius=30, start_angle=0, end_angle=180,
                dxfattribs={"color": 4})
    msp.add_arc(center=(50, 50), radius=20, start_angle=45, end_angle=270,
                dxfattribs={"color": 5})

    # Circles (2+)
    msp.add_circle(center=(150, 50), radius=25, dxfattribs={"color": 6})
    msp.add_circle(center=(150, 100), radius=15, dxfattribs={"color": 7})

    # Ellipses (2+)
    msp.add_ellipse(center=(250, 50), major_axis=(40, 0, 0), ratio=0.5,
                    dxfattribs={"color": 1})
    msp.add_ellipse(center=(250, 100), major_axis=(30, 10, 0), ratio=0.3,
                    start_param=0, end_param=math.pi,
                    dxfattribs={"color": 2})

    # LwPolyline with bulge
    msp.add_lwpolyline(
        [(300, 0, 0, 0, 0), (350, 0, 0.5, 0, 0), (350, 50, 0, 0, 0),
         (300, 50, -0.3, 0, 0)],
        close=True,
        dxfattribs={"color": 3},
        format="xybse",
    )

    # Polyline2D
    msp.add_polyline2d(
        [(400, 0), (450, 0), (450, 50), (400, 50)],
        close=True,
        dxfattribs={"color": 4},
    )

    # Polyline3D
    msp.add_polyline3d(
        [(0, 0, 0), (50, 0, 20), (50, 50, 40), (0, 50, 60)],
        dxfattribs={"color": 5},
    )

    # Points (2+)
    msp.add_point((500, 0), dxfattribs={"color": 6})
    msp.add_point((500, 50), dxfattribs={"color": 7})

    # OCS entity with non-trivial normal
    msp.add_circle(
        center=(0, 0),
        radius=20,
        dxfattribs={"color": 1, "extrusion": Vec3(0.5, 0.5, 0.707107)},
    )

    path = os.path.join(OUTPUT_DIR, "stage00_primitives.dxf")
    doc.saveas(path)
    print(f"  Created {path}")


def stage01_inserts():
    """INSERT (simple, scaled+rotated, nested), ATTRIB+ATTDEF, ByBlock, MINSERT grid."""
    doc = ezdxf.new("R2010")
    msp = doc.modelspace()

    # Simple block
    blk = doc.blocks.new("SIMPLE")
    blk.add_line((0, 0), (10, 0), dxfattribs={"color": 1})
    blk.add_line((0, 0), (0, 10), dxfattribs={"color": 2})
    blk.add_circle((5, 5), radius=3, dxfattribs={"color": 256})  # ByBlock

    # Block with ATTDEF
    blk_attr = doc.blocks.new("WITH_ATTR")
    blk_attr.add_line((0, 0), (20, 0), dxfattribs={"color": 3})
    blk_attr.add_attdef("TAG1", insert=(5, 5), dxfattribs={"color": 4, "height": 2.5})
    blk_attr.add_attdef("TAG2", insert=(5, 10), dxfattribs={"color": 5, "height": 2.5})

    # Inner block for nesting
    inner = doc.blocks.new("INNER")
    inner.add_circle((0, 0), radius=5, dxfattribs={"color": 6})
    inner.add_line((-5, 0), (5, 0), dxfattribs={"color": 6})

    # Outer block (nests INNER)
    outer = doc.blocks.new("OUTER")
    outer.add_line((0, 0), (30, 0), dxfattribs={"color": 7})
    outer.add_blockref("INNER", insert=(15, 0))

    # Simple insert
    msp.add_blockref("SIMPLE", insert=(0, 0))

    # Scaled + rotated insert
    msp.add_blockref("SIMPLE", insert=(50, 0),
                     dxfattribs={"xscale": 2, "yscale": 2, "rotation": 45})

    # Insert with attributes
    insert_attr = msp.add_blockref("WITH_ATTR", insert=(100, 0))
    insert_attr.add_auto_attribs({"TAG1": "Value1", "TAG2": "Value2"})

    # Another insert with attributes (different values)
    insert_attr2 = msp.add_blockref("WITH_ATTR", insert=(100, 50))
    insert_attr2.add_auto_attribs({"TAG1": "Hello", "TAG2": "World"})

    # Nested insert (OUTER contains INNER)
    msp.add_blockref("OUTER", insert=(200, 0))

    # MINSERT grid (column_count, row_count on Insert)
    minsert = msp.add_blockref("SIMPLE", insert=(0, 100),
                               dxfattribs={
                                   "column_count": 3,
                                   "row_count": 2,
                                   "column_spacing": 20,
                                   "row_spacing": 20,
                               })

    path = os.path.join(OUTPUT_DIR, "stage01_inserts.dxf")
    doc.saveas(path)
    print(f"  Created {path}")


def stage02_text():
    """TEXT (various alignments, rotated, oblique), MTEXT (single, multiline, formatting)."""
    doc = ezdxf.new("R2010")
    msp = doc.modelspace()

    # TEXT entities with different alignments
    # Left-aligned (default)
    msp.add_text("Left Aligned", height=5,
                 dxfattribs={"insert": (0, 0), "color": 1})

    # Center-aligned
    msp.add_text("Center Aligned", height=5,
                 dxfattribs={"halign": 1, "valign": 0, "color": 2,
                             "insert": (100, 0), "align_point": (100, 0)})

    # Right-aligned
    msp.add_text("Right Aligned", height=5,
                 dxfattribs={"halign": 2, "valign": 0, "color": 3,
                             "insert": (200, 0), "align_point": (200, 0)})

    # Middle-center
    msp.add_text("Middle Center", height=5,
                 dxfattribs={"halign": 4, "valign": 0, "color": 4,
                             "insert": (300, 0), "align_point": (300, 0)})

    # Rotated text
    msp.add_text("Rotated 30deg", height=5,
                 dxfattribs={"insert": (0, 50), "rotation": 30, "color": 5})

    # Oblique text
    msp.add_text("Oblique 15deg", height=5,
                 dxfattribs={"insert": (0, 100), "oblique": 15, "color": 6})

    # MTEXT entities
    # Single-line
    msp.add_mtext("Single line MTEXT",
                  dxfattribs={"insert": (0, 150), "char_height": 5, "color": 1})

    # Multiline with \P paragraph breaks
    msp.add_mtext("Line one\\PLine two\\PLine three",
                  dxfattribs={"insert": (0, 200), "char_height": 5, "color": 2})

    # With wrapping (set width)
    msp.add_mtext("This is a longer text that should wrap within the defined width boundary for testing purposes.",
                  dxfattribs={"insert": (0, 250), "char_height": 5, "width": 80, "color": 3})

    # With bold/italic formatting codes
    msp.add_mtext("{\\b Bold text} and {\\i Italic text} and normal",
                  dxfattribs={"insert": (0, 300), "char_height": 5, "color": 4})

    path = os.path.join(OUTPUT_DIR, "stage02_text.dxf")
    doc.saveas(path)
    print(f"  Created {path}")


def stage03_dimensions():
    """DimensionLinear, DimensionAligned, DimensionAngular3Pt, DimensionRadius,
    DimensionDiameter, DimensionOrdinate. Calls render() to create anonymous *D blocks."""
    doc = ezdxf.new("R2010")
    msp = doc.modelspace()

    # Linear dimension
    dim = msp.add_linear_dim(
        base=(50, 30),
        p1=(0, 0),
        p2=(100, 0),
        dimstyle="Standard",
    )
    dim.render()

    # Aligned dimension
    dim = msp.add_aligned_dim(
        p1=(0, 50),
        p2=(80, 100),
        distance=10,
        dimstyle="Standard",
    )
    dim.render()

    # Angular 3-point dimension
    dim = msp.add_angular_dim_3p(
        base=(150, 50),
        center=(150, 0),
        p1=(200, 0),
        p2=(150, 50),
        dimstyle="Standard",
    )
    dim.render()

    # Radius dimension
    dim = msp.add_radius_dim(
        center=(300, 50),
        radius=30,
        angle=45,
        dimstyle="Standard",
    )
    dim.render()

    # Diameter dimension
    dim = msp.add_diameter_dim(
        center=(400, 50),
        radius=25,
        angle=60,
        dimstyle="Standard",
    )
    dim.render()

    # Ordinate dimension
    dim = msp.add_ordinate_dim(
        feature_location=(0, 150),
        offset=(20, 5),
        dtype=0,
        dimstyle="Standard",
    )
    dim.render()

    path = os.path.join(OUTPUT_DIR, "stage03_dimensions.dxf")
    doc.saveas(path)
    print(f"  Created {path}")


def stage04_multileader():
    """MultiLeader with MTEXT content, straight leader."""
    doc = ezdxf.new("R2010")
    msp = doc.modelspace()

    from ezdxf.render.mleader import ConnectionSide
    from ezdxf.math import Vec2

    ml_builder = msp.add_multileader_mtext("Standard")
    ml_builder.set_content("Leader Text", style="Standard")
    ml_builder.add_leader_line(ConnectionSide.left, [Vec2(0, 0)])
    ml_builder.build(insert=Vec2(50, 30))

    path = os.path.join(OUTPUT_DIR, "stage04_multileader.dxf")
    doc.saveas(path)
    print(f"  Created {path}")


def stage05_hatch():
    """Solid fill, ANSI31 pattern, island (outer+hole), arc-boundary edge path."""
    doc = ezdxf.new("R2010")
    msp = doc.modelspace()

    # Solid fill hatch
    hatch1 = msp.add_hatch(color=1)
    hatch1.paths.add_polyline_path(
        [(0, 0), (50, 0), (50, 50), (0, 50)], is_closed=True
    )

    # ANSI31 pattern hatch
    hatch2 = msp.add_hatch(color=2)
    hatch2.set_pattern_fill("ANSI31", scale=1.0)
    hatch2.paths.add_polyline_path(
        [(60, 0), (110, 0), (110, 50), (60, 50)], is_closed=True
    )

    # Island: outer boundary + inner hole
    hatch3 = msp.add_hatch(color=3)
    # Outer boundary
    hatch3.paths.add_polyline_path(
        [(120, 0), (200, 0), (200, 80), (120, 80)], is_closed=True
    )
    # Inner hole
    hatch3.paths.add_polyline_path(
        [(140, 20), (180, 20), (180, 60), (140, 60)],
        is_closed=True,
        flags=ezdxf.const.BOUNDARY_PATH_OUTERMOST,
    )

    # Arc-boundary edge path
    hatch4 = msp.add_hatch(color=4)
    ep = hatch4.paths.add_edge_path()
    ep.add_line((210, 0), (260, 0))
    ep.add_arc(center=(235, 0), radius=25, start_angle=0, end_angle=180)
    ep.add_line((210, 0), (210, 0))

    path = os.path.join(OUTPUT_DIR, "stage05_hatch.dxf")
    doc.saveas(path)
    print(f"  Created {path}")


def stage06_image_underlay():
    """IMAGE (ref to test_image.png), PDFUNDERLAY (ref to test_underlay.pdf).
    External files won't actually be valid; the test handles this gracefully."""
    doc = ezdxf.new("R2010")
    msp = doc.modelspace()

    # IMAGE with reference to a tiny PNG
    image_def = doc.add_image_def(filename="test_image.png", size_in_pixel=(1, 1))
    msp.add_image(
        insert=(0, 0),
        size_in_units=(50, 50),
        image_def=image_def,
        rotation=0,
    )

    # PDFUNDERLAY
    underlay_def = doc.add_underlay_def(
        filename="test_underlay.pdf", name="1"
    )
    msp.add_underlay(underlay_def, insert=(100, 0), scale=1.0)

    path = os.path.join(OUTPUT_DIR, "stage06_image_underlay.dxf")
    doc.saveas(path)
    print(f"  Created {path}")


def stage07_mline():
    """MLine entities with different justifications."""
    doc = ezdxf.new("R2010")
    msp = doc.modelspace()

    # MLine 1 - simple path
    msp.add_mline(
        [(0, 0), (50, 0), (50, 50)],
        close=False,
        dxfattribs={"color": 1},
    )

    # MLine 2 - closed path, different justification
    ml2 = msp.add_mline(
        [(100, 0), (150, 0), (150, 50), (100, 50)],
        close=True,
        dxfattribs={"color": 2},
    )
    ml2.dxf.justification = 1  # Top justification

    # MLine 3 - bottom justification
    ml3 = msp.add_mline(
        [(200, 0), (250, 0), (250, 50)],
        close=False,
        dxfattribs={"color": 3},
    )
    ml3.dxf.justification = 2  # Bottom justification

    path = os.path.join(OUTPUT_DIR, "stage07_mline.dxf")
    doc.saveas(path)
    print(f"  Created {path}")


def stage08_ray_xline():
    """Ray (3 directions), XLine (3 directions)."""
    doc = ezdxf.new("R2010")
    msp = doc.modelspace()

    # Rays from origin in 3 directions
    msp.add_ray((0, 0, 0), (1, 0, 0), dxfattribs={"color": 1})
    msp.add_ray((0, 0, 0), (0, 1, 0), dxfattribs={"color": 2})
    msp.add_ray((0, 0, 0), (1, 1, 0), dxfattribs={"color": 3})

    # XLines through origin in 3 directions
    msp.add_xline((50, 0, 0), (1, 0, 0), dxfattribs={"color": 4})
    msp.add_xline((50, 0, 0), (0, 1, 0), dxfattribs={"color": 5})
    msp.add_xline((50, 0, 0), (1, 1, 0), dxfattribs={"color": 6})

    path = os.path.join(OUTPUT_DIR, "stage08_ray_xline.dxf")
    doc.saveas(path)
    print(f"  Created {path}")


def stage09_tolerance():
    """Tolerance with GD&T content strings."""
    doc = ezdxf.new("R2010")
    msp = doc.modelspace()

    msp.new_entity("TOLERANCE", dxfattribs={
        "content": "{\\Fgdt;j}%%v0.05%%vA%%vB",
        "insert": (0, 0, 0),
        "color": 1,
        "dimstyle": "Standard",
    })

    path = os.path.join(OUTPUT_DIR, "stage09_tolerance.dxf")
    doc.saveas(path)
    print(f"  Created {path}")


def generate_test_image():
    """Generate a tiny 1x1 pixel PNG for stage 06 IMAGE entity."""
    # Minimal valid PNG: 1x1 white pixel
    png_path = os.path.join(OUTPUT_DIR, "test_image.png")

    # PNG file structure: signature + IHDR + IDAT + IEND
    import zlib

    def png_chunk(chunk_type, data):
        chunk = chunk_type + data
        return (
            struct.pack(">I", len(data))
            + chunk
            + struct.pack(">I", zlib.crc32(chunk) & 0xFFFFFFFF)
        )

    signature = b"\x89PNG\r\n\x1a\n"
    # IHDR: 1x1, 8-bit RGB
    ihdr_data = struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0)
    ihdr = png_chunk(b"IHDR", ihdr_data)
    # IDAT: single row, filter byte 0, white pixel (255, 255, 255)
    raw = b"\x00\xff\xff\xff"
    idat = png_chunk(b"IDAT", zlib.compress(raw))
    iend = png_chunk(b"IEND", b"")

    with open(png_path, "wb") as f:
        f.write(signature + ihdr + idat + iend)

    print(f"  Created {png_path}")


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    print("Generating DXF fixtures...")

    stage00_primitives()
    stage01_inserts()
    stage02_text()
    stage03_dimensions()
    stage04_multileader()
    stage05_hatch()
    stage06_image_underlay()
    stage07_mline()
    stage08_ray_xline()
    stage09_tolerance()
    generate_test_image()

    print("Done.")


if __name__ == "__main__":
    main()
