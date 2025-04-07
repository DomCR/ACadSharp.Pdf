using ACadSharp.IO;
using ACadSharp.Pdf.Extensions;
using System.IO;
using Xunit;

namespace ACadSharp.Pdf.Tests.Extensions
{
	public class CadDocumentExtensionsTests
	{
		[Fact]
		public void CreatePreviewTest()
		{
			string filename = Path.Combine(TestVariables.OutputSamplesFolder, "preview.dwg");
			CadDocument doc = TestUtils.GetDocument();
			using (DwgWriter writer = new DwgWriter(filename, doc))
			{
				writer.Preview = doc.CreatePreview();
				writer.Write();
			}
		}
	}
}
