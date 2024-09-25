namespace ACadSharp.Pdf
{
	public class PdfString : PdfItem
	{
		private string _value;

		public PdfString(string value)
		{
			this._value = value;
		}

		public override string GetPdfForm(PdfExporterConfiguration configuration)
		{
			return $"({this._value})";
		}
	}
}
