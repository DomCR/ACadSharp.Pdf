using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf.Extensions;
using ACadSharp.Tables;
using CSMath;
using System.Collections.Generic;
using System.Linq;

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
				switch (this.Layout.PaperRotation)
				{
					case PlotRotation.Degrees90:
					case PlotRotation.Degrees270:
						return PdfUnitType.Millimeter.Transform(this.Layout.PaperHeight);
					case PlotRotation.NoRotation:
					case PlotRotation.Degrees180:
					default:
						return PdfUnitType.Millimeter.Transform(this.Layout.PaperWidth);
				}
			}
		}

		/// <summary>
		/// Paper height value in Pdf units.
		/// </summary>
		public double Height
		{
			get
			{
				switch (this.Layout.PaperRotation)
				{
					case PlotRotation.Degrees90:
					case PlotRotation.Degrees270:
						return PdfUnitType.Millimeter.Transform(this.Layout.PaperWidth);
					case PlotRotation.NoRotation:
					case PlotRotation.Degrees180:
					default:
						return PdfUnitType.Millimeter.Transform(this.Layout.PaperHeight);
				}
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

		public PdfContent Contents { get; }

		private readonly PdfPages _parent;

		public PdfPage(PdfPages pages)
		{
			this._parent = pages;

			this.Items.Add("/Type", new PdfName("/Page"));
			this.Items.Add("/Parent", this._parent.Reference);

			this.Contents = new PdfContent(this);
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

		/// <summary>
		/// Add the block record to the page.
		/// </summary>
		/// <param name="block"></param>
		/// <param name="resizeLayout">Resize the layout to fit all the current entities.</param>
		/// <exception cref="System.NotImplementedException"></exception>
		public void Add(BlockRecord block, bool resizeLayout = true)
		{
			this.Entities.AddRange(block.Entities);

			if (resizeLayout)
			{
				this.UpdateLayoutSize();
			}
		}

		/// <summary>
		/// Update the layout size to fit all the entities in the view.
		/// </summary>
		public void UpdateLayoutSize()
		{
			BoundingBox limits = BoundingBox.Merge(this.Entities.Select(e => e.GetBoundingBox()));

			this.Contents.Translation = -(XY)limits.Min;

			limits = limits.Move(-limits.Min);

			this.Layout.PaperWidth = limits.Max.X;
			this.Layout.PaperHeight = limits.Max.Y;
		}
	}
}
