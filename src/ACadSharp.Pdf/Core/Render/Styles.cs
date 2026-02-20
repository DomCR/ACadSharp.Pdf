using System;
using System.Collections.Generic;

namespace ACadSharp.Pdf.Core.Render
{
	public sealed class StrokeStyle
	{
		public ACadSharp.Color Color { get; }

		public double WidthPt { get; }

		public IReadOnlyList<double> DashArrayPt { get; }

		public double DashOffsetPt { get; }

		public StrokeStyle(ACadSharp.Color color, double widthPt, IReadOnlyList<double> dashArrayPt, double dashOffsetPt)
		{
			this.Color = color;
			this.WidthPt = widthPt;
			this.DashArrayPt = dashArrayPt ?? Array.Empty<double>();
			this.DashOffsetPt = dashOffsetPt;
		}
	}

	public sealed class FillStyle
	{
		public ACadSharp.Color Color { get; }

		public FillStyle(ACadSharp.Color color)
		{
			this.Color = color;
		}
	}
}
