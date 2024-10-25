using ACadSharp.Objects;
using ACadSharp.Pdf.Core;
using CSMath;
using System;

namespace ACadSharp.Pdf.Extensions
{
	public static class XYZExtensions
	{
		public static string ToPdfArray(this XYZ xyz)
		{
			throw new NotImplementedException();
		}
	}

	public static class DoubleExtensions
	{
		public static double ToPdfUnit(this double value, PlotPaperUnits unit)
		{
			switch (unit)
			{
				case PlotPaperUnits.Inches:
					return PdfUnitType.Inch.Transform(value);
				case PlotPaperUnits.Milimeters:
					return PdfUnitType.Millimeter.Transform(value);
				case PlotPaperUnits.Pixels:
				default:
					return PdfUnitType.Point.Transform(value);
			}
		}

		public static double ToPdfUnit(this double value, PdfUnitType unit)
		{
			return unit.Transform(value);
		}
	}

	public static class ColorExtensions
	{
		public static string ToPdfString(this Color color)
		{
			return $"{color.R / 255d} {color.G / 255d} {color.B / 255d} RG";
		}
	}
}