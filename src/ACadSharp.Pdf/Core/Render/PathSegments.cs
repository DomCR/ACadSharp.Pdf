using CSMath;

namespace ACadSharp.Pdf.Core.Render
{
	public abstract class PathSegment { }

	public sealed class MoveTo : PathSegment
	{
		public XY Point { get; }

		public MoveTo(XY point) { this.Point = point; }
	}

	public sealed class LineTo : PathSegment
	{
		public XY Point { get; }

		public LineTo(XY point) { this.Point = point; }
	}

	public sealed class CubicTo : PathSegment
	{
		public XY C1 { get; }

		public XY C2 { get; }

		public XY End { get; }

		public CubicTo(XY c1, XY c2, XY end)
		{
			this.C1 = c1;
			this.C2 = c2;
			this.End = end;
		}
	}

	public sealed class ClosePath : PathSegment { }
}
