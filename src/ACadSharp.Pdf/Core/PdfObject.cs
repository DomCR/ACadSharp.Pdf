﻿namespace ACadSharp.Pdf.Core
{
	public abstract class PdfObject : PdfItem
	{
		public PdfObjectReference Reference { get; }

		public PdfObject()
		{
			this.Reference = new PdfObjectReference(this);
		}

	}
}
