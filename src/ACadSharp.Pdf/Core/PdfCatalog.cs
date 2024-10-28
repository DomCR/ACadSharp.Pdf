namespace ACadSharp.Pdf.Core
{
	public class PdfCatalog : PdfDictionary
	{
		private readonly PdfDocument _document;

		public PdfCatalog(PdfDocument document)
		{
			this._document = document;

			this.Items.Add("/Type", new PdfName("/Catalog"));
			this.Items.Add("/Pages", this._document.Pages.Reference);
		}
	}
}
