using ACadSharp.Pdf.Extensions;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ACadSharp.Pdf.Core.IO
{
	internal static class StringBuilderExtensions
	{
		public static void Append(this StringBuilder sb, params double[] arr)
		{
			sb.AppendJoin(" ", arr.Select(d => d.ToString(CultureInfo.InvariantCulture)));
		}

		public static void Append(this StringBuilder sb, PdfUnitType unit, params double[] arr)
		{
			sb.AppendJoin(" ", arr.Select(d => unit.Transform(d).ToString(CultureInfo.InvariantCulture)));
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
