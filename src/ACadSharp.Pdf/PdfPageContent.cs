using System.IO;

namespace ACadSharp.Pdf
{
	public class PdfPageContent : PdfDictionary
	{
		public Stream Stream { get; set; }

		public PdfPageContent()
		{
			this.Stream = new MemoryStream();
			this.Items.Add("/Length", new PdfReference<long>(() => Stream.Length));
		}

		public override string GetStringForm()
		{
			return base.GetStringForm();
		}
	}
}
