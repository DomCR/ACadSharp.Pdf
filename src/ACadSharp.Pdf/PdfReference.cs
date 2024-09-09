using System;

namespace ACadSharp.Pdf
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

		public override string GetStringForm()
		{
			return this._f.Invoke().ToString();
		}
	}
}
