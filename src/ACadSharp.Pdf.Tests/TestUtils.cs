using ACadSharp.IO;
using System.IO;

namespace ACadSharp.Pdf.Tests
{
	public static class TestUtils
	{
		public static CadDocument GetDocument()
		{
			return DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, "export_sample.dwg"));
		}
	}
}
