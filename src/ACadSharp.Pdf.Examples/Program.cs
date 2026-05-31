using ACadSharp.IO;
using System.IO;

namespace ACadSharp.Pdf.Examples
{
	internal class Program
	{
		const string _file = "../../../../../samples/export_sample.dwg";

		static void Main(string[] args)
		{
			CadDocument doc = DwgReader.Read(_file, NotificationHelper.LogConsoleNotification);

			string filename = Path.ChangeExtension(_file, ".pdf");

			PdfExporter exporter = new PdfExporter(filename);
			exporter.Configuration.OnNotification += NotificationHelper.LogConsoleNotification;

			//Add the model space as a page
			exporter.Add(doc.ModelSpace);

			//Save and close the pdf
			exporter.Close();
		}
	}
}