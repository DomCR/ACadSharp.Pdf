using ACadSharp.IO;
using Svg;
using System.Drawing.Imaging;
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
			MemoryStream svgStream = new MemoryStream();
			using (SvgWriter writer = new SvgWriter(svgStream, document))
			{
				writer.Write();
			}

			var svg = SvgDocument.Open<SvgDocument>(new MemoryStream(svgStream.GetBuffer()));
			svg.Transforms.Clear();

			var bitmap = svg.Draw();
			bitmap.RotateFlip(System.Drawing.RotateFlipType.Rotate180FlipX);

			MemoryStream imageStream = new MemoryStream();
			bitmap.Save(imageStream, ImageFormat.Png);

			return new DwgPreview(DwgPreview.PreviewType.Png, new byte[80], imageStream.GetBuffer());
		}
	}
}
