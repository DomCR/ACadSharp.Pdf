using ACadSharp.Entities;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests.Integration
{
	public class Stage02TextIntegrationTests : IntegrationTestBase
	{
		private const string Fixture = "stage02_text.dxf";

		public Stage02TextIntegrationTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void LoadFixture_HasTextEntities()
		{
			CadDocument doc = this.LoadFixture(Fixture);

			int textCount = this.CountEntities<TextEntity>(doc);
			int mtextCount = this.CountEntities<MText>(doc);

			Assert.True(textCount >= 1, $"Expected at least 1 TextEntity, found {textCount}");
			Assert.True(mtextCount >= 1, $"Expected at least 1 MText, found {mtextCount}");
		}

		[Fact]
		public void ExportLegacy_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportLegacy(doc, "stage02");
			this.AssertValidPdf(pdf);
		}

		[Fact]
		public void ExportSceneGraph_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportSceneGraph(doc, "stage02");
			this.AssertValidPdf(pdf);
		}
	}
}
