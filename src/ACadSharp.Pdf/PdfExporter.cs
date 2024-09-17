using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace ACadSharp.Pdf
{
	/// <summary>
	/// Exporter to create a pdf document.
	/// </summary>
	public class PdfExporter : IDisposable
	{
		/// <summary>
		/// Notification event to get information about the export process.
		/// </summary>
		/// <remarks>
		/// The notification system informs about any issue or non critical errors during the export.
		/// </remarks>
		public event NotificationEventHandler OnNotification;

		public PdfExporterConfiguration Configuration { get; } = new PdfExporterConfiguration();

		private readonly PdfDocument _pdf;
		private readonly Stream _stream;

		/// <summary>
		/// Initialize an instance of <see cref="PdfExporter"/>.
		/// </summary>
		/// <param name="path">Path where the pdf will be saved.</param>
		public PdfExporter(string path) : this(File.Create(path))
		{
		}

		/// <summary>
		/// Initialize an instance of <see cref="PdfExporter"/>.
		/// </summary>
		/// <param name="stream">Stream where the pdf will be saved.</param>
		public PdfExporter(Stream stream)
		{
			this._stream = stream;
			this._pdf = new PdfDocument();
		}

		public void AddModelSpace()
		{
			this.AddModelSpace(this.Configuration.ReferenceDocument);
		}

		public void AddModelSpace(CadDocument document)
		{
			this.Add(document.ModelSpace);
		}

		public void AddPaperSpaces()
		{
			this.AddPaperSpaces(this.Configuration.ReferenceDocument);
		}

		public void AddPaperSpaces(CadDocument document)
		{
			this.Add(document.Layouts);
		}

		public void Add(IEnumerable<Layout> layouts)
		{
			foreach (var layout in layouts)
			{
				if (!layout.IsPaperSpace)
				{
					continue;
				}

				this.Add(layout);
			}
		}

		public void Add(Layout layout)
		{
			PdfPage page = this._pdf.Pages.AddPage();

			page.Layout = layout;

			foreach (Entity e in layout.AssociatedBlock.Entities)
			{
				page.Entities.Add(e);
			}

			//page.Width = new XUnit(layout.PaperWidth, XGraphicsUnit.Millimeter);
			//page.Height = new XUnit(layout.PaperHeight, XGraphicsUnit.Millimeter);

			//if (layout.PaperRotation == PlotRotation.Degrees90)
			//{
			//	page.Orientation = PdfSharp.PageOrientation.Landscape;
			//}

			////TODO: Setup margins
			////page.TrimMargins.Bottom = new XUnit(layout.UnprintableMargin.Bottom);
			////page.TrimMargins.Right = new XUnit(10, XGraphicsUnit.Millimeter);
			////page.TrimMargins.All = new XUnit(10, XGraphicsUnit.Millimeter);

			////TODO: Fix rotation page to match with Layout config
			////switch (layout.PaperRotation)
			////{
			////	case PlotRotation.NoRotation:
			////		break;
			////	case PlotRotation.Degrees90:
			////		page.Rotate = 90;
			////		break;
			////	case PlotRotation.Degrees180:
			////		page.Rotate = 180;
			////		break;
			////	case PlotRotation.Degrees270:
			////		page.Rotate = 270;
			////		break;
			////}

			//XGraphicsUnit unit;
			//switch (layout.PaperUnits)
			//{
			//	case PlotPaperUnits.Inches:
			//		unit = XGraphicsUnit.Inch;
			//		break;
			//	case PlotPaperUnits.Milimeters:
			//		unit = XGraphicsUnit.Millimeter;
			//		break;
			//	case PlotPaperUnits.Pixels:
			//		unit = XGraphicsUnit.Point;
			//		break;
			//	default:
			//		unit = XGraphicsUnit.Point;
			//		break;
			//}

			//XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append, unit, XPageDirection.Upwards);

			////gfx.PageUnit = unit;
			////Draw paper entities
			//foreach (Entity e in layout.AssociatedBlock.Entities)
			//{
			//	XPen pen = this.getDrawingPen(e);

			//	if (e is Line)
			//	{
			//		this.drawEntity(gfx, pen, e);
			//	}
			//}

			//foreach (Viewport vp in layout.Viewports)
			//{
			//	if (vp.RepresentsPaper)
			//	{
			//		continue;
			//	}

			//	XPen pen = this.getDrawingPen(vp);

			//	this.drawViewport(gfx, pen, vp);
			//}
		}

		public void Add(BlockRecord block)
		{
			PdfPage page = this._pdf.Pages.AddPage();

			foreach (Entity e in block.Entities)
			{
				page.Entities.Add(e);
			}
		}

		public void AddPage(BlockRecord block, PlotSettings settings)
		{
			throw new NotImplementedException();
		}

		public void AddLayouts(CadDocument document)
		{
			foreach (Objects.Layout layout in document.Layouts)
			{
				this.Add(layout);
			}
		}

		/// <summary>
		/// Close the document and save it.
		/// </summary>
		public void Close()
		{
			using (PdfWriter writer = new PdfWriter(this._stream, _pdf))
			{
				writer.Write();
			}
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			//this._pdf.Dispose();
		}

		protected void notify(string message, NotificationType notificationType, Exception ex = null)
		{
			this.OnNotification?.Invoke(this, new NotificationEventArgs(message, notificationType, ex));
		}
	}
}
