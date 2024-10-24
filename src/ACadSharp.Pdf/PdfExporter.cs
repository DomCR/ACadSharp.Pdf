using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using System;
using System.Collections.Generic;
using System.IO;

namespace ACadSharp.Pdf
{
	/// <summary>
	/// Exporter to create a pdf document.
	/// </summary>
	public class PdfExporter : IDisposable
	{
		public PdfConfiguration Configuration { get; } = new PdfConfiguration();

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
				if (e is Viewport)
				{
					continue;
				}

				page.Entities.Add(e);
			}

			foreach (Viewport vp in layout.Viewports)
			{
				if (vp.RepresentsPaper)
				{
					continue;
				}

				page.Viewports.Add(vp);
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
			using (PdfWriter writer = new PdfWriter(this._stream, this._pdf, this.Configuration))
			{
				writer.Write();
			}
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			//this._pdf.Dispose();
		}
	}
}
