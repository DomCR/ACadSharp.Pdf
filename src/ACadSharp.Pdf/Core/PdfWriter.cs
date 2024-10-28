using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ACadSharp.Pdf.Core
{
	internal class PdfWriter : IDisposable
	{
		private int _currNumber = 1;
		private Encoding _encoding = Encoding.ASCII;
		private Stream _stream;
		private PdfDocument _document;
		private PdfConfiguration _configuration;

		private List<long> _xrefs = new();

		public PdfWriter(Stream stream, PdfDocument document, PdfConfiguration configuration)
		{
			_stream = stream;
			_document = document;
			_configuration = configuration;
		}

		public void Write()
		{
			this.assignIds();

			this.writeLine("%PDF-1.5");
			this.write(new byte[6] { 0x25, 0xA1, 0xD3, 0xF4, 0xCC, 0xA });

			//Information
			this.writePdfDictionary(_document.Information);

			//Catalog
			this.writePdfDictionary(_document.Catalog);

			//Outlines (optional)

			//Pages
			this.writePdfDictionary(_document.Pages);
			//Page
			foreach (PdfPage item in this._document.Pages)
			{
				this.writePdfDictionary(item);
				this.writePdfDictionary(item.Contents);
			}

			long xrefPos = this.writeXRef();

			this.writeTrailer();

			this.writeLine(PdfKey.StartXref);
			this.writeLine(xrefPos.ToString(CultureInfo.InvariantCulture));
			this.write("%%EOF");
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			this._stream.Dispose();
		}

		private void assignIds()
		{
			this.assignId(this._document.Information);
			this.assignId(this._document.Catalog);
			this.assignId(this._document.Pages);
		}

		private void assignId(PdfItem item)
		{
			item.SetId(ref this._currNumber);
		}

		private void writePdfDictionary(PdfDictionary dict)
		{
			this._xrefs.Add(this._stream.Position);
			this.write(dict.GetPdfForm(this._configuration));
		}

		private long writeXRef()
		{
			long position = this._stream.Position;

			this.writeLine("xref");
			this.writeLine($"0 {this._currNumber}");
			this.writeLine($"0000000000 65535 f");

			foreach (var item in this._xrefs)
			{
				this.writeLine($"{item.ToString("0000000000", CultureInfo.InvariantCulture)} 00000 n");
			}

			return position;
		}

		private void writeTrailer()
		{
			this.writeLine("trailer");
			this.writeLine("<<");
			this.writeLine($"/Size {this._currNumber}");
			this.writeLine($"/Info {this._document.Information.Id} 0 R");
			this.writeLine($"/Root {this._document.Catalog.Id} 0 R");
			this.writeLine(">>");
		}

		private void write(byte[] bytes)
		{
			_stream.Write(bytes);
		}

		private void write(string str)
		{
			this.write(this._encoding.GetBytes(str));
		}

		private void writeLine(string str)
		{
			this.write(str + "\n");
		}
	}
}
