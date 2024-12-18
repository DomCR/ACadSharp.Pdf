﻿using System.Collections.Generic;
using System.Linq;
using System.Text;

#if NETFRAMEWORK
using CSUtilities.Extensions;
#endif

namespace ACadSharp.Pdf.Core
{
	public class PdfArray<T> : PdfItem
		where T : PdfItem
	{
		public PdfReference<int> CountReference { get; }

		public List<T> Items { get; } = new();

		public PdfArray()
		{
			this.CountReference = new(() => this.Items.Count);
		}

		public void Add(T item)
		{
			this.Items.Add(item);
		}

		public override string GetPdfForm(PdfConfiguration configuration)
		{
			StringBuilder sb = new StringBuilder();

			sb.Append("[");

			sb.AppendJoin(" ", this.Items.Select(i => i.GetPdfForm(configuration)));

			sb.Append("]");

			return sb.ToString();
		}
	}
}
