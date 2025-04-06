using ACadSharp.IO;
using System.IO;
using System.Linq;
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
			CadDocument doc = this.getDocument();

			PdfExporter exporter = this.getPdfExporter(filename);
			exporter.AddModelSpace(doc);
			exporter.Close();
		}

		[Fact]
		public void AddLayoutTest()
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, "paper.pdf");
			CadDocument doc = this.getDocument();
			var layout = doc.Layouts["Layout1"];

			PdfExporter exporter = this.getPdfExporter(filename);
			exporter.Add(layout);
			exporter.Close();
		}

		[Fact]
		public void TextSample()
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, "text_sample.pdf");
			CadDocument doc = this.getDocument();
			var layout = doc.Layouts["text_sample"];

			var a = doc.Header.InsUnits;
			var b = doc.Header.MeasurementUnits;

			PdfExporter exporter = this.getPdfExporter(filename);
			exporter.Add(layout);
			exporter.Close();
		}

		[Fact]
		public void AddBlockTest()
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, "my_block.pdf");
			CadDocument doc = this.getDocument();

			PdfExporter exporter = this.getPdfExporter(filename);
			exporter.Add(doc.BlockRecords["my_block"]);
			exporter.Close();
		}

		private PdfExporter getPdfExporter(string path)
		{
			PdfExporter exporter = new PdfExporter(path);

			exporter.Configuration.OnNotification += this.onNotification;

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