using System;
using System.Runtime.CompilerServices;

namespace ACadSharp.Pdf
{
	public class PdfInformation : PdfDictionary
	{
		public string Title
		{
			get { return this.getValue(); }
			set
			{
				this.updateUpdate(value);
			}
		}

		public string Author
		{
			get { return this.getValue(); }
			set
			{
				this.updateUpdate(value);
			}
		}

		public string Creator
		{
			get { return this.getValue(); }
			set
			{
				this.updateUpdate(value);
			}
		}

		public string CreationDate
		{
			get { return this.getValue(); }
			set
			{
				this.updateUpdate(value);
			}
		}

		public PdfInformation()
		{
			this.Title = string.Empty;
			this.Author = string.Empty;
			this.Creator = "ACadSharp.Pdf";
			this.CreationDate = DateTime.Now.ToString(); 
		}

		protected string getValue([CallerMemberName] string name = null)
		{
			if (this.Items.TryGetValue($"/{name}", out PdfItem value) && value is PdfString str)
			{
				return str.ToString();
			}
			else
			{
				return string.Empty;
			}
		}

		protected void updateUpdate(string value, [CallerMemberName] string name = null)
		{
			this.Items[$"/{name}"] = new PdfString(value);
		}
	}
}
