using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Pdf.Core;
using ACadSharp.Tables;
using System;
using System.Collections.Generic;
using System.IO;

namespace ACadSharp.Pdf
{
	/// <summary>
	/// Exporter to create a pdf document.
	/// </summary>
	public class PdfExporter
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

		/// <summary>
		/// Add the model space form the referenced cad document.
		/// </summary>
		public void AddModelSpace()
		{
			this.AddModelSpace(this.Configuration.ReferenceDocument);
		}

		/// <summary>
		/// Add the model space from a cad document.
		/// </summary>
		/// <param name="document"></param>
		/// <remarks>
		/// This method does not import the <see cref="TableEntry"/> from the document.
		/// </remarks>
		public void AddModelSpace(CadDocument document)
		{
			this.Add(document.ModelSpace);
		}

		/// <summary>
		/// Add all the paper layouts 
		/// </summary>
		public void AddPaperLayouts()
		{
			this.AddPaperLayouts(this.Configuration.ReferenceDocument);
		}

		public void AddPaperLayouts(CadDocument document)
		{
			this.Add(document.Layouts);
		}

		/// <summary>
		/// Add layouts to the pdf as pages.
		/// </summary>
		/// <param name="layouts"></param>
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

		/// <summary>
		/// Add a <see cref="Layout"/> to the pdf as a page.
		/// </summary>
		/// <param name="layout"></param>
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

		/// <summary>
		/// Add a <see cref="BlockRecord"/> as a page.
		/// </summary>
		/// <param name="block"></param>
		public void Add(BlockRecord block)
		{
			PdfPage page = this._pdf.Pages.AddPage();

			page.Add(block);
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
	}
}
