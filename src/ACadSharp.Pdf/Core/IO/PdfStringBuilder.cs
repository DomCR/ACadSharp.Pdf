using ACadSharp.Pdf.Extensions;
using System.Globalization;
using System.Text;

namespace ACadSharp.Pdf.Core.IO
{
	internal static class PdfStringBuilderExtensions
	{
		public static void Append(this StringBuilder sb, params double[] arr)
		{
			foreach (double value in arr)
			{
				sb.Append(value.ToString(CultureInfo.InvariantCulture));
			}
		}

		public static void Append(this StringBuilder sb, PdfUnitType unit, params double[] arr)
		{
			foreach (double value in arr)
			{
				sb.AppendInvariant(unit.Transform(value));
			}
		}

		public static void AppendInvariant(this StringBuilder sb, double value)
		{
			sb.Append(value.ToString(CultureInfo.InvariantCulture));
		}

		public static void Append(this StringBuilder sb, double value, PdfUnitType unit)
		{
			sb.AppendInvariant(unit.Transform(value));
		}
	}
}
