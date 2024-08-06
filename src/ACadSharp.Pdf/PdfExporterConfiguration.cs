using System.Collections.Generic;

namespace ACadSharp.Pdf
{
	public class PdfExporterConfiguration
	{
		public CadDocument ReferenceDocument { get; set; }

		public Dictionary<LineweightType, double> LineweightValues { get; set; } = new()
		{
			{ LineweightType.ByDIPs, 0 }
		};
	}
}
