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
			StringBuilder drawing = new StringBuilder();
			drawing.AppendLine("stream");
			drawing.AppendLine("q");
			//Bottom left is 0,0
			drawing.AppendLine("1 0 0 1 0 0 cm");
			drawing.AppendLine("q");

			drawing.AppendLine("1 w");

			drawReferenceCross(drawing);

			foreach (Entities.Line item in this.Owner.Entities.OfType<Line>())
			{
				this.drawLine(drawing, item);
			}

			drawing.AppendLine("Q");
			drawing.AppendLine("Q");

			drawing.AppendLine("endstream");

			Stream.Write(Encoding.Default.GetBytes(drawing.ToString()));

			StringBuilder str = new StringBuilder();
			str.AppendLine($"{this.Id} 0 obj");
			str.Append(this.getDictinaryForm());
			str.Append(drawing);
			str.AppendLine("endobj");

			return str.ToString();
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
