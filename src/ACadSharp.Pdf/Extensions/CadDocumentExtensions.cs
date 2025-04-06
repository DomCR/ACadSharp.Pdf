using ACadSharp.IO;
using Svg;
using System.Drawing.Imaging;
using System.IO;

namespace ACadSharp.Pdf.Extensions
{
	public static class CadDocumentExtensions
	{
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
