using ACadSharp.IO;
using Example;
using System.IO;

namespace ACadSharp.Pdf.Examples
{
	internal class Program
	{
		private const string _folder = "..\\\\..\\\\..\\\\..\\\\..\\\\samples";
		private const string _outFolder = "..\\\\..\\\\..\\\\..\\\\..\\\\samples\\\\out";

		static void Main(string[] args)
		{
			PdfSharpExamples.HelloWorld(Path.Combine(_outFolder, "PdfSharp", "helloworld.pdf"));
			PdfSharpExamples.Landscape(Path.Combine(_outFolder, "PdfSharp", "Landscape.pdf"));
			PdfSharpExamples.DrawDiagonal(Path.Combine(_outFolder, "PdfSharp", "Diagonal.pdf"));

			return;

			CadDocument doc = DwgReader.Read(Path.Combine(_folder, "export_sample.dwg"), NotificationHelper.LogConsoleNotification);

			string filename = Path.Combine(_folder, "export_sample_output.pdf");

			PdfExporter exporter = new PdfExporter(filename);

			exporter.OnNotification += NotificationHelper.LogConsoleNotification;

			exporter.Add(doc.ModelSpace);

			exporter.Close();
		}
	}
}