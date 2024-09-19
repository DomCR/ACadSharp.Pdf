using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf.Extensions;
using CSMath;
using System.Linq;
using System.Text;

namespace ACadSharp.Pdf
{
	public class PdfContent : PdfDictionary
	{
		public XY Translation { get; set; } = XY.Zero;

		public StringBuilder ContentString { get; }

		public PdfPage Owner { get; }

		public Layout Layout { get { return this.Owner.Layout; } }

		public PdfContent(PdfPage owner)
		{
			this.Owner = owner;

			this.ContentString = new StringBuilder();
			this.Items.Add("/Length", new PdfReference<long>(() => this.ContentString.Length));
		}

		public override string GetStringForm()
		{
			string drawing = this.createDrawingString();

			StringBuilder str = new StringBuilder();
			str.Append(this.getStartObj());
			str.Append(this.getBody());
			str.Append(drawing);
			str.Append(this.getEndObj());

			return str.ToString();
		}

		private string createDrawingString()
		{
			this.ContentString.Clear();

			this.ContentString.AppendLine(PdfKey.StreamStart);

			this.drawReferenceCross(this.ContentString);

			//Stack
			this.ContentString.AppendLine("q");
			//Bottom left is 0,0

			//translation
			//1 0 0 1 tx ty cm
			//scaling
			//sx 0 0 xy 0 0 cm
			//rotation - clockwise
			//(cos q) (sin q) (-sin q) (cos q) 0 0 cm

			this.getTotalTranslation(out double xt, out double yt);
			this.ContentString.AppendLine($"1 0 0 1 {xt} {yt} cm");
			this.ContentString.AppendLine("q");

			this.ContentString.AppendLine($"1 {PdfKey.LineWidth}");

			foreach (Entities.Line item in this.Owner.Entities.OfType<Line>())
			{
				this.drawLine(item);
			}

			//Reset stack
			this.ContentString.AppendLine("Q");
			this.ContentString.AppendLine("Q");

			this.ContentString.AppendLine(PdfKey.StreamEnd);

			return this.ContentString.ToString();
		}

		private void getTotalTranslation(out double xt, out double yt)
		{
			xt = PdfUnitType.Millimeter.Transform(this.Layout.UnprintableMargin.Left);
			yt = PdfUnitType.Millimeter.Transform(this.Layout.UnprintableMargin.Bottom);

			xt += PdfUnitType.Millimeter.Transform(this.Translation.X);
			yt += PdfUnitType.Millimeter.Transform(this.Translation.Y);
		}

		private void drawReferenceCross(StringBuilder str)
		{
			//Reference method
			str.AppendLine("0.5 w");
			str.AppendLine("1 0 0 RG");

			str.AppendLine("0 0 m");
			str.AppendLine($"{this.Owner.Width} {this.Owner.Height} l");
			str.AppendLine("S");

			str.AppendLine($"0 {this.Owner.Height} m");
			str.AppendLine($"{this.Owner.Width} 0 l");
			str.AppendLine("S");
		}

		private void applyStyle(Entity entity)
		{
			switch (entity.LineWeight)
			{
				case LineweightType.ByDIPs:
					break;
				case LineweightType.Default:
					break;
				case LineweightType.ByBlock:
					break;
				case LineweightType.ByLayer:
					break;
				default:
					break;
			}


		}

		private void drawLine(Line line)
		{
			this.applyStyle(line);

			//set transform
			this.ContentString.AppendLine($"{this.toPdfDouble(line.StartPoint.X)} {this.toPdfDouble(line.StartPoint.Y)} m");
			ContentString.AppendLine($"{this.toPdfDouble(line.EndPoint.X)} {this.toPdfDouble(line.EndPoint.Y)} l");
			ContentString.AppendLine("S");
		}

		private string toPdfDouble(double value)
		{
			return value.ToPdfUnit(this.Layout.PaperUnits).ToString("0.####");
		}
	}
}
