using ACadSharp.IO;
using System.IO;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class PdfExporterTests
	{
		private CadDocument _document;

		[Fact]
		public void AddModelSpaceTest()
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, "model.pdf");
			using (PdfExporter exporter = new PdfExporter(filename))
			{
				exporter.Configuration.ReferenceDocument = this.getDocument();
				exporter.AddModelSpace();

				exporter.Close();
			}
		}

		[Fact]
		public void AddPaperSpaceTest()
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, "paper.pdf");
			using (PdfExporter exporter = new PdfExporter(filename))
			{
				exporter.Configuration.ReferenceDocument = this.getDocument();
				exporter.AddPaperSpaces();

				exporter.Close();
			}
		}

		private CadDocument getDocument()
		{
			if (this._document == null)
			{
				this._document = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, "export_sample.dwg"));
			}
			return this._document;
		}
	}
}