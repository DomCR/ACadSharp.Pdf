using System.ComponentModel;
using ACadSharp.Objects;
using ACadSharp.Pdf.Core;

namespace ACadSharp.Pdf.Extensions
{
	public static class PlotPaperUnitsExtensions
	{
		public static PdfUnitType ToPdfUnit(this PlotPaperUnits unit)
		{
			switch (unit)
			{
				case PlotPaperUnits.Inches:
					return PdfUnitType.Inch;
				case PlotPaperUnits.Millimeters:
					return PdfUnitType.Millimeter;
				case PlotPaperUnits.Pixels:
					return PdfUnitType.Point;
				default:
					throw new InvalidEnumArgumentException(nameof(unit), (int)unit, typeof(PdfUnitType));
			}
		}
	}

	public static class PdfUnitTypeExtensions
	{
		public static double Transform(this PdfUnitType type, double value)
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
