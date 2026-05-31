using ACadSharp.IO;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests
{
	public class PdfExporterTests
	{
		public static CadDocument Document { get; }

		public static readonly TheoryData<string> LayoutNames = new();

		private readonly ITestOutputHelper _output;

		static PdfExporterTests()
		{
			Document = TestUtils.GetDocument();

			foreach (var item in Document.Layouts)
			{
				if (!item.IsPaperSpace)
				{
					continue;
				}

				LayoutNames.Add(item.Name);
			}
		}

		public PdfExporterTests(ITestOutputHelper output)
		{
			this._output = output;
		}

		[Fact]
		public void AddBlockTest()
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, "my_block.pdf");
			CadDocument doc = TestUtils.GetDocument();

			PdfExporter exporter = this.getPdfExporter(filename);
			exporter.Add(doc.BlockRecords["my_block"]);
			exporter.Close();
		}

		[Fact]
		public void AddModelSpaceTest()
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, "model.pdf");
			CadDocument doc = TestUtils.GetDocument();

			PdfExporter exporter = this.getPdfExporter(filename);
			exporter.AddModelSpace(doc);
			exporter.Close();
		}

		[Theory]
		[MemberData(nameof(LayoutNames))]
		public void WriteLayouts(string name)
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, $"{name}.pdf");
			var layout = Document.Layouts[name];

			PdfExporter exporter = this.getPdfExporter(filename);
			exporter.Add(layout);
			exporter.Close();
		}

		private PdfExporter getPdfExporter(string path)
		{
			PdfExporter exporter = new PdfExporter(path);

			exporter.Configuration.OnNotification += this.onNotification;

			return exporter;
		}

		private void onNotification(object sender, NotificationEventArgs e)
		{
			this._output.WriteLine(e.Message);
		}
	}
}