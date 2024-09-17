using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Example
{
	public static class PdfSharpExamples
	{
		public static void HelloWorld(string filename)
		{
			// Create a new PDF document
			PdfDocument document = new PdfDocument();
			document.Info.Title = "Created with PDFsharp";

			// Create an empty page
			PdfPage page = document.AddPage();

			// Get an XGraphics object for drawing
			XGraphics gfx = XGraphics.FromPdfPage(page);

			// Create a font
			XFont font = new XFont("Verdana", 20, XFontStyleEx.Bold);

			// Draw the text
			gfx.DrawString("Hello, World!", font, XBrushes.Black,
			  new XRect(0, 0, page.Width.Point, page.Height.Point),
			  XStringFormats.Center);

			// Save the document...
			document.Save(filename);
		}

		public static void Landscape(string filename)
		{
			// Create a new PDF document
			PdfDocument document = new PdfDocument();
			document.Info.Title = "Created with PDFsharp";

			// Create an empty page
			PdfPage page = document.AddPage();
			page.Orientation = PdfSharp.PageOrientation.Landscape;

			// Get an XGraphics object for drawing
			XGraphics gfx = XGraphics.FromPdfPage(page);

			// Create a font
			XFont font = new XFont("Verdana", 20, XFontStyleEx.Bold);

			// Draw the text
			gfx.DrawString("Hello, World!", font, XBrushes.Black,
			  new XRect(0, 0, page.Width.Point, page.Height.Point),
			  XStringFormats.Center);

			// Save the document...
			document.Save(filename);
		}

		public static void DrawDiagonal(string filename)
		{
			PdfDocument document = new PdfDocument();

			PdfPage page = document.AddPage();
			page.Height = new XUnit(10, XGraphicsUnit.Inch);
			page.Width = new XUnit(10, XGraphicsUnit.Inch);

			var pen = new XPen(XColor.FromArgb(125, 0, 0), 0.05);

			XGraphics gfx = XGraphics.FromPdfPage(page);

			gfx.DrawLine(pen, 1, 2, page.Width.Point / 2, page.Height.Point / 2);

			// Save the document...
			document.Save(filename);
		}
	}
}
