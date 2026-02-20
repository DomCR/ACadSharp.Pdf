using ACadSharp.IO;
using SkiaSharp;
using Svg.Skia;
using System;
using System.IO;

namespace ACadSharp.Pdf.Extensions
{
	public static class CadDocumentExtensions
	{
		/// <summary>
		/// Create a <see cref="DwgPreview"/> with a png image in it using the ModelSpace as a reference.
		/// </summary>
		/// <param name="document">Document to create the preview image from.</param>
		/// <returns></returns>
		public static DwgPreview CreatePreview(this CadDocument document)
		{
			byte[] svgBytes;
			using (MemoryStream svgStream = new MemoryStream())
			{
				using (SvgWriter writer = new SvgWriter(svgStream, document))
				{
					writer.Write();
					svgBytes = svgStream.ToArray();
				}
			}

			SKSvg svg = new SKSvg();
			using MemoryStream input = new MemoryStream(svgBytes);
			SKPicture picture = svg.Load(input);
			if (picture == null)
			{
				throw new InvalidOperationException("Unable to rasterize preview: SVG payload could not be loaded.");
			}

			SKRect bounds = picture.CullRect;
			int width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
			int height = Math.Max(1, (int)Math.Ceiling(bounds.Height));

			using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas canvas = surface.Canvas;
			canvas.Clear(SKColors.Transparent);

			// Keep parity with previous output orientation (Rotate180 + FlipX == vertical flip).
			canvas.Translate(0, height);
			canvas.Scale(1f, -1f);
			canvas.DrawPicture(picture);
			canvas.Flush();

			using SKImage image = surface.Snapshot();
			using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
			byte[] imageBytes = data.ToArray();

			return new DwgPreview(DwgPreview.PreviewType.Png, new byte[80], imageBytes);
		}
	}
}
