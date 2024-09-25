using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf.Extensions;
using CSMath;
using System.Text;

namespace ACadSharp.Pdf
{
	public class PdfContent : PdfDictionary
	{
		public XY Translation { get; set; } = XY.Zero;

		public PdfPage Owner { get; }

		public Layout Layout { get { return this.Owner.Layout; } }

		private readonly StringBuilder _sb = new();

		public PdfContent(PdfPage owner)
		{
			this.Owner = owner;

			this.Items.Add("/Length", new PdfReference<long>(() => this._sb.Length));
		}

		public override string GetPdfForm(PdfExporterConfiguration configuration)
		{
			string drawing = this.createDrawingString();

			StringBuilder str = new StringBuilder();
			str.Append(this.getStartObj());
			str.Append(this.getBody(configuration));
			str.Append(drawing);
			str.Append(this.getEndObj());

			return str.ToString();
		}

		private string createDrawingString()
		{
			this._sb.Clear();

			this._sb.AppendLine(PdfKey.StreamStart);

			this.writeStackStart();

			this._sb.AppendLine($"1 {PdfKey.LineWidth}");

			foreach (Entity e in this.Owner.Entities)
			{
				this.drawEntity(e);
			}

			this.writeStackEnd();

			this._sb.AppendLine(PdfKey.StreamEnd);

			return this._sb.ToString();
		}

		private void writeStackStart()
		{
			//Stack
			this._sb.AppendLine(PdfKey.StackStart);
			//Bottom left is 0,0

			//translation
			//1 0 0 1 tx ty cm
			//scaling
			//sx 0 0 xy 0 0 cm
			//rotation - clockwise
			//(cos q) (sin q) (-sin q) (cos q) 0 0 cm

			this.getTotalTranslation(out double xt, out double yt);
			this._sb.AppendLine($"1 0 0 1 {xt} {yt} cm");
			this._sb.AppendLine(PdfKey.StackStart);
		}

		private void writeStackEnd()
		{
			//Reset stack
			this._sb.AppendLine("Q");
			this._sb.AppendLine("Q");
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

		private void drawEntity(Entity entity)
		{
			this.applyStyle(entity);

			switch (entity)
			{
				case Line line:
					this.drawLine(line);
					break;
				default:
					break;
			}
		}

		private void applyStyle(Entity entity)
		{
			switch (entity.LineWeight)
			{
				case LineWeightType.ByDIPs:
					break;
				case LineWeightType.Default:
					break;
				case LineWeightType.ByBlock:
					break;
				case LineWeightType.ByLayer:
					break;
				default:
					break;
			}

			Color color = default;
			if (entity.Color.IsByLayer)
			{
				color = entity.Layer.Color;
			}
			else
			{
				color = entity.Color;
			}

			if (color.Index == 7)
			{
				color = new Color(0, 0, 0);
			}

			this._sb.AppendLine(color.ToPdfString());
		}

		private void drawLine(Line line)
		{
			//set transform
			this._sb.AppendLine($"{this.toPdfDouble(line.StartPoint.X)} {this.toPdfDouble(line.StartPoint.Y)} m");
			_sb.AppendLine($"{this.toPdfDouble(line.EndPoint.X)} {this.toPdfDouble(line.EndPoint.Y)} l");
			_sb.AppendLine("S");
		}

		private string toPdfDouble(double value)
		{
			return value.ToPdfUnit(this.Layout.PaperUnits).ToString("0.####");
		}
	}
}
