using ACadSharp.Entities;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests.Integration
{
	public class Stage05HatchIntegrationTests : IntegrationTestBase
	{
		private const string Fixture = "stage05_hatch.dxf";

		public Stage05HatchIntegrationTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void LoadFixture_HasHatchEntities()
		{
			CadDocument doc = this.LoadFixture(Fixture);

			int count = this.CountEntities<Hatch>(doc);
			Assert.True(count >= 3, $"Expected at least 3 Hatch entities, found {count}");
		}

		[Fact(Skip = "Stage 05 (Hatch) rendering not yet implemented")]
		public void ExportLegacy_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportLegacy(doc, "stage05");
			this.AssertValidPdf(pdf);
		}

		// Stage 05 is implemented in the scene-graph pipeline (legacy pipeline may still be incomplete).
		[Fact]
		public void ExportSceneGraph_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportSceneGraph(doc, "stage05");
			this.AssertValidPdf(pdf);
		}
	}
}
