using System;
using System.Globalization;

namespace ACadSharp.Pdf.Core
{
	public class PdfReference<T> : PdfItem
	{
		public T Value { get { return this._f.Invoke(); } }

		private readonly Func<T> _f;

		public PdfReference(Func<T> f)
		{
			this._f = f;
		}

		public void Print()
		{
			Console.WriteLine(_f());
		}

		public override string GetPdfForm(PdfConfiguration configuration)
		{
			T value = this._f.Invoke();

			if (value is IFormattable formattable)
			{
				return formattable.ToString(null, CultureInfo.InvariantCulture);
			}

			return value.ToString();
		}
	}
}
