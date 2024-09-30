using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf.Core.IO;
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

		public override string GetPdfForm(PdfConfiguration configuration)
		{
			string drawing = this.createDrawingString(configuration);

			StringBuilder str = new StringBuilder();
			str.Append(this.getStartObj());
			str.Append(this.getBody(configuration));
			str.Append(drawing);
			str.Append(this.getEndObj());

			return str.ToString();
		}

		private string createDrawingString(PdfConfiguration configuration)
		{
			PdfPen pen = new PdfPen(configuration);
			pen.PaperUnits = Layout.PaperUnits;

			this._sb.Clear();

			this._sb.AppendLine(PdfKey.StreamStart);

			this.writeStackStart();

			this._sb.AppendLine($"1 {PdfKey.LineWidth}");

			foreach (Entity e in this.Owner.Entities)
			{
				pen.DrawEntity(e);
			}

			this._sb.Append(pen.ToString());

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
			this._sb.AppendLine($"1 0 0 1 {xt} {yt} {PdfKey.CurrentMatrix}");
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

		private string toPdfDouble(double value)
		{
			return value.ToPdfUnit(this.Layout.PaperUnits).ToString("0.####");
		}
	}
}
