using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using System.Globalization;
using System.IO;
using System.Text;
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

		[Fact]
		public void InvariantDecimalSeparatorTest()
		{
			CultureInfo previousCulture = CultureInfo.CurrentCulture;

			try
			{
				//A culture such as fr-FR uses the comma as decimal separator,
				//which would produce an invalid pdf stream if numbers were not
				//formatted using the invariant culture.
				CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

				CadDocument doc = new CadDocument();
				doc.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(12.5, 7.25, 0)));

				string content;
				using (MemoryStream stream = new MemoryStream())
				{
					PdfExporter exporter = new PdfExporter(stream);
					exporter.AddModelSpace(doc);
					exporter.Close();

					content = Encoding.ASCII.GetString(stream.ToArray());
				}

				//No number must use the comma as decimal separator.
				Assert.DoesNotMatch(@"\d,\d", content);
				//The decimal point must be used (e.g. MediaBox, coordinates).
				Assert.Contains(".", content);
			}
			finally
			{
				CultureInfo.CurrentCulture = previousCulture;
			}
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