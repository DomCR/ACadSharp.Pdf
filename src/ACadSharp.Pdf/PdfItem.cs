namespace ACadSharp.Pdf
{
	public abstract class PdfItem : IPdfItem
	{
		public virtual int Id { get; set; }

		public string Name { get; set; }

		public abstract string GetPdfForm(PdfConfiguration configuration);

		public virtual void SetId(ref int currNumber)
		{
			this.Id = currNumber;
			currNumber++;
		}
	}
}
