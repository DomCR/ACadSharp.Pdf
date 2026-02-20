# Stage 06: UNDERLAY (PDF/IMAGE)

## Overview

IMAGE and PDFUNDERLAY entities embed external raster images and PDF pages into a DXF drawing. IMAGE places a bitmap file (BMP, TIFF, JPEG, PNG, etc.) at a specified position with an affine transformation. PDFUNDERLAY places a page from an external PDF file similarly.

Both entity types share a common pattern: they reference an external definition object (IMAGEDEF or PDFDEFINITION) that specifies the file path, and the entity itself defines the placement (insertion point, scale/orientation vectors, clipping).

The primary challenge is that the referenced files are NOT embedded in the DXF -- they are external files that must be resolved from the file system. For PDF underlays, the PDF page must be rasterized into a bitmap before it can be included in the output PDF (to avoid PDF-in-PDF nesting complexity).

The target module for this stage is `UnderlayRasterCache.cs`.

---

## Domain Knowledge

### IMAGE Entity

The IMAGE entity embeds a raster image reference into the drawing.

**Key group codes**:

| Group Code | Field | Description |
|-----------|-------|-------------|
| 10/20/30 | Insertion point | WCS position of the image origin (bottom-left corner in image coordinates) |
| 11/21/31 | U-vector | Direction and scale of one pixel in the horizontal direction (WCS in ACadSharp). Length = one pixel width in drawing units |
| 12/22/32 | V-vector | Direction and scale of one pixel in the vertical direction (WCS in ACadSharp). Length = one pixel height in drawing units |
| 13/23 | Image size | Width and height of the image in pixels |
| 340 | IMAGEDEF handle | Reference to the IMAGEDEF object |
| 360 | IMAGEDEF_REACTOR handle | Reference to reactor |
| 70 | Display properties | Bit flags (ACadSharp `ImageDisplayFlags`): 1=show, 2=show when not aligned, 4=use clipping boundary, 8=transparency on |
| 71 | Clipping type | 1=rectangular, 2=polygonal |
| 91 | Number of clip vertices | Number of clipping boundary vertices |
| 14/24 | Clip vertices | Clipping boundary vertices in pixel coordinates |
| 280 | Clipping state | 0=off, 1=on |
| 281 | Brightness | 0-100 (default 50) |
| 282 | Contrast | 0-100 (default 50) |
| 283 | Fade | 0-100 (default 0) |

### IMAGEDEF Object

The IMAGEDEF object defines the image file:

| Group Code | Field | Description |
|-----------|-------|-------------|
| 1 | Filename | Path to the image file (relative or absolute) |
| 10/20 | Image size | Width and height in pixels |
| 11/21 | Pixel size | Default size of one pixel in drawing units |

The IMAGEDEF does NOT contain the actual image data -- it references an external file.

### Image Affine Transform

The IMAGE entity defines its placement through two vectors (U and V) that represent the mapping of one pixel in the horizontal and vertical directions:

```
WorldPoint = InsertionPoint + px * U_vector + (imageHeight - py - 1) * V_vector
```

Where `(px, py)` is a pixel coordinate with (0,0) at the top-left of the image.

The V-vector typically points upward (since DXF Y-axis is up), while image pixel Y increases downward. Hence the `(imageHeight - py - 1)` term.

The U and V vectors encode:
- **Position**: The insertion point is the image origin in WCS
- **Scale**: The magnitude of U and V vectors determines the size of each pixel in drawing units
- **Rotation**: The direction of U and V determines the rotation
- **Shear**: If U and V are not perpendicular, the image is sheared

The affine transform matrix:
```
| Ux  Vx  InsertX |
| Uy  Vy  InsertY |
| 0   0   1       |
```

### Image Clipping

Clipping restricts which portion of the image is visible:

**Default clipping** (no clip or rectangular):
- Rectangle from (-0.5, -0.5) to (width-0.5, height-0.5) in pixel coordinates
- The -0.5 offset accounts for the center-of-pixel convention

**Rectangular clipping** (type 1):
- Two vertices: top-left and bottom-right corners in pixel coordinates
- Applied as a rectangular mask

**Polygonal clipping** (type 2):
- N vertices forming a closed polygon in pixel coordinates
- The polygon clips the image to an arbitrary shape

Clipping vertices are in **pixel coordinates** (not drawing units). They must be transformed to drawing units using the U/V vectors before applying to the rendered image.

### Display Properties

- **Show/hide** (bit 1): If not set, image is invisible
- **Use clipping boundary** (bit 4): Apply clip boundary vertices if clipping is enabled
- **Transparency** (bit 8): Treat background/alpha as transparent (implementation-dependent)
- **Brightness/Contrast/Fade**: Adjust the image appearance. These require pixel-level manipulation.

### PDFUNDERLAY Entity

The PDFUNDERLAY entity references an external PDF file page:

**Key group codes**:

| Group Code | Field | Description |
|-----------|-------|-------------|
| 10/20/30 | Insertion point | WCS position |
| 41 | Scale X | Horizontal scale factor |
| 42 | Scale Y | Vertical scale factor |
| 43 | Scale Z | (usually 1.0) |
| 50 | Rotation | DXF stores degrees; ACadSharp exposes radians via `UnderlayEntity.Rotation` |
| 340 | PDFDEFINITION handle | Reference to the PDFDEFINITION object |
| 210/220/230 | Normal vector | OCS extrusion |

### PDFDEFINITION Object

| Group Code | Field | Description |
|-----------|-------|-------------|
| 1 | Filename | Path to the PDF file (relative or absolute) |
| 2 | Page name | Internal page name/number |

### PDF Rasterization

To include a PDF underlay in the output, the referenced PDF page must be rasterized:

1. Load the external PDF file
2. Render the specified page to a bitmap at a target DPI
3. The rasterized bitmap is then treated like a regular IMAGE

**Rasterization libraries for .NET**:

| Library | License | Platforms | Notes |
|---------|---------|-----------|-------|
| PDFiumSharpV2 | Verify | Win/Linux/Mac | .NET wrapper around PDFium. Good performance. NuGet package available. |
| Docnet | MIT | Win/Linux/Mac | Cross-platform PDF renderer. NuGet package. |
| PdfiumViewer | Apache 2.0 | Windows only | WinForms-based, not cross-platform. |
| SkiaSharp + PDFium | Various | Cross-platform | Can render PDF to SKBitmap. |

Recommended: choose a cross-platform PDF rasterizer and record its exact license/redistribution constraints in the repo (license terms vary by wrapper and native binaries).

### Caching Strategy

Multiple IMAGE entities can reference the same IMAGEDEF, and multiple PDFUNDERLAY entities can reference the same PDFDEFINITION. Loading and rasterizing the same file multiple times is wasteful.

A cache keyed by `(filepath, page_number, target_dpi)` should store:
- Loaded bitmap data (pixel array)
- Image dimensions
- Metadata (color depth, transparency info)

Cache eviction: LRU (Least Recently Used) with a configurable maximum memory limit.

---

## External Reference Code

### ezdxf Image Tutorial (MIT License)
- **URL**: https://ezdxf.readthedocs.io/en/stable/tutorials/image.html
- **What to study**: How ezdxf creates and handles IMAGE entities. Shows the relationship between IMAGE and IMAGEDEF, and how the U/V vectors define placement.

### ezdxf Underlay Tutorial (MIT License)
- **URL**: https://ezdxf.readthedocs.io/en/stable/tutorials/underlay.html
- **What to study**: PDFUNDERLAY handling, including the scale/rotation/insertion point semantics and the PDFDEFINITION reference.

### PDFiumSharpV2 (MS-RL License)
- **URL**: https://github.com/ArtifexSoftware/PDFiumSharpV2 (or NuGet: PDFiumSharpV2)
- **What to study**: API for loading a PDF document, accessing pages, rendering to bitmap. Key classes:
  - `PdfDocument.Load(path)`: Open a PDF file
  - `document.Pages[n]`: Access page by index
  - `page.Render(width, height, dpi)`: Render page to bitmap

### SixLabors.ImageSharp (Apache 2.0 License)
- **URL**: https://github.com/SixLabors/ImageSharp
- **What to study**: Cross-platform image loading library for .NET. Can load BMP, PNG, JPEG, TIFF, GIF. Use for loading IMAGE entity referenced files. NuGet: `SixLabors.ImageSharp`.

---

## Step-by-Step Implementation Plan

### Step 1: Create UnderlayRasterCache Class

**What**: A caching layer for loading external image files and rasterizing PDF pages.

**Key structure**:
```csharp
class UnderlayRasterCache
{
    private Dictionary<CacheKey, CachedImage> _cache = new();
    private long _currentMemoryUsage = 0;
    private long _maxMemoryBytes = 256 * 1024 * 1024; // 256 MB default

    // Load an image file (BMP, PNG, JPEG, TIFF, etc.)
    CachedImage LoadImage(string filePath)
    {
        var key = new CacheKey(filePath, 0, 0);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        // Load using ImageSharp or System.Drawing
        byte[] pixels = LoadImageFile(filePath, out int width, out int height);

        var image = new CachedImage(pixels, width, height);
        AddToCache(key, image);
        return image;
    }

    // Rasterize a PDF page
    CachedImage RasterizePdfPage(string filePath, int pageNumber, int dpi)
    {
        var key = new CacheKey(filePath, pageNumber, dpi);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        // Rasterize using PDFiumSharpV2 or similar
        byte[] pixels = RasterizePdf(filePath, pageNumber, dpi, out int width, out int height);

        var image = new CachedImage(pixels, width, height);
        AddToCache(key, image);
        return image;
    }

    void AddToCache(CacheKey key, CachedImage image)
    {
        long imageBytes = (long)image.Width * image.Height * 4; // RGBA
        while (_currentMemoryUsage + imageBytes > _maxMemoryBytes && _cache.Count > 0)
        {
            EvictLRU();
        }
        _cache[key] = image;
        _currentMemoryUsage += imageBytes;
    }
}

record CacheKey(string FilePath, int PageNumber, int Dpi);

class CachedImage
{
    public byte[] Pixels; // RGBA bytes
    public int Width;
    public int Height;
}
```

**Input**: File path, optional page number and DPI.

**Output**: Loaded/cached bitmap data.

**Edge cases**:
- File not found: log warning, return placeholder (or skip entity)
- Relative file path: resolve relative to the DXF file's directory
- Very large images: may exceed memory limits; consider downsampling
- Corrupt image file: catch exceptions, log, skip

---

### Step 2: Implement Image File Resolution

**What**: Resolve image file paths from IMAGEDEF/PDFDEFINITION, handling relative paths and missing files.

**Algorithm**:
```csharp
string ResolveFilePath(string dxfFilePath, string referencedPath)
{
    // 1. Try as absolute path
    if (Path.IsPathRooted(referencedPath) && File.Exists(referencedPath))
        return referencedPath;

    // 2. Try relative to DXF file directory
    if (!string.IsNullOrEmpty(dxfFilePath))
    {
        string dxfDir = Path.GetDirectoryName(dxfFilePath);
        string resolved = Path.Combine(dxfDir, referencedPath);
        if (File.Exists(resolved))
            return resolved;
    }

    // 3. Try relative to current directory
    if (File.Exists(referencedPath))
        return Path.GetFullPath(referencedPath);

    // 4. Try stripping directory parts (just filename)
    string fileName = Path.GetFileName(referencedPath);
    if (!string.IsNullOrEmpty(dxfFilePath))
    {
        string dxfDir = Path.GetDirectoryName(dxfFilePath);
        string resolved = Path.Combine(dxfDir, fileName);
        if (File.Exists(resolved))
            return resolved;
    }

    // Not found
    return null;
}
```

**Input**: DXF file path, referenced file path from IMAGEDEF/PDFDEFINITION.

**Output**: Resolved absolute file path, or null if not found.

**Edge cases**:
- Windows-style paths on Linux (backslash separators): convert to platform separator
- UNC paths (network shares): attempt resolution, handle timeouts
- URL paths (http://...): not supported, log warning
- Path with special characters or spaces

---

### Step 3: Implement IMAGE Entity Rendering

**What**: Render an IMAGE entity as an ImageNode render primitive.

**Algorithm**:
```csharp
List<RenderNode> RenderImage(RasterImage image, Matrix4 parentTransform)
{
    // 1. Get IMAGEDEF
    var imageDef = image.Definition; // via handle 340
    if (imageDef == null)
    {
        _log.Skip(image, "missing IMAGEDEF");
        return empty;
    }

    // 2. Check visibility
    if (!image.ShowImage)
    {
        _log.Skip(image, "image hidden");
        return empty;
    }

    // 3. Resolve and load image file
    string filePath = ResolveFilePath(dxfPath, imageDef.Filename);
    if (filePath == null)
    {
        _log.Skip(image, $"image file not found: {imageDef.Filename}");
        return empty;
    }

    CachedImage bitmap = _cache.LoadImage(filePath);

    // 4. Compute affine transform
    XYZ insertPoint = image.InsertPoint;
    XYZ uVector = image.UVector; // one pixel in X direction
    XYZ vVector = image.VVector; // one pixel in Y direction

    // The image transform maps pixel coords to WCS:
    // WCS = InsertionPoint + px * U + py * V
    // Note: V typically points up, image Y points down
    // So we use (imageHeight - py) * V for correct orientation

    // Build a pixel→WCS transform as:
    //   WCS(px, py) = InsertPoint + px * UVector + py * VVector
    // Prefer a dedicated helper to avoid matrix-layout mistakes.
    var imageTransform = TransformHelper.ImagePixelToWcs(insertPoint, uVector, vVector);

    // 5. Handle clipping
    PathNode clipPath = null;
    if (image.ClippingState && image.Flags.HasFlag(ImageDisplayFlags.UseClippingBoundary))
    {
        clipPath = ComputeImageClipPath(image, imageTransform, parentTransform);
    }

    // 6. Create ImageNode
    var imageNode = new ImageNode
    {
        PixelData = bitmap.Pixels,
        Width = bitmap.Width,
        Height = bitmap.Height,
        Transform = parentTransform * imageTransform,
        ClipRegion = clipPath,
        // Brightness/contrast/fade exist on the entity model; applying them requires
        // pixel-level processing and can be implemented as a later enhancement.
        SourceHandle = image.Handle,
    };

    return new List<RenderNode> { imageNode };
}
```

**Input**: IMAGE entity, parent transform.

**Output**: ImageNode render primitive.

**Edge cases**:
- U and V vectors are zero length: degenerate, skip
- U and V vectors are parallel: shear is extreme, image may appear as a line
- Image file is very large (>100MP): consider downsampling before caching
- Transparency enabled but image has no alpha channel: no effect

---

### Step 4: Implement Image Clipping Path Computation

**What**: Convert image clipping vertices (in pixel coordinates) to a world-space clip path.

**Algorithm**:
```csharp
PathNode ComputeImageClipPath(RasterImage image, Matrix4 imageTransform, Matrix4 parentTransform)
{
    var clipVertices = image.ClipVertices; // in pixel coordinates

    if (image.ClippingType == 1) // Rectangular
    {
        // Two vertices: top-left and bottom-right
        XY topLeft = clipVertices[0];
        XY bottomRight = clipVertices[1];

        clipVertices = new List<XY>
        {
            topLeft,
            new XY(bottomRight.X, topLeft.Y),
            bottomRight,
            new XY(topLeft.X, bottomRight.Y),
        };
    }

    // Transform clip vertices from pixel coords to world coords
    var worldVertices = clipVertices.Select(pv =>
    {
        // Pixel coord -> WCS: apply image transform
        var worldPoint = Vector3.Transform(
            new Vector3((float)pv.X, (float)pv.Y, 0),
            imageTransform);
        // Apply parent transform
        worldPoint = Vector3.Transform(worldPoint, parentTransform);
        return new XY(worldPoint.X, worldPoint.Y);
    }).ToList();

    // Build closed polygon path
    var path = new PathNode();
    path.Segments.Add(new MoveToSegment { Point = worldVertices[0] });
    for (int i = 1; i < worldVertices.Count; i++)
        path.Segments.Add(new LineToSegment { Point = worldVertices[i] });
    path.Segments.Add(new CloseSegment());

    return path;
}
```

**Input**: IMAGE clipping data, transforms.

**Output**: PathNode defining the clip region in world coordinates.

**Edge cases**:
- No clip vertices provided: use full image extent as clip
- Clip polygon is self-intersecting: may cause rendering artifacts, proceed anyway
- Rectangular clip with inverted coordinates: normalize min/max

---

### Step 5: Implement PDFUNDERLAY Entity Rendering

**What**: Render a PDFUNDERLAY entity by rasterizing the referenced PDF page.

**Algorithm**:
```csharp
List<RenderNode> RenderPdfUnderlay(PdfUnderlay underlay, Matrix4 parentTransform)
{
    // 1. Get PDFDEFINITION
    var pdfDef = underlay.PdfDefinition; // via handle 340
    if (pdfDef == null)
    {
        _log.Skip(underlay, "missing PDFDEFINITION");
        return empty;
    }

    // 2. Resolve PDF file
    string filePath = ResolveFilePath(dxfPath, pdfDef.Filename);
    if (filePath == null)
    {
        _log.Skip(underlay, $"PDF file not found: {pdfDef.Filename}");
        return empty;
    }

    // 3. Determine page number
    int pageNumber = ParsePageNumber(pdfDef.PageName); // "1" -> 0 (zero-based)

    // 4. Choose rasterization DPI
    int dpi = DetermineTargetDpi(underlay, parentTransform);

    // 5. Rasterize PDF page
    CachedImage bitmap = _cache.RasterizePdfPage(filePath, pageNumber, dpi);

    // 6. Compute placement transform
    XYZ insertPoint = underlay.InsertPoint;
    double scaleX = underlay.ScaleX;
    double scaleY = underlay.ScaleY;
    double rotation = underlay.Rotation; // radians in ACadSharp

    // PDF page dimensions in drawing units
    // The page is rendered at the given DPI, so pixel dimensions are:
    // width_pixels = page_width_inches * dpi
    // The scale factors map the page into drawing units

    var underlayTransform = Matrix4.GetArbitraryAxis(underlay.Normal) *
        // Follow Stage 00 transform conventions (T * R * S). Angles are radians in ACadSharp.
        Matrix4.CreateTranslation(insertPoint) *
        Matrix4.CreateFromAxisAngle(XYZ.AxisZ, rotation) *
        Matrix4.CreateScale(new XYZ(scaleX, scaleY, 1));

    // 7. Create ImageNode
    var imageNode = new ImageNode
    {
        PixelData = bitmap.Pixels,
        Width = bitmap.Width,
        Height = bitmap.Height,
        Transform = parentTransform * underlayTransform,
        SourceHandle = underlay.Handle,
    };

    return new List<RenderNode> { imageNode };
}
```

**Input**: PdfUnderlay entity, parent transform.

**Output**: ImageNode with rasterized PDF content.

**Edge cases**:
- PDF file is password-protected: PDFium can handle some cases, log error for others
- PDF page number out of range: log warning, skip
- Very large PDF page at high DPI: limit maximum raster size (e.g., 4096x4096)
- PDF contains transparent elements: use RGBA rasterization if possible

---

### Step 6: Implement DPI Selection for PDF Rasterization

**What**: Choose appropriate DPI for rasterizing PDF underlays based on the viewport scale.

**Algorithm**:
```csharp
int DetermineTargetDpi(PdfUnderlay underlay, Matrix4 parentTransform)
{
    // Extract the effective scale from the parent transform
    double effectiveScale = ExtractScaleFactor(parentTransform);

    // Scale by the underlay's own scale
    double totalScale = effectiveScale * Math.Max(
        Math.Abs(underlay.ScaleX), Math.Abs(underlay.ScaleY));

    // Target: at final PDF output, aim for 150-300 DPI apparent resolution
    // A higher total scale means we need higher rasterization DPI
    int baseDpi = 150;
    int targetDpi = (int)(baseDpi / totalScale);

    // Clamp to reasonable range
    return Math.Clamp(targetDpi, 72, 600);
}
```

**Input**: Underlay scale, parent transform.

**Output**: Target DPI for rasterization.

**Edge cases**:
- Very small scale (zoomed way out): use low DPI (72)
- Very large scale (zoomed in): cap at 600 DPI to prevent memory exhaustion

---

### Step 7: Implement Image-to-PDF Embedding

**What**: Extend the PdfBackend to embed raster images in the PDF output.

**Algorithm**:
```csharp
void RenderImage(ImageNode image)
{
    // 1. Create a PDF image XObject
    int imageId = _nextImageId++;
    string imageName = $"Im{imageId}";

    // 2. Encode pixel data as PDF image stream
    // Options: DCTDecode (JPEG), FlateDecode (zlib-compressed raw), or CCITTFaxDecode (B&W)
    byte[] encodedData = EncodeImage(image.PixelData, image.Width, image.Height);

    // 3. Write image XObject to PDF
    _imageXObjects.Add(new PdfImageXObject
    {
        Name = imageName,
        Width = image.Width,
        Height = image.Height,
        ColorSpace = "DeviceRGB",
        BitsPerComponent = 8,
        Data = encodedData,
        Filter = "FlateDecode",
    });

    // 4. In the content stream, place the image with the transform
    // PDF image rendering: images are placed in a 1x1 unit square,
    // so the transform must scale to the desired size

    _sb.AppendLine("q"); // Save state

    // Apply the image transform (from drawing units to PDF points)
    var pdfTransform = _pageTransform * image.Transform;
    WriteConcatMatrix(pdfTransform);

    // Scale to image pixel dimensions (image occupies 1x1 in user space by default)
    _sb.AppendLine($"{image.Width} 0 0 {image.Height} 0 0 cm");

    // If clipping is needed
    if (image.ClipRegion != null)
    {
        RenderClipPath(image.ClipRegion);
        _sb.AppendLine("W n");
    }

    _sb.AppendLine($"/{imageName} Do"); // Draw image

    _sb.AppendLine("Q"); // Restore state
}
```

**Input**: ImageNode from the scene graph.

**Output**: PDF content stream operators + image XObject.

**Edge cases**:
- JPEG source images: can be embedded directly without re-encoding (DCTDecode passthrough)
- Large images: use FlateDecode for lossless compression
- Alpha channel: PDF supports SMask (soft mask) for transparency
- Monochrome mode: convert to grayscale before embedding

---

### Step 8: Implement Brightness/Contrast/Fade Adjustments

**What**: Apply image display property adjustments.

**Algorithm**:
```csharp
byte[] AdjustImage(byte[] pixels, int width, int height,
    int brightness, int contrast, int fade)
{
    // Brightness: 0-100, default 50. Maps to -1.0 to +1.0
    double brightnessFactor = (brightness - 50) / 50.0;

    // Contrast: 0-100, default 50. Maps to 0.0 to 2.0
    double contrastFactor = contrast / 50.0;

    // Fade: 0-100, default 0. Blends toward background color
    double fadeFactor = fade / 100.0;

    byte[] adjusted = new byte[pixels.Length];

    for (int i = 0; i < pixels.Length; i += 4) // RGBA
    {
        for (int c = 0; c < 3; c++) // R, G, B
        {
            double value = pixels[i + c] / 255.0;

            // Apply contrast (center around 0.5)
            value = (value - 0.5) * contrastFactor + 0.5;

            // Apply brightness
            value += brightnessFactor;

            // Apply fade (blend toward white)
            value = value * (1 - fadeFactor) + fadeFactor;

            adjusted[i + c] = (byte)Math.Clamp(value * 255, 0, 255);
        }
        adjusted[i + 3] = pixels[i + 3]; // Preserve alpha
    }

    return adjusted;
}
```

**Input**: Pixel data, brightness/contrast/fade values.

**Output**: Adjusted pixel data.

**Edge cases**:
- Default values (50, 50, 0): no adjustment needed, skip processing
- Extreme values: clamp to 0-255 range per channel

---

### Step 9: Integrate into EntityFrontend

**What**: Add IMAGE and PDFUNDERLAY cases to the EntityFrontend dispatcher.

```csharp
case Image image:
    return _underlayCache.RenderImage(image, worldTransform);
case PdfUnderlay underlay:
    return _underlayCache.RenderPdfUnderlay(underlay, worldTransform);
```

Also need to add the `UnderlayRasterCache` to the `PdfConfiguration` so callers can configure:
- Maximum cache size
- Base directory for file resolution
- Default DPI for PDF rasterization
- Whether to skip missing files silently or throw

---

### Step 10: Add Configuration Options

**What**: Extend `PdfConfiguration` with image/underlay settings.

```csharp
// In PdfConfiguration:
public string BasePath { get; set; }                // Base directory for resolving relative paths
public int MaxImageCacheMemoryMB { get; set; } = 256;  // Max cache memory
public int PdfUnderlayDpi { get; set; } = 150;          // Default rasterization DPI
public bool SkipMissingImages { get; set; } = true;      // Skip vs throw for missing files
public Dictionary<string, string> ImagePathOverrides { get; set; } // Manual path remapping
```

---

## Testing Strategy

### Unit Tests

1. **File path resolution**: Relative path resolved against DXF directory. Absolute path used directly.
2. **Missing file handling**: Non-existent file returns null, entity is skipped.
3. **Image affine transform**: U=(1,0,0), V=(0,1,0), Insert=(100,200). Verify pixel (0,0) maps to (100,200).
4. **Image rotation**: U and V rotated 45 degrees. Verify correct mapping.
5. **Image scale**: U=(2,0,0), V=(0,2,0). Verify pixel is 2x2 drawing units.
6. **Rectangular clipping**: Clip to half the image. Verify clip path vertices.
7. **Polygonal clipping**: Clip to triangle. Verify 3-vertex clip path.
8. **Brightness adjustment**: Brightness 75 increases all channel values.
9. **Contrast adjustment**: Contrast 100 increases difference from midpoint.
10. **Fade adjustment**: Fade 50 blends 50% toward white.
11. **Cache key**: Same file+page+dpi returns cached image. Different dpi triggers new load.
12. **Cache eviction**: Exceed memory limit, verify LRU entry is evicted.
13. **DPI selection**: Large underlay scale uses lower DPI. Small scale uses higher DPI.

### Integration Tests

14. **Simple IMAGE DXF**: DXF with an IMAGE entity referencing a PNG file. Verify image appears in PDF at correct position.
15. **Clipped IMAGE**: IMAGE with rectangular clip. Verify only clipped region is visible.
16. **Rotated IMAGE**: IMAGE with 45-degree rotation. Verify correct orientation.
17. **PDFUNDERLAY**: DXF with a PDF underlay. Verify PDF page is rasterized and placed correctly.
18. **Multiple images sharing IMAGEDEF**: Two IMAGE entities referencing the same IMAGEDEF. Verify cache hit.
19. **IMAGE in INSERT**: Image inside a block with scale and rotation.

---

## Dependencies

### Depends On
- **Stage 00 (Render Infrastructure)**: ImageNode primitive, Transform infrastructure, ClipNode, PropertyResolver
- **Stage 01 (INSERT/Blocks)**: Not directly, but images inside blocks use BlockExpander

### Enables
- No other stages directly depend on IMAGE/UNDERLAY

### External Dependencies
- **SixLabors.ImageSharp** (Apache 2.0, NuGet): For loading raster image files (BMP, PNG, JPEG, TIFF)
- **PDFiumSharpV2** (MS-RL, NuGet): For rasterizing PDF pages. This is an optional dependency; if not available, PDF underlays are skipped with a warning.
- ACadSharp `RasterImage` entity + `ImageDefinition` object, and `PdfUnderlay` entity + `PdfUnderlayDefinition` object
