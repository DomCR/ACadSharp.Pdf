using ACadSharp.Entities;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests.Integration
{
	public class Stage06ImageUnderlayIntegrationTests : IntegrationTestBase
	{
		private const string Fixture = "stage06_image_underlay.dxf";

		public Stage06ImageUnderlayIntegrationTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void LoadFixture_HasImageAndUnderlay()
		{
			CadDocument doc = this.LoadFixture(Fixture);

			int imageCount = this.CountEntities<RasterImage>(doc);
			int underlayCount = this.CountEntities<PdfUnderlay>(doc);

			Assert.True(imageCount >= 1, $"Expected at least 1 RasterImage, found {imageCount}");
			Assert.True(underlayCount >= 1, $"Expected at least 1 PdfUnderlay, found {underlayCount}");
		}

		[Fact]
		public void ExportLegacy_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportLegacy(doc, "stage06", FixturesFolder);
			this.AssertValidPdf(pdf);
			AssertPdfContainsInlineImage(pdf);
		}

		[Fact]
		public void ExportSceneGraph_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportSceneGraph(doc, "stage06", FixturesFolder);
			this.AssertValidPdf(pdf);
			AssertPdfContainsInlineImage(pdf);
		}

		private static void AssertPdfContainsInlineImage(FileInfo pdf)
		{
			byte[] bytes = File.ReadAllBytes(pdf.FullName);
			string text = System.Text.Encoding.ASCII.GetString(bytes);
			Assert.Contains("BI", text);
			Assert.Contains("/ASCIIHexDecode", text);
		}
	}
}
