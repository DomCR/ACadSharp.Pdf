using System.Drawing;
using ACadSharp.Pdf.Core;

namespace ACadSharp.Pdf
{
	public class PdfDocument
	{
		public PdfInformation Information { get; } = new PdfInformation();

		public PdfCatalog Catalog { get; }

		public PdfPages Pages { get; } = new();

		public PdfDocument()
		{
			this.Catalog = new PdfCatalog(this);
		}
	}
}
