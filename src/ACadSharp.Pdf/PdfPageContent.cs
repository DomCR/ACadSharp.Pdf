using ACadSharp.Entities;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ACadSharp.Pdf
{
	public class PdfPageContent : PdfDictionary
	{
		public Stream Stream { get; set; }

		public PdfPage Owner { get; }

		public PdfPageContent(PdfPage owner)
		{
			this.Owner = owner;

			this.Stream = new MemoryStream();
			this.Items.Add("/Length", new PdfReference<long>(() => this.Stream.Length));
		}

		public override string GetStringForm()
		{
			string drawing = this.getDrawingString();

			StringBuilder str = new StringBuilder();
			str.Append(getStartObj());
			str.Append(this.getBody());
			str.Append(drawing);
			str.Append(this.getEndObj());

			return str.ToString();
		}

		private string getDrawingString()
		{
			StringBuilder drawing = new StringBuilder();

			drawing.AppendLine(PdfKey.StreamStart);

			//Stack
			drawing.AppendLine("q");
			//Bottom left is 0,0

			//translation
			//1 0 0 1 tx ty cm
			//scaling
			//sx 0 0 xy 0 0 cm
			//rotation - clockwise
			//(cos q) (sin q) (-sin q) (cos q) 0 0 cm
			drawing.AppendLine("1 0 0 1 0 0 cm");
			drawing.AppendLine("q");

			drawing.AppendLine($"1 {PdfKey.LineWidth}");

			drawReferenceCross(drawing);

			foreach (Entities.Line item in this.Owner.Entities.OfType<Line>())
			{
				this.drawLine(drawing, item);
			}

			//Reset stack
			drawing.AppendLine("Q");
			drawing.AppendLine("Q");

			drawing.AppendLine(PdfKey.StreamEnd);

			Stream.Write(Encoding.Default.GetBytes(drawing.ToString()));

			return drawing.ToString();
		}

		private void drawReferenceCross(StringBuilder str)
		{
			str.AppendLine("0 0 m");
			str.AppendLine("100 100 l");
			str.AppendLine("S");
		}

		private void drawLine(StringBuilder str, Line item)
		{
			//set transform
		}
	}
}
