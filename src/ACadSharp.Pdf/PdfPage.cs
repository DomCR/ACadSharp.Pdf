using ACadSharp.Entities;
using ACadSharp.Objects;
using System.Collections.Generic;

namespace ACadSharp.Pdf
{
	public class PdfPage : PdfDictionary
	{
		/// <summary>
		/// Layout applied to this page.
		/// </summary>
		public Layout Layout { get; set; }

		/// <summary>
		/// Entities to draw in the page.
		/// </summary>
		public List<Entity> Entities { get; } = new();

		public PdfPageContent Contents { get; set; } = new();

		private readonly PdfPages _parent;

		public PdfPage(PdfPages pages)
		{
			this._parent = pages;

			this.Items.Add("/Type", new PdfName("/Page"));
			this.Items.Add("/Parent", this._parent.Reference);
			this.Items.Add("/Contents", this.Contents.Reference);
		}

		public override void SetId(ref int currNumber)
		{
			base.SetId(ref currNumber);

			this.Contents.SetId(ref currNumber);
		}
	}
}
