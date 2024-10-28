namespace ACadSharp.Pdf.Core
{
	public class PdfName : PdfItem
	{
		private string _value;

		public PdfName(string type)
		{
			this._value = type;
		}

		public override string GetPdfForm(PdfConfiguration configuration)
		{
			return _value;
		}
	}
}
