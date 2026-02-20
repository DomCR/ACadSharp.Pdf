using ACadSharp.Entities;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests.Integration
{
	public class Stage07MLineIntegrationTests : IntegrationTestBase
	{
		private const string Fixture = "stage07_mline.dxf";

		public Stage07MLineIntegrationTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void LoadFixture_HasMLineEntities()
		{
			CadDocument doc = this.LoadFixture(Fixture);

			int count = this.CountEntities<MLine>(doc);
			Assert.True(count >= 2, $"Expected at least 2 MLine entities, found {count}");
		}

		[Fact]
		public void ExportLegacy_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportLegacy(doc, "stage07");
			this.AssertValidPdf(pdf);
		}

		[Fact]
		public void ExportSceneGraph_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportSceneGraph(doc, "stage07");
			this.AssertValidPdf(pdf);
		}
	}
}
