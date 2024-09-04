using ACadSharp.IO;
using System.IO;

namespace ACadSharp.Pdf.Examples
{
	internal class Program
	{
		private const string _folder = "..\\\\..\\\\..\\\\..\\\\..\\\\samples";

		static void Main(string[] args)
		{
			CadDocument doc = DwgReader.Read(Path.Combine(_folder, "export_sample.dwg"), NotificationHelper.LogConsoleNotification);

			string filename = Path.Combine(_folder, "export_sample_output.pdf");

			PdfExporter exporter = new PdfExporter(filename);

			exporter.OnNotification += NotificationHelper.LogConsoleNotification;

			exporter.Add(doc.ModelSpace);

			exporter.Close();
		}
	}
}