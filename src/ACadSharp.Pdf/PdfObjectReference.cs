namespace ACadSharp.Pdf
{
	public class PdfObjectReference : PdfItem
	{
		public PdfObject Value { get; set; }

		public PdfObjectReference(PdfObject value)
		{
			this.Value = value;
		}

		public override string GetPdfForm(PdfExporterConfiguration configuration)
		{
			return $"{this.Value.Id} 0 R";
		}
	}
}
