using ACadSharp.Entities;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests.Integration
{
	public class Stage09ToleranceIntegrationTests : IntegrationTestBase
	{
		private const string Fixture = "stage09_tolerance.dxf";

		public Stage09ToleranceIntegrationTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void LoadFixture_HasToleranceEntity()
		{
			CadDocument doc = this.LoadFixture(Fixture);

			int count = this.CountEntities<Tolerance>(doc);
			Assert.True(count >= 1, $"Expected at least 1 Tolerance entity, found {count}");
		}

		[Fact]
		public void ExportLegacy_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportLegacy(doc, "stage09");
			this.AssertValidPdf(pdf);
		}

		[Fact]
		public void ExportSceneGraph_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportSceneGraph(doc, "stage09");
			this.AssertValidPdf(pdf);
			string text = readAscii(pdf);
			Assert.Contains("(0.05) Tj", text);
			Assert.Contains("(A) Tj", text);
			Assert.Contains("(B) Tj", text);
		}

		private static string readAscii(FileInfo file)
		{
			byte[] bytes = File.ReadAllBytes(file.FullName);
			return Encoding.ASCII.GetString(bytes);
		}
	}
}
