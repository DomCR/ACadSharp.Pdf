using System.Collections.Generic;
using System.Text;

namespace ACadSharp.Pdf
{
	public class PdfDictionary : PdfObject
	{
		public Dictionary<string, PdfItem> Items { get; set; } = new();

		public override string GetStringForm()
		{
			StringBuilder str = new StringBuilder();

			str.AppendLine($"{this.Id} 0 obj");

			str.Append(getDictinaryForm());

			str.AppendLine("endobj");

			return str.ToString();
		}

		protected string getDictinaryForm()
		{
			StringBuilder str = new StringBuilder();

			str.AppendLine("<<");
			foreach (var item in this.Items)
			{
				str.AppendLine($"{item.Key} {item.Value.GetStringForm()}");
			}
			str.AppendLine(">>");

			return str.ToString();
		}
	}
}
