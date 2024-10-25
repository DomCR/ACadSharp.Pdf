using ACadSharp.IO;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests
{
	public class PdfExporterTests
	{
		private CadDocument _document;
		private readonly ITestOutputHelper _output;

		public PdfExporterTests(ITestOutputHelper output)
		{
			this._output = output;
		}

		[Fact]
		public void AddModelSpaceTest()
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, "model.pdf");
			PdfExporter exporter = this.getPdfExporter(filename, this.getDocument());
			exporter.AddModelSpace();
			exporter.Close();
		}

		[Fact]
		public void AddPaperSpaceTest()
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, "paper.pdf");
			PdfExporter exporter = this.getPdfExporter(filename, this.getDocument());
			exporter.Add(exporter.Configuration.ReferenceDocument.Layouts["Layout1"]);
			exporter.Close();
		}

		[Fact]
		public void AddBlockTest()
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, "layout1.pdf");
			PdfExporter exporter = this.getPdfExporter(filename, this.getDocument());
			exporter.Add(exporter.Configuration.ReferenceDocument.Layouts["Layout1"]);
			exporter.Close();
		}

		private PdfExporter getPdfExporter(string path, CadDocument document)
		{
			PdfExporter exporter = new PdfExporter(path);

			exporter.Configuration.OnNotification += this.onNotification;
			exporter.Configuration.ReferenceDocument = document;

			return exporter;
		}

		private CadDocument getDocument()
		{
			if (this._document == null)
			{
				this._document = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, "export_sample.dwg"));
			}
			return this._document;
		}

		private void onNotification(object sender, NotificationEventArgs e)
		{
			this._output.WriteLine(e.Message);
		}
	}
}