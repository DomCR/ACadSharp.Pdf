using ACadSharp.Entities;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests.Integration
{
	public class Stage08RayXLineIntegrationTests : IntegrationTestBase
	{
		private const string Fixture = "stage08_ray_xline.dxf";

		public Stage08RayXLineIntegrationTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void LoadFixture_HasRayAndXLineEntities()
		{
			CadDocument doc = this.LoadFixture(Fixture);

			int rayCount = this.CountEntities<Ray>(doc);
			int xlineCount = this.CountEntities<XLine>(doc);

			Assert.True(rayCount >= 3, $"Expected at least 3 Ray entities, found {rayCount}");
			Assert.True(xlineCount >= 3, $"Expected at least 3 XLine entities, found {xlineCount}");
		}

		[Fact]
		public void ExportLegacy_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportLegacy(doc, "stage08");
			this.AssertValidPdf(pdf);
			Assert.Contains("RAY |", readAscii(pdf));
			Assert.Contains("XLINE |", readAscii(pdf));
		}

		[Fact]
		public void ExportSceneGraph_ProducesValidPdf()
		{
			CadDocument doc = this.LoadFixture(Fixture);
			FileInfo pdf = this.ExportSceneGraph(doc, "stage08");
			this.AssertValidPdf(pdf);
			string text = readAscii(pdf);
			Assert.Contains(" m", text);
			Assert.Contains(" l", text);
		}

		private static string readAscii(FileInfo file)
		{
			byte[] bytes = File.ReadAllBytes(file.FullName);
			return Encoding.ASCII.GetString(bytes);
		}
	}
}
