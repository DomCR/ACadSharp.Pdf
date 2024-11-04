namespace ACadSharp.Pdf.Examples
{
	public static class PdfExporterExamples
	{
		/// <summary>
		/// Example of how to print the model space in a pdf page.
		/// </summary>
		/// <param name="document"></param>
		/// <param name="pdfPath"></param>
		public static void AddModelSpace(CadDocument document, string pdfPath)
		{
			PdfExporter exporter = new PdfExporter(pdfPath);

			//Add the model space into the file, page size will be adapted to include all entities
			exporter.AddModelSpace(document);

			exporter.Close();
		}

		/// <summary>
		/// Example of how to add all paper layouts into the pdf document.
		/// </summary>
		/// <param name="document"></param>
		/// <param name="pdfPath"></param>
		public static void AddPaperLayout(CadDocument document, string pdfPath)
		{
			PdfExporter exporter = new PdfExporter(pdfPath);

			//Add all paper layouts in the pdf document
			exporter.AddPaperLayouts(document);

			exporter.Close();
		}
	}
}
