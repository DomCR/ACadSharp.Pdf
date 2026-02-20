using CSMath;
using System;
using System.Reflection;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class InfiniteLineClipperTests
	{
		[Fact]
		public void ClipXLine_ThroughCenter()
		{
			var rect = new BoundingBox(new XYZ(0, 0, 0), new XYZ(100, 100, 0));
			var clipped = clipXLine(new XY(50, 50), new XY(1, 0), rect);

			Assert.True(clipped.HasValue);
			assertUnordered(clipped.Value, new XY(0, 50), new XY(100, 50));
		}

		[Fact]
		public void ClipXLine_Diagonal()
		{
			var rect = new BoundingBox(new XYZ(0, 0, 0), new XYZ(100, 100, 0));
			var clipped = clipXLine(new XY(50, 50), new XY(1, 1), rect);

			Assert.True(clipped.HasValue);
			assertUnordered(clipped.Value, new XY(0, 0), new XY(100, 100));
		}

		[Fact]
		public void ClipXLine_Outside_ReturnsNull()
		{
			var rect = new BoundingBox(new XYZ(0, 0, 0), new XYZ(100, 100, 0));
			var clipped = clipXLine(new XY(200, 200), new XY(1, 0), rect);
			Assert.False(clipped.HasValue);
		}

		[Fact]
		public void ClipXLine_ParallelToEdge_OnBoundary()
		{
			var rect = new BoundingBox(new XYZ(0, 0, 0), new XYZ(100, 100, 0));
			var clipped = clipXLine(new XY(0, 50), new XY(1, 0), rect);

			Assert.True(clipped.HasValue);
			assertUnordered(clipped.Value, new XY(0, 50), new XY(100, 50));
		}

		[Fact]
		public void ClipRay_StartingInside()
		{
			var rect = new BoundingBox(new XYZ(0, 0, 0), new XYZ(100, 100, 0));
			var clipped = clipRay(new XY(50, 50), new XY(1, 0), rect);

			Assert.True(clipped.HasValue);
			Assert.True(distance(clipped.Value.Start, new XY(50, 50)) < 1e-9);
			Assert.True(distance(clipped.Value.End, new XY(100, 50)) < 1e-9);
		}

		[Fact]
		public void ClipRay_OutsidePointingIn()
		{
			var rect = new BoundingBox(new XYZ(0, 0, 0), new XYZ(100, 100, 0));
			var clipped = clipRay(new XY(-50, 50), new XY(1, 0), rect);

			Assert.True(clipped.HasValue);
			assertUnordered(clipped.Value, new XY(0, 50), new XY(100, 50));
		}

		[Fact]
		public void ClipRay_OutsidePointingAway_ReturnsNull()
		{
			var rect = new BoundingBox(new XYZ(0, 0, 0), new XYZ(100, 100, 0));
			var clipped = clipRay(new XY(-50, 50), new XY(-1, 0), rect);
			Assert.False(clipped.HasValue);
		}

		[Fact]
		public void ClipXLine_Vertical()
		{
			var rect = new BoundingBox(new XYZ(0, 0, 0), new XYZ(100, 100, 0));
			var clipped = clipXLine(new XY(50, 0), new XY(0, 1), rect);

			Assert.True(clipped.HasValue);
			assertUnordered(clipped.Value, new XY(50, 0), new XY(50, 100));
		}

		[Fact]
		public void ClipXLine_ZeroDirection_ReturnsNull()
		{
			var rect = new BoundingBox(new XYZ(0, 0, 0), new XYZ(100, 100, 0));
			var clipped = clipXLine(new XY(50, 50), XY.Zero, rect);
			Assert.False(clipped.HasValue);
		}

		[Fact]
		public void ClipXLine_NearlyParallel_ReturnsSegment()
		{
			var rect = new BoundingBox(new XYZ(0, 0, 0), new XYZ(100, 100, 0));
			var clipped = clipXLine(new XY(50, 0), new XY(1, 0.001), rect);
			Assert.True(clipped.HasValue);

			Assert.True(isInRect(clipped.Value.Start, rect));
			Assert.True(isInRect(clipped.Value.End, rect));
		}

		private static (XY Start, XY End)? clipRay(XY basePoint, XY direction, BoundingBox rect)
		{
			return invokeClip("ClipRay", basePoint, direction, rect);
		}

		private static (XY Start, XY End)? clipXLine(XY basePoint, XY direction, BoundingBox rect)
		{
			return invokeClip("ClipXLine", basePoint, direction, rect);
		}

		private static (XY Start, XY End)? invokeClip(string method, XY basePoint, XY direction, BoundingBox rect)
		{
			Type t = typeof(ACadSharp.Pdf.PdfConfiguration).Assembly.GetType(
				"ACadSharp.Pdf.Core.Render.SceneGraph.InfiniteLineClipper",
				throwOnError: true);

			MethodInfo mi = t.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			Assert.NotNull(mi);

			object result = mi.Invoke(null, new object[] { basePoint, direction, rect });
			if (result == null)
			{
				return null;
			}

			return ((XY Start, XY End))result;
		}

		private static void assertUnordered((XY Start, XY End) segment, XY a, XY b)
		{
			double d1 = distance(segment.Start, a) + distance(segment.End, b);
			double d2 = distance(segment.Start, b) + distance(segment.End, a);
			Assert.True(Math.Min(d1, d2) < 1e-9);
		}

		private static bool isInRect(XY p, BoundingBox rect)
		{
			double minX = Math.Min(rect.Min.X, rect.Max.X);
			double minY = Math.Min(rect.Min.Y, rect.Max.Y);
			double maxX = Math.Max(rect.Min.X, rect.Max.X);
			double maxY = Math.Max(rect.Min.Y, rect.Max.Y);
			return p.X >= minX - 1e-9 && p.X <= maxX + 1e-9 && p.Y >= minY - 1e-9 && p.Y <= maxY + 1e-9;
		}

		private static double distance(XY a, XY b)
		{
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;
			return Math.Sqrt(dx * dx + dy * dy);
		}
	}
}

