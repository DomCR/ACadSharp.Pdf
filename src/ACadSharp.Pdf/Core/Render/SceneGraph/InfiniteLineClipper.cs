using CSMath;
using System;

namespace ACadSharp.Pdf.Core.Render.SceneGraph
{
	internal static class InfiniteLineClipper
	{
		private const double ParallelEpsilon = 1e-12;
		private const double MinSegmentLength = 1e-9;

		public static (XY Start, XY End)? ClipRay(XY basePoint, XY direction, BoundingBox clipRect)
		{
			return clipInfiniteLine(basePoint, direction, clipRect, tMinInitial: 0.0);
		}

		public static (XY Start, XY End)? ClipXLine(XY basePoint, XY direction, BoundingBox clipRect)
		{
			return clipInfiniteLine(basePoint, direction, clipRect, tMinInitial: double.NegativeInfinity);
		}

		private static (XY Start, XY End)? clipInfiniteLine(XY basePoint, XY direction, BoundingBox clipRect, double tMinInitial)
		{
			if (!isFinite(basePoint.X) || !isFinite(basePoint.Y)
				|| !isFinite(direction.X) || !isFinite(direction.Y))
			{
				return null;
			}

			if (Math.Abs(direction.X) <= ParallelEpsilon && Math.Abs(direction.Y) <= ParallelEpsilon)
			{
				return null;
			}

			if (!tryGetClipRect(clipRect, out double minX, out double minY, out double maxX, out double maxY))
			{
				return null;
			}

			double x0 = basePoint.X;
			double y0 = basePoint.Y;
			double dx = direction.X;
			double dy = direction.Y;

			double tMin = tMinInitial;
			double tMax = double.PositiveInfinity;

			if (!clipBoundary(-dx, x0 - minX, ref tMin, ref tMax)) return null; // left
			if (!clipBoundary(+dx, maxX - x0, ref tMin, ref tMax)) return null; // right
			if (!clipBoundary(-dy, y0 - minY, ref tMin, ref tMax)) return null; // bottom
			if (!clipBoundary(+dy, maxY - y0, ref tMin, ref tMax)) return null; // top

			if (tMin > tMax)
			{
				return null;
			}

			XY start = new XY(x0 + tMin * dx, y0 + tMin * dy);
			XY end = new XY(x0 + tMax * dx, y0 + tMax * dy);

			if (!isFinite(start.X) || !isFinite(start.Y) || !isFinite(end.X) || !isFinite(end.Y))
			{
				return null;
			}

			double segmentLengthSq = (end.X - start.X) * (end.X - start.X) + (end.Y - start.Y) * (end.Y - start.Y);
			if (segmentLengthSq <= MinSegmentLength * MinSegmentLength)
			{
				return null;
			}

			return (start, end);
		}

		private static bool clipBoundary(double p, double q, ref double tMin, ref double tMax)
		{
			if (Math.Abs(p) <= ParallelEpsilon)
			{
				return q >= 0.0;
			}

			double t = q / p;
			if (p < 0.0)
			{
				if (t > tMin)
				{
					tMin = t;
				}
			}
			else
			{
				if (t < tMax)
				{
					tMax = t;
				}
			}

			return tMin <= tMax;
		}

		private static bool tryGetClipRect(BoundingBox clipRect, out double minX, out double minY, out double maxX, out double maxY)
		{
			minX = Math.Min(clipRect.Min.X, clipRect.Max.X);
			minY = Math.Min(clipRect.Min.Y, clipRect.Max.Y);
			maxX = Math.Max(clipRect.Min.X, clipRect.Max.X);
			maxY = Math.Max(clipRect.Min.Y, clipRect.Max.Y);

			if (!isFinite(minX) || !isFinite(minY) || !isFinite(maxX) || !isFinite(maxY))
			{
				return false;
			}

			return maxX >= minX && maxY >= minY;
		}

		private static bool isFinite(double value)
		{
			return !double.IsNaN(value) && !double.IsInfinity(value);
		}
	}
}
