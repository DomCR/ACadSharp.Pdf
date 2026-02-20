using ACadSharp.Entities;
using ACadSharp.IO;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Pdf.Tests.Integration
{
	public abstract class IntegrationTestBase
	{
		protected static readonly string FixturesFolder =
			Path.Combine(TestVariables.SamplesFolder, "fixtures");

		protected static readonly string OutputFolder =
			Path.Combine(TestVariables.OutputSamplesFolder, "integration");

		protected readonly ITestOutputHelper _output;

		protected IntegrationTestBase(ITestOutputHelper output)
		{
			this._output = output;

			if (!Directory.Exists(OutputFolder))
			{
				Directory.CreateDirectory(OutputFolder);
			}
		}

		protected CadDocument LoadFixture(string filename)
		{
			string path = Path.Combine(FixturesFolder, filename);
			Assert.True(File.Exists(path), $"Fixture not found: {path}");
			return DxfReader.Read(path);
		}

		protected FileInfo ExportLegacy(CadDocument doc, string name, string basePath = null)
		{
			string path = Path.Combine(OutputFolder, $"{name}_legacy.pdf");
			PdfExporter exporter = new PdfExporter(path);
			exporter.Configuration.UseSceneGraph = false;
			if (!string.IsNullOrWhiteSpace(basePath))
			{
				exporter.Configuration.BasePath = basePath;
			}
			exporter.Configuration.OnNotification += this.onNotification;
			exporter.AddModelSpace(doc);
			exporter.Close();
			return new FileInfo(path);
		}

		protected FileInfo ExportSceneGraph(CadDocument doc, string name, string basePath = null)
		{
			string path = Path.Combine(OutputFolder, $"{name}_scenegraph.pdf");
			PdfExporter exporter = new PdfExporter(path);
			exporter.Configuration.UseSceneGraph = true;
			if (!string.IsNullOrWhiteSpace(basePath))
			{
				exporter.Configuration.BasePath = basePath;
			}
			exporter.Configuration.OnNotification += this.onNotification;
			exporter.AddModelSpace(doc);
			exporter.Close();
			return new FileInfo(path);
		}

		protected void AssertValidPdf(FileInfo file)
		{
			Assert.True(file.Exists, $"PDF not created: {file.FullName}");
			Assert.True(file.Length > 0, $"PDF is empty: {file.FullName}");

			using (FileStream fs = file.OpenRead())
			{
				byte[] header = new byte[5];
				int bytesRead = fs.Read(header, 0, 5);
				Assert.Equal(5, bytesRead);
				string headerStr = System.Text.Encoding.ASCII.GetString(header);
				Assert.Equal("%PDF-", headerStr);
			}
		}

		protected int CountEntities<T>(CadDocument doc) where T : Entity
		{
			return doc.ModelSpace.Entities.OfType<T>().Count();
		}

		private void onNotification(object sender, NotificationEventArgs e)
		{
			this._output.WriteLine(e.Message);
		}
	}
}
