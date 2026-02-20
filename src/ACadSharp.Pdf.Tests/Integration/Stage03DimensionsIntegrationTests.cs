using ACadSharp.Entities;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests.Integration
{
	public class Stage03DimensionsIntegrationTests : IntegrationTestBase
	{
		private const string Fixture = "stage03_dimensions.dxf";

		public Stage03DimensionsIntegrationTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void LoadFixture_HasAllDimensionSubclasses()
		{
			CadDocument doc = this.LoadFixture(Fixture);

			Assert.True(this.CountEntities<DimensionLinear>(doc) >= 1,
				"Expected at least 1 DimensionLinear");
			Assert.True(this.CountEntities<DimensionAligned>(doc) >= 1,
				"Expected at least 1 DimensionAligned");
			Assert.True(this.CountEntities<DimensionAngular3Pt>(doc) >= 1,
				"Expected at least 1 DimensionAngular3Pt");
			Assert.True(this.CountEntities<DimensionRadius>(doc) >= 1,
				"Expected at least 1 DimensionRadius");
			Assert.True(this.CountEntities<DimensionDiameter>(doc) >= 1,
				"Expected at least 1 DimensionDiameter");
			Assert.True(this.CountEntities<DimensionOrdinate>(doc) >= 1,
				"Expected at least 1 DimensionOrdinate");
		}

		[Fact]
		public void ExportLegacy_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportLegacy(doc, "stage03");
			this.AssertValidPdf(pdf);
		}

		[Fact]
		public void ExportSceneGraph_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportSceneGraph(doc, "stage03");
			this.AssertValidPdf(pdf);
		}
	}
}
