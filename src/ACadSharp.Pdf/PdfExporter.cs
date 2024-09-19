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

			foreach (ViewPort vp in layout.Viewports)
			{
				if (vp.RepresentsPaper)
				{
					continue;
				}

				page.Entities.Add(vp);
			}
		}

		public void Add(BlockRecord block)
		{
			PdfPage page = this._pdf.Pages.AddPage();

			page.Add(block);
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
