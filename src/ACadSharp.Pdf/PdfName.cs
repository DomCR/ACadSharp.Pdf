namespace ACadSharp.Pdf
{
	public class PdfName : PdfItem
	{
		private string _value;

		public PdfName(string type)
		{
			this._value = type;
		}

		public override string GetStringForm()
		{
			return _value;
		}
	}
}
