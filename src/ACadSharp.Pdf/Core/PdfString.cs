namespace ACadSharp.Pdf.Core
{
	public class PdfString : PdfItem
	{
		private string _value;

		public PdfString(string value)
		{
			this._value = value;
		}

		public override string GetPdfForm(PdfConfiguration configuration)
		{
			return $"({this._value})";
		}
	}
}
