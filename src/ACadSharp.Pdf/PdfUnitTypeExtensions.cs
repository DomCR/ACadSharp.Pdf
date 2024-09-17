using System.ComponentModel;

namespace ACadSharp.Pdf
{
	public static class PdfUnitTypeExtensions
	{
		public static double ToUnit(this PdfUnitType type, double value)
		{
			return type switch
			{
				PdfUnitType.Point => value,
				PdfUnitType.Inch => value * 72,
				PdfUnitType.Millimeter => value * (72 / 25.4),
				PdfUnitType.Centimeter => value * (72 / 2.54),
				PdfUnitType.Presentation => value * (72d / 96),
				_ => throw new InvalidEnumArgumentException(nameof(type), (int)type, typeof(PdfUnitType))
			};
		}
	}
}
