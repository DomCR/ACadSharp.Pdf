using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Pdf.Extensions;
using ACadSharp.Tables;
using CSMath;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
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

		public PdfDocumentInformation Information
		{
			get { return this._pdf.Info; }
		}

		private readonly PdfDocument _pdf;

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
			this._pdf = new PdfDocument(stream);
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
			PdfPage page = this._pdf.AddPage();
			page.Width = new XUnit(layout.PaperWidth, XGraphicsUnit.Millimeter);
			page.Height = new XUnit(layout.PaperHeight, XGraphicsUnit.Millimeter);

			switch (layout.PaperRotation)
			{
				case PlotRotation.NoRotation:
					break;
				case PlotRotation.Degrees90:
					page.Rotate = 90;
					break;
				case PlotRotation.Degrees180:
					page.Rotate = 180;
					break;
				case PlotRotation.Degrees270:
					page.Rotate = 270;
					break;
			}

			XGraphicsUnit unit;
			switch (layout.PaperUnits)
			{
				case PlotPaperUnits.Inches:
					unit = XGraphicsUnit.Inch;
					break;
				case PlotPaperUnits.Milimeters:
					unit = XGraphicsUnit.Millimeter;
					break;
				case PlotPaperUnits.Pixels:
					unit = XGraphicsUnit.Point;
					break;
				default:
					unit = XGraphicsUnit.Point;
					break;
			}

			XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append, unit, XPageDirection.Upwards);

			foreach (Viewport vp in layout.Viewports)
			{
				XPen pen = this.getDrawingPen(vp);

				this.drawViewport(gfx, pen, vp);
			}

			//throw new NotImplementedException();
		}

		public void Add(BlockRecord block)
		{
			PdfPage page = this._pdf.AddPage();

			BoundingBox viewBox = BoundingBox.Null;
			XGraphics gfx = XGraphics.FromPdfPage(page);
			gfx.PageDirection = XPageDirection.Upwards;

			foreach (Entity e in block.Entities)
			{
				XPen pen = this.getDrawingPen(e);

				this.drawEntity(gfx, pen, e);
				viewBox = viewBox.Merge(e.GetBoundingBox());
			}

			this.updateSize(page, gfx, viewBox);
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
			this._pdf.Close();
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			this._pdf.Dispose();
		}

		private XPen getDrawingPen(Entity entity)
		{
			Color color;
			if (entity.Color.IsByLayer)
			{
				color = entity.Color;
			}
			else
			{
				color = entity.Color;
			}

			return new XPen(color.ToXColor(), 1);
		}

		protected void notify(string message, NotificationType notificationType, Exception ex = null)
		{
			this.OnNotification?.Invoke(this, new NotificationEventArgs(message, notificationType, ex));
		}

		private void drawEntity(XGraphics gfx, XPen pen, Entity entity)
		{
			switch (entity)
			{
				case Arc arc:
					this.drawArc(gfx, pen, arc);
					break;
				case Circle circle:
					this.drawCircle(gfx, pen, circle);
					break;
				case Line line:
					this.drawLine(gfx, pen, line);
					break;
				case Point point:
					this.drawPoint(gfx, pen, point);
					break;
				default:
					this.notify($"Entity {entity.SubclassMarker} not implemented.", NotificationType.NotImplemented);
					break;
			}
		}

		private void drawArc(XGraphics gfx, XPen pen, Arc arc)
		{
			var start = MathUtils.RadToDeg(arc.StartAngle);
			var end = MathUtils.RadToDeg(arc.EndAngle);

			if (arc.EndAngle < arc.StartAngle)
			{
				gfx.DrawArc(pen, arc.GetBoundingBox().ToXRect(), start - 360, end + Math.Abs(start - 360));
			}
			else
			{
				gfx.DrawArc(pen, arc.GetBoundingBox().ToXRect(), start, end);
			}
		}

		private void drawCircle(XGraphics gfx, XPen pen, Circle circle)
		{
			var box = circle.GetBoundingBox();
			gfx.DrawEllipse(pen, box.ToXRect());
		}

		private void drawLine(XGraphics gfx, XPen pen, Line line)
		{
			gfx.DrawLine(pen, line.StartPoint.X, line.StartPoint.Y, line.EndPoint.X, line.EndPoint.Y);
		}

		private void drawPoint(XGraphics gfx, XPen pen, Point point)
		{
			//TODO: Fix point drawing, add weight
			gfx.DrawLine(pen, point.Location.X, point.Location.Y, point.Location.X, point.Location.Y);
			var box = point.GetBoundingBox();
			box.Max = box.Max + new XYZ(1);
			box.Min = box.Min - new XYZ(1);

			XSolidBrush brush = new XSolidBrush();

			gfx.DrawEllipse(pen, brush, box.ToXRect());
		}

		private void drawViewport(XGraphics gfx, XPen pen, Viewport vp)
		{
			//Inches to mm = 1 * 25.4

			XRect rect = new XRect(vp.Center.X, vp.Center.Y, vp.Width, vp.Height);
			gfx.DrawRectangle(pen, vp.GetBoundingBox().ToXRect());
		}

		private void updateSize(PdfPage page, XGraphics gfx, BoundingBox box)
		{
			if (box.Extent == BoundingBoxExtent.Infinite ||
				box.Extent == BoundingBoxExtent.Null)
			{
				return;
			}

			page.Width = new XUnit(box.Max.X);
			page.Height = new XUnit(box.Max.Y);

			//gfx.TranslateTransform(box.Min.X, box.Max.Y);
		}
	}
}
