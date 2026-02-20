using CSMath;
using System;
using System.Collections.Generic;

namespace ACadSharp.Pdf.Core.Render.Flattening
{
	public abstract class FlatDrawCommand { }

	public sealed class FlatBeginClipCommand : FlatDrawCommand
	{
		public IReadOnlyList<PathSegment> ClipSegmentsPdfPt { get; }

		public FlatBeginClipCommand(IReadOnlyList<PathSegment> clipSegmentsPdfPt)
		{
			this.ClipSegmentsPdfPt = clipSegmentsPdfPt ?? throw new ArgumentNullException(nameof(clipSegmentsPdfPt));
		}
	}

	public sealed class FlatEndClipCommand : FlatDrawCommand { }

	public sealed class FlatPathCommand : FlatDrawCommand
	{
		public IReadOnlyList<PathSegment> SegmentsPdfPt { get; }
		public StrokeStyle Stroke { get; }
		public FillStyle Fill { get; }

		public FlatPathCommand(IReadOnlyList<PathSegment> segmentsPdfPt, StrokeStyle stroke, FillStyle fill)
		{
			this.SegmentsPdfPt = segmentsPdfPt ?? throw new ArgumentNullException(nameof(segmentsPdfPt));
			this.Stroke = stroke;
			this.Fill = fill;
		}
	}

	public sealed class FlatTextCommand : FlatDrawCommand
	{
		public string Text { get; }
		public double FontSizePt { get; }
		public XY AnchorPdfPt { get; }
		public double A { get; }
		public double B { get; }
		public double C { get; }
		public double D { get; }
		public ACadSharp.Color Color { get; }

		public FlatTextCommand(string text, double fontSizePt, XY anchorPdfPt, double a, double b, double c, double d, ACadSharp.Color color)
		{
			this.Text = text ?? string.Empty;
			this.FontSizePt = fontSizePt;
			this.AnchorPdfPt = anchorPdfPt;
			this.A = a;
			this.B = b;
			this.C = c;
			this.D = d;
			this.Color = color;
		}
	}

	public sealed class FlatImageCommand : FlatDrawCommand
	{
		public byte[] Rgb24Data { get; }

		public int SourceWidthPixels { get; }

		public int SourceHeightPixels { get; }

		public double DisplayWidth { get; }

		public double DisplayHeight { get; }

		public double A { get; }

		public double B { get; }

		public double C { get; }

		public double D { get; }

		public double E { get; }

		public double F { get; }

		public FlatImageCommand(
			byte[] rgb24Data,
			int sourceWidthPixels,
			int sourceHeightPixels,
			double displayWidth,
			double displayHeight,
			double a,
			double b,
			double c,
			double d,
			double e,
			double f)
		{
			this.Rgb24Data = rgb24Data ?? throw new ArgumentNullException(nameof(rgb24Data));
			this.SourceWidthPixels = sourceWidthPixels;
			this.SourceHeightPixels = sourceHeightPixels;
			this.DisplayWidth = displayWidth;
			this.DisplayHeight = displayHeight;
			this.A = a;
			this.B = b;
			this.C = c;
			this.D = d;
			this.E = e;
			this.F = f;
		}
	}
}
