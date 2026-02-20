using ACadSharp.Entities;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests.Integration
{
	public class Stage01InsertsIntegrationTests : IntegrationTestBase
	{
		private const string Fixture = "stage01_inserts.dxf";

		public Stage01InsertsIntegrationTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void LoadFixture_HasExpectedInserts()
		{
			CadDocument doc = this.LoadFixture(Fixture);

			int insertCount = this.CountEntities<Insert>(doc);
			Assert.True(insertCount >= 5, $"Expected at least 5 Insert entities, found {insertCount}");
		}

		[Fact]
		public void LoadFixture_HasNestedBlocks()
		{
			CadDocument doc = this.LoadFixture(Fixture);

			Assert.True(doc.BlockRecords.Contains("OUTER"), "Expected OUTER block");
			Assert.True(doc.BlockRecords.Contains("INNER"), "Expected INNER block");

			var outerBlock = doc.BlockRecords["OUTER"];
			bool hasNestedInsert = outerBlock.Entities.OfType<Insert>().Any();
			Assert.True(hasNestedInsert, "OUTER block should contain a nested INSERT");
		}

		[Fact]
		public void ExportLegacy_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportLegacy(doc, "stage01");
			this.AssertValidPdf(pdf);
		}

		[Fact]
		public void ExportSceneGraph_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportSceneGraph(doc, "stage01");
			this.AssertValidPdf(pdf);
		}
	}
}
