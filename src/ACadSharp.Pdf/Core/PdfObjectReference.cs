﻿namespace ACadSharp.Pdf.Core
{
	public class PdfObjectReference : PdfItem
	{
		public PdfObject Value { get; set; }

		public PdfObjectReference(PdfObject value)
		{
			this.Value = value;
		}

		public override string GetPdfForm(PdfConfiguration configuration)
		{
			return $"{this.Value.Id} 0 R";
		}
	}
}
