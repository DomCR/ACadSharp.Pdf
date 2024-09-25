using System.Collections.Generic;

namespace ACadSharp.Pdf
{
	public class PdfExporterConfiguration
	{
		public static readonly IReadOnlyDictionary<LineWeightType, double> LineWeightDefaultValues =
		new Dictionary<LineWeightType, double>()
		{
			{ LineWeightType.Default, 0 },
			{ LineWeightType.W0, 0.001 },
			{ LineWeightType.W5, 0.05 },
		};

		public CadDocument ReferenceDocument { get; set; }

		public Dictionary<LineWeightType, double> LineWeightValues { get; set; } = new();

		public PdfExporterConfiguration()
		{
		}
	}
}
