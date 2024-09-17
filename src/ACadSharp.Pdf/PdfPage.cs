using ACadSharp.Entities;
using ACadSharp.Objects;
using System.Collections.Generic;

namespace ACadSharp.Pdf
{
	public class PdfPage : PdfDictionary
	{
		/// <summary>
		/// Paper width value in Pdf units.
		/// </summary>
		public double Width
		{
			get
			{
				return PdfUnitType.Millimeter.ToUnit(this.Layout.PaperWidth);
			}
		}

		/// <summary>
		/// Paper height value in Pdf units.
		/// </summary>
		public double Height
		{
			get
			{
				return PdfUnitType.Millimeter.ToUnit(this.Layout.PaperHeight);
			}
		}

		/// <summary>
		/// Layout applied to this page.
		/// </summary>
		public Layout Layout { get; set; } = new Layout("default_page");

		/// <summary>
		/// Entities to draw in the page.
		/// </summary>
		public List<Entity> Entities { get; } = new();

		public PdfPageContent Contents { get; }

		private readonly PdfPages _parent;

		public PdfPage(PdfPages pages)
		{
			this._parent = pages;

			this.Items.Add("/Type", new PdfName("/Page"));
			this.Items.Add("/Parent", this._parent.Reference);

			this.Contents = new PdfPageContent(this);
			this.Items.Add("/Contents", this.Contents.Reference);

			PdfArray<PdfReference<double>> mediaBox = new();
			mediaBox.Add(new PdfReference<double>(() => 0));
			mediaBox.Add(new PdfReference<double>(() => 0));
			mediaBox.Add(new PdfReference<double>(() => this.Width));
			mediaBox.Add(new PdfReference<double>(() => this.Height));
			this.Items.Add("/MediaBox", mediaBox);
		}

		public override void SetId(ref int currNumber)
		{
			base.SetId(ref currNumber);

			this.Contents.SetId(ref currNumber);
		}
	}
}
