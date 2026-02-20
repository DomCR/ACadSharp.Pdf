using ACadSharp.Pdf.Core.Render.Flattening;
using ACadSharp.Pdf.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ACadSharp.Pdf.Core.Render.Pdf
{
	internal sealed class PdfRenderBackend
	{
		private readonly PdfConfiguration _configuration;

		public PdfRenderBackend(PdfConfiguration configuration)
		{
			this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		}

		public string Serialize(IReadOnlyList<FlatDrawCommand> commands)
		{
			if (commands == null || commands.Count == 0)
			{
				return string.Empty;
			}

			StringBuilder sb = new StringBuilder();
			foreach (var cmd in commands)
			{
				switch (cmd)
				{
					case FlatBeginClipCommand beginClip:
						sb.AppendLine(PdfKey.StackStart);
						appendPath(sb, beginClip.ClipSegmentsPdfPt);
						sb.AppendLine($"{PdfKey.ClippingPath} n");
						break;
					case FlatEndClipCommand:
						sb.AppendLine(PdfKey.StackEnd);
						break;
					case FlatPathCommand path:
						serializePath(sb, path);
						break;
					case FlatTextCommand text:
						serializeText(sb, text);
						break;
					case FlatImageCommand image:
						serializeImage(sb, image);
						break;
				}
			}

			return sb.ToString();
		}

		private void serializePath(StringBuilder sb, FlatPathCommand cmd)
		{
			if (cmd == null || cmd.SegmentsPdfPt == null || cmd.SegmentsPdfPt.Count == 0)
			{
				return;
			}

			bool hasStroke = cmd.Stroke != null;
			bool hasFill = cmd.Fill != null;

			if (hasStroke)
			{
				sb.AppendLine($"{toPdf(cmd.Stroke.WidthPt)} {PdfKey.LineWidth}");
				sb.AppendLine(cmd.Stroke.Color.ToPdfString());
				appendDash(sb, cmd.Stroke);
			}
			else
			{
				sb.AppendLine("[] 0 d");
			}

			if (hasFill)
			{
				sb.AppendLine(fillColor(cmd.Fill.Color));
			}

			appendPath(sb, cmd.SegmentsPdfPt);

			if (hasStroke && hasFill)
			{
				// For Stage 00, use nonzero winding rule.
				sb.AppendLine("B");
			}
			else if (hasFill)
			{
				sb.AppendLine("F");
			}
			else if (hasStroke)
			{
				sb.AppendLine(PdfKey.Stroke);
			}
		}

		private void serializeText(StringBuilder sb, FlatTextCommand cmd)
		{
			sb.AppendLine(PdfKey.BasicTextStart);

			// Font resource setup is currently simplistic; PdfPen also uses /F1.
			sb.Append("/F1 ");
			sb.Append(toPdf(cmd.FontSizePt));
			sb.Append(' ');
			sb.AppendLine(PdfKey.TypeFont);

			sb.AppendLine($"{toPdf(cmd.A)} {toPdf(cmd.B)} {toPdf(cmd.C)} {toPdf(cmd.D)} {toPdf(cmd.AnchorPdfPt.X)} {toPdf(cmd.AnchorPdfPt.Y)} {PdfKey.TextMatrix}");

			sb.AppendLine(strokeColor(cmd.Color));
			sb.AppendLine(fillColor(cmd.Color));
			sb.AppendLine($"({escapePdfString(cmd.Text)}) {PdfKey.TextString}");
			sb.AppendLine(PdfKey.BasicTextEnd);
		}

		private void serializeImage(StringBuilder sb, FlatImageCommand cmd)
		{
			if (cmd == null || cmd.Rgb24Data == null || cmd.Rgb24Data.Length == 0)
			{
				return;
			}

			if (cmd.SourceWidthPixels <= 0 || cmd.SourceHeightPixels <= 0)
			{
				return;
			}

			if (cmd.DisplayWidth <= 0 || cmd.DisplayHeight <= 0)
			{
				return;
			}

			sb.AppendLine(PdfKey.StackStart);
			sb.AppendLine($"{toPdf(cmd.A)} {toPdf(cmd.B)} {toPdf(cmd.C)} {toPdf(cmd.D)} {toPdf(cmd.E)} {toPdf(cmd.F)} {PdfKey.CurrentMatrix}");
			sb.AppendLine($"{toPdf(cmd.DisplayWidth)} 0 0 {toPdf(cmd.DisplayHeight)} 0 0 {PdfKey.CurrentMatrix}");

			sb.AppendLine("BI");
			sb.AppendLine($"/W {cmd.SourceWidthPixels}");
			sb.AppendLine($"/H {cmd.SourceHeightPixels}");
			sb.AppendLine("/BPC 8");
			sb.AppendLine("/CS /RGB");
			sb.AppendLine("/F /ASCIIHexDecode");
			sb.AppendLine("ID");
			appendAsciiHexData(sb, cmd.Rgb24Data);
			sb.AppendLine(">");
			sb.AppendLine("EI");
			sb.AppendLine(PdfKey.StackEnd);
		}

		private void appendPath(StringBuilder sb, IReadOnlyList<PathSegment> segments)
		{
			foreach (var seg in segments)
			{
				switch (seg)
				{
					case MoveTo m:
						sb.AppendLine($"{toPdf(m.Point.X)} {toPdf(m.Point.Y)} {PdfKey.BeginPath}");
						break;
					case LineTo l:
						sb.AppendLine($"{toPdf(l.Point.X)} {toPdf(l.Point.Y)} {PdfKey.Line}");
						break;
					case CubicTo c:
						sb.AppendLine($"{toPdf(c.C1.X)} {toPdf(c.C1.Y)} {toPdf(c.C2.X)} {toPdf(c.C2.Y)} {toPdf(c.End.X)} {toPdf(c.End.Y)} {PdfKey.Arc}");
						break;
					case ClosePath:
						sb.AppendLine("h");
						break;
				}
			}
		}

		private void appendDash(StringBuilder sb, StrokeStyle stroke)
		{
			if (stroke == null)
			{
				sb.AppendLine("[] 0 d");
				return;
			}

			if (stroke.DashArrayPt == null || stroke.DashArrayPt.Count == 0)
			{
				sb.AppendLine("[] 0 d");
				return;
			}

			var arr = stroke.DashArrayPt.Where(d => d > 0).Select(toPdf).ToArray();
			if (arr.Length == 0)
			{
				sb.AppendLine("[] 0 d");
				return;
			}

			sb.Append('[');
			sb.Append(string.Join(" ", arr));
			sb.AppendLine($"] {toPdf(stroke.DashOffsetPt)} d");
		}

		private static string fillColor(ACadSharp.Color color)
		{
			return $"{color.R / 255d} {color.G / 255d} {color.B / 255d} rg";
		}

		private static string strokeColor(ACadSharp.Color color)
		{
			return $"{color.R / 255d} {color.G / 255d} {color.B / 255d} RG";
		}

		private static string escapePdfString(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}

			StringBuilder escaped = new StringBuilder(value.Length);
			foreach (char c in value)
			{
				switch (c)
				{
					case '\\':
						escaped.Append("\\\\");
						break;
					case '(':
						escaped.Append("\\(");
						break;
					case ')':
						escaped.Append("\\)");
						break;
					case '\n':
						escaped.Append("\\n");
						break;
					case '\r':
						escaped.Append("\\r");
						break;
					case '\t':
						escaped.Append("\\t");
						break;
					case '\b':
						escaped.Append("\\b");
						break;
					case '\f':
						escaped.Append("\\f");
						break;
					default:
						escaped.Append(c);
						break;
				}
			}

			return escaped.ToString();
		}

		private static void appendAsciiHexData(StringBuilder sb, byte[] data)
		{
			if (data == null || data.Length == 0)
			{
				return;
			}

			const int lineChars = 128;
			const string hex = "0123456789ABCDEF";

			int chars = 0;
			for (int i = 0; i < data.Length; i++)
			{
				byte value = data[i];
				sb.Append(hex[value >> 4]);
				sb.Append(hex[value & 0x0F]);
				chars += 2;

				if (chars >= lineChars)
				{
					sb.AppendLine();
					chars = 0;
				}
			}

			if (chars != 0)
			{
				sb.AppendLine();
			}
		}

		private string toPdf(double value)
		{
			return value.ToString(this._configuration.DecimalFormat, CultureInfo.InvariantCulture);
		}
	}
}
