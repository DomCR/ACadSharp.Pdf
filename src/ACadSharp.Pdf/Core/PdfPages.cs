using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ACadSharp.Pdf.Core
{
	public class PdfPages : PdfDictionary, IEnumerable<PdfPage>
	{
		private readonly PdfArray<PdfObjectReference> _pages = new();

		public PdfPages()
		{
			this.Items.Add("/Type", new PdfName("/Pages"));
			this.Items.Add("/Kids", this._pages);
			this.Items.Add("/Count", this._pages.CountReference);
		}

		public PdfPage AddPage()
		{
			PdfPage page = new PdfPage(this);
			this._pages.Add(page.Reference);
			return page;
		}

		public override void SetId(ref int currNumber)
		{
			base.SetId(ref currNumber);

			foreach (PdfPage item in this)
			{
				item.SetId(ref currNumber);
			}
		}

		public IEnumerator<PdfPage> GetEnumerator()
		{
			return this._pages.Items.Select(r => r.Value as PdfPage).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return this._pages.Items.Select(r => r.Value as PdfPage).GetEnumerator();
		}
	}
}
