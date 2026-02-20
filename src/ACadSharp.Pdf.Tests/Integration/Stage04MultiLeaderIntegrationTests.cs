using ACadSharp.Entities;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests.Integration
{
	public class Stage04MultiLeaderIntegrationTests : IntegrationTestBase
	{
		private const string Fixture = "stage04_multileader.dxf";

		public Stage04MultiLeaderIntegrationTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void LoadFixture_HasMultiLeader()
		{
			CadDocument doc = this.LoadFixture(Fixture);

			int count = this.CountEntities<MultiLeader>(doc);
			Assert.True(count >= 1, $"Expected at least 1 MultiLeader, found {count}");
		}

		[Fact]
		public void ExportLegacy_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportLegacy(doc, "stage04");
			this.AssertValidPdf(pdf);
		}

		[Fact]
		public void ExportSceneGraph_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportSceneGraph(doc, "stage04");
			this.AssertValidPdf(pdf);
		}
	}
}
