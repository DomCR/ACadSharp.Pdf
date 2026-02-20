using ACadSharp.Entities;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests.Integration
{
	public class Stage00PrimitivesIntegrationTests : IntegrationTestBase
	{
		private const string Fixture = "stage00_primitives.dxf";

		public Stage00PrimitivesIntegrationTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void LoadFixture_HasExpectedEntityCounts()
		{
			CadDocument doc = this.LoadFixture(Fixture);

			Assert.True(this.CountEntities<Line>(doc) >= 3, "Expected at least 3 Line entities");
			Assert.True(this.CountEntities<Arc>(doc) >= 2, "Expected at least 2 Arc entities");
			Assert.True(this.CountEntities<Circle>(doc) >= 2, "Expected at least 2 Circle entities");
			Assert.True(this.CountEntities<Ellipse>(doc) >= 2, "Expected at least 2 Ellipse entities");
			Assert.True(this.CountEntities<Point>(doc) >= 2, "Expected at least 2 Point entities");
		}

		[Fact]
		public void ExportLegacy_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportLegacy(doc, "stage00");
			this.AssertValidPdf(pdf);
		}

		[Fact]
		public void ExportSceneGraph_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportSceneGraph(doc, "stage00");
			this.AssertValidPdf(pdf);
		}
	}
}
