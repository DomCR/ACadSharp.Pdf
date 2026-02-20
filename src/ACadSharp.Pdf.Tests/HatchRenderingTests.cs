using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf;
using ACadSharp.Pdf.Core.Render;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class HatchRenderingTests
	{
		private readonly struct Segment
		{
			public double X1 { get; }
			public double Y1 { get; }
			public double X2 { get; }
			public double Y2 { get; }

			public double MidX => (this.X1 + this.X2) * 0.5;
			public double MidY => (this.Y1 + this.Y2) * 0.5;

			public Segment(double x1, double y1, double x2, double y2)
			{
				this.X1 = x1;
				this.Y1 = y1;
				this.X2 = x2;
				this.Y2 = y2;
			}
		}

		[Fact]
		public void SolidHatch_Rectangle_RendersFilledPath()
		{
			Hatch hatch = createBaseHatch();
			hatch.Paths.Add(createRectanglePath(0, 0, 100, 100, BoundaryPathFlags.External));

			string content = renderEntity(hatch, out RenderLog log);

			Assert.Contains("0 0 m", content);
			Assert.Contains("100 0 l", content);
			Assert.Contains("\nF\n", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error || e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void SolidHatch_StylesRespectIslandDepth()
		{
			Hatch normal = createNestedSolidHatch(HatchStyleType.Normal);
			Hatch outer = createNestedSolidHatch(HatchStyleType.Outer);
			Hatch ignore = createNestedSolidHatch(HatchStyleType.Ignore);

			string normalContent = renderEntity(normal, out _);
			string outerContent = renderEntity(outer, out _);
			string ignoreContent = renderEntity(ignore, out _);

			Assert.Equal(3, countClosePathOps(normalContent));
			Assert.Equal(2, countClosePathOps(outerContent));
			Assert.Equal(1, countClosePathOps(ignoreContent));
		}

		[Fact]
		public void SolidHatch_Hole_EmitsOppositeWindingForNonZeroFill()
		{
			Hatch hatch = createBaseHatch();
			hatch.Style = HatchStyleType.Normal;
			hatch.Paths.Add(createRectanglePath(0, 0, 100, 100, BoundaryPathFlags.External));
			hatch.Paths.Add(createRectanglePath(25, 25, 75, 75, BoundaryPathFlags.Outermost));

			string content = renderEntity(hatch, out _);
			var subpaths = parseClosedSubpaths(content);

			Assert.True(subpaths.Count >= 2);
			double a0 = signedArea(subpaths[0]);
			double a1 = signedArea(subpaths[1]);

			Assert.True(a0 > 0.0, $"Expected outer path CCW (area>0), got {a0}");
			Assert.True(a1 < 0.0, $"Expected hole path CW (area<0), got {a1}");
		}

		[Fact]
		public void PatternHatch_Ansi31ByName_RendersStrokedSegments()
		{
			Hatch hatch = createBaseHatch();
			hatch.IsSolid = false;
			hatch.PatternType = HatchPatternType.PatternFill;
			hatch.Pattern = new HatchPattern("ANSI31");
			hatch.PatternScale = 10.0;
			hatch.Paths.Add(createRectanglePath(0, 0, 100, 100, BoundaryPathFlags.External));

			string content = renderEntity(hatch, out RenderLog log);
			List<Segment> segments = parseSegments(content);

			Assert.Contains("\nS\n", content);
			Assert.DoesNotContain("\nF\n", content);
			Assert.True(segments.Count > 10);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error || e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void PatternHatch_UnknownPattern_FallsBackToSolidFill()
		{
			Hatch hatch = createBaseHatch();
			hatch.IsSolid = false;
			hatch.PatternType = HatchPatternType.PatternFill;
			hatch.Pattern = new HatchPattern("NOT-A-PATTERN");
			hatch.Paths.Add(createRectanglePath(0, 0, 50, 50, BoundaryPathFlags.External));

			string content = renderEntity(hatch, out RenderLog log);

			Assert.Contains("\nF\n", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error);
		}

		[Fact]
		public void PatternHatch_NormalStyle_DoesNotDrawInsideIsland()
		{
			Hatch hatch = createStripedHatch(HatchStyleType.Normal);

			string content = renderEntity(hatch, out _);
			List<Segment> segments = parseSegments(content);

			Assert.NotEmpty(segments);
			Assert.DoesNotContain(segments, s => insideRect(s.MidX, s.MidY, 30, 30, 70, 70));
		}

		[Fact]
		public void PatternHatch_IgnoreStyle_DrawsInsideIsland()
		{
			Hatch hatch = createStripedHatch(HatchStyleType.Ignore);

			string content = renderEntity(hatch, out _);
			List<Segment> segments = parseSegments(content);

			Assert.Contains(segments, s => insideRect(s.MidX, s.MidY, 30, 30, 70, 70));
		}

		[Fact]
		public void PatternHatch_PatternAngle_RotatesLinesOnce()
		{
			Hatch hatch = createBaseHatch();
			hatch.IsSolid = false;
			hatch.PatternType = HatchPatternType.PatternFill;
			hatch.Style = HatchStyleType.Ignore;
			hatch.Pattern = new HatchPattern("ANGLE");
			hatch.Pattern.Lines.Add(new HatchPattern.Line
			{
				Angle = 0.0,
				BasePoint = XY.Zero,
				Offset = new XY(0, 10),
			});

			// ACadSharp mutates pattern geometry when setting PatternAngle.
			hatch.PatternAngle = Math.PI / 2;
			hatch.Paths.Add(createRectanglePath(0, 0, 100, 100, BoundaryPathFlags.External));

			string content = renderEntity(hatch, out _);
			List<Segment> segments = parseSegments(content);

			Assert.NotEmpty(segments);
			int vertical = segments.Count(s => isVertical(s));
			Assert.True(vertical > (segments.Count * 0.8));
		}

		[Fact]
		public void PatternHatch_PatternScale_ScalesSpacingOnce()
		{
			Hatch hatch = createBaseHatch();
			hatch.IsSolid = false;
			hatch.PatternType = HatchPatternType.PatternFill;
			hatch.Style = HatchStyleType.Ignore;
			hatch.Pattern = new HatchPattern("SCALE");
			hatch.Pattern.Lines.Add(new HatchPattern.Line
			{
				Angle = 0.0,
				BasePoint = XY.Zero,
				Offset = new XY(0, 10),
			});

			// ACadSharp mutates pattern geometry when setting PatternScale.
			hatch.PatternScale = 2.0;
			hatch.Paths.Add(createRectanglePath(0, 0, 100, 100, BoundaryPathFlags.External));

			string content = renderEntity(hatch, out _);
			List<Segment> segments = parseSegments(content);

			var ys = segments
				.Where(isHorizontal)
				.Select(s => Math.Round(s.MidY, 3))
				.Distinct()
				.OrderBy(y => y)
				.ToList();

			Assert.True(ys.Count >= 3);
			double minDelta = double.MaxValue;
			for (int i = 1; i < ys.Count; i++)
			{
				double d = ys[i] - ys[i - 1];
				if (d > 1e-3)
				{
					minDelta = Math.Min(minDelta, d);
				}
			}

			AssertClose(20.0, minDelta, 1e-2);
		}

		[Fact]
		public void PatternHatch_ConcaveBoundary_ClipsSegmentsInside()
		{
			// L-shaped polygon.
			var poly = new List<XY>
			{
				new XY(0, 0),
				new XY(100, 0),
				new XY(100, 40),
				new XY(40, 40),
				new XY(40, 100),
				new XY(0, 100),
			};

			Hatch hatch = createBaseHatch();
			hatch.IsSolid = false;
			hatch.PatternType = HatchPatternType.PatternFill;
			hatch.Style = HatchStyleType.Ignore;
			hatch.Pattern = new HatchPattern("CONCAVE");
			hatch.Pattern.Lines.Add(new HatchPattern.Line
			{
				Angle = 0.0,
				BasePoint = XY.Zero,
				Offset = new XY(0, 10),
			});
			hatch.Paths.Add(createPolygonPath(poly, BoundaryPathFlags.External));

			string content = renderEntity(hatch, out _);
			List<Segment> segments = parseSegments(content);

			Assert.NotEmpty(segments);
			foreach (var seg in segments)
			{
				Assert.True(pointInPolygonInclusive(new XY(seg.MidX, seg.MidY), poly), $"Segment midpoint outside polygon: ({seg.MidX},{seg.MidY})");
			}
		}

		[Fact]
		public void GradientHatch_FallsBackToSolidFill()
		{
			Hatch hatch = createBaseHatch();
			hatch.IsSolid = false;
			hatch.PatternType = HatchPatternType.PatternFill;
			hatch.Pattern = new HatchPattern("ANSI31");
			hatch.Paths.Add(createRectanglePath(0, 0, 50, 50, BoundaryPathFlags.External));
			hatch.GradientColor.Enabled = true;
			hatch.GradientColor.Colors.Add(new GradientColor
			{
				Value = 0.0,
				Color = Color.Green,
			});

			string content = renderEntity(hatch, out RenderLog log);

			Assert.Contains("\nF\n", content);
			Assert.Contains(log.Entries, e =>
				e.Status == RenderStatus.Rendered
				&& e.Reason.IndexOf("Gradient HATCH approximated as solid fill", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		[Fact]
		public void BoundaryPolyline_WithBulge_DoesNotThrowAndTessellates()
		{
			Hatch hatch = createBaseHatch();
			hatch.IsSolid = true;
			hatch.PatternType = HatchPatternType.SolidFill;

			var path = new Hatch.BoundaryPath();
			path.Flags = BoundaryPathFlags.External | BoundaryPathFlags.Polyline;
			path.Edges.Add(new Hatch.BoundaryPath.Polyline(
				new[]
				{
					new XYZ(0, 0, 0.5),   // bulge on segment (0,0)->(100,0)
					new XYZ(100, 0, 0.0),
					new XYZ(100, 100, 0.0),
					new XYZ(0, 100, 0.0),
				},
				isClosed: true));
			hatch.Paths.Add(path);

			string content = renderEntity(hatch, out RenderLog log);
			int lineOps = Regex.Matches(content, @"\sl\s").Count;

			Assert.Contains("\nF\n", content);
			Assert.True(lineOps > 10);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error);
		}

		[Fact]
		public void BoundaryEdgePath_WithArc_DoesNotThrowAndRenders()
		{
			Hatch hatch = createBaseHatch();
			hatch.IsSolid = true;
			hatch.PatternType = HatchPatternType.SolidFill;

			var path = new Hatch.BoundaryPath();
			path.Flags = BoundaryPathFlags.External;
			path.Edges.Add(new Hatch.BoundaryPath.Arc
			{
				Center = XY.Zero,
				Radius = 50.0,
				StartAngle = 0.0,
				EndAngle = Math.PI * 2.0,
				CounterClockWise = true,
			});
			hatch.Paths.Add(path);

			string content = renderEntity(hatch, out RenderLog log);
			int lineOps = Regex.Matches(content, @"\sl\s").Count;

			Assert.Contains("\nF\n", content);
			Assert.True(lineOps > 20);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error);
		}

		[Fact]
		public void HatchInInsert_AppliesParentTransform()
		{
			Hatch hatch = createBaseHatch();
			hatch.Paths.Add(createRectanglePath(0, 0, 10, 10, BoundaryPathFlags.External));

			var block = new BlockRecord("HATCH-BLOCK");
			block.Entities.Add(hatch);

			var insert = new Insert(block)
			{
				InsertPoint = new XYZ(100, 50, 0),
				XScale = 2.0,
				YScale = 2.0,
				ZScale = 1.0,
			};

			string content = renderEntity(insert, out _);

			Assert.Contains("100 50", content);
			Assert.Contains("120 70", content);
		}

		[Fact]
		public void HatchInInsert_WithRotation_DoesNotThrow()
		{
			Hatch hatch = createBaseHatch();
			hatch.Paths.Add(createRectanglePath(0, 0, 10, 20, BoundaryPathFlags.External));

			var block = new BlockRecord("HATCH-ROT-BLOCK");
			block.Entities.Add(hatch);

			var insert = new Insert(block)
			{
				InsertPoint = new XYZ(100, 50, 0),
				Rotation = Math.PI / 2,
				XScale = 1.0,
				YScale = 1.0,
				ZScale = 1.0,
			};

			string content = renderEntity(insert, out RenderLog log);

			Assert.Contains("\nF\n", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error);
		}

		[Fact]
		public void HatchWithoutBoundaries_IsSkipped()
		{
			Hatch hatch = createBaseHatch();

			string content = renderEntity(hatch, out RenderLog log);

			Assert.DoesNotContain("\nF\n", content);
			Assert.Contains(log.Entries, e =>
				e.Status == RenderStatus.Skipped
				&& e.Reason.IndexOf("no valid boundaries", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		private static Hatch createBaseHatch()
		{
			return new Hatch
			{
				Layer = new Layer("0") { Color = Color.Red },
				Color = Color.ByLayer,
				Normal = XYZ.AxisZ,
				Elevation = 0.0,
				Pattern = HatchPattern.Solid,
				IsSolid = true,
				PatternType = HatchPatternType.SolidFill,
				Style = HatchStyleType.Normal,
			};
		}

		private static Hatch createNestedSolidHatch(HatchStyleType style)
		{
			Hatch hatch = createBaseHatch();
			hatch.Style = style;
			hatch.Paths.Add(createRectanglePath(0, 0, 100, 100, BoundaryPathFlags.External));
			hatch.Paths.Add(createRectanglePath(20, 20, 80, 80, BoundaryPathFlags.Outermost));
			hatch.Paths.Add(createRectanglePath(40, 40, 60, 60, BoundaryPathFlags.Default));
			return hatch;
		}

		private static Hatch createStripedHatch(HatchStyleType style)
		{
			Hatch hatch = createBaseHatch();
			hatch.IsSolid = false;
			hatch.PatternType = HatchPatternType.PatternFill;
			hatch.Style = style;
			hatch.Pattern = new HatchPattern("TEST");
			hatch.Pattern.Lines.Add(new HatchPattern.Line
			{
				Angle = 0.0,
				BasePoint = XY.Zero,
				Offset = new XY(0, 10),
			});

			hatch.Paths.Add(createRectanglePath(0, 0, 100, 100, BoundaryPathFlags.External));
			hatch.Paths.Add(createRectanglePath(30, 30, 70, 70, BoundaryPathFlags.Outermost));
			return hatch;
		}

		private static Hatch.BoundaryPath createRectanglePath(double minX, double minY, double maxX, double maxY, BoundaryPathFlags flags)
		{
			var path = new Hatch.BoundaryPath();
			path.Flags = flags | BoundaryPathFlags.Polyline;
			path.Edges.Add(new Hatch.BoundaryPath.Polyline(
				new[]
				{
					new XYZ(minX, minY, 0),
					new XYZ(maxX, minY, 0),
					new XYZ(maxX, maxY, 0),
					new XYZ(minX, maxY, 0),
				},
				isClosed: true));
			return path;
		}

		private static Hatch.BoundaryPath createPolygonPath(IReadOnlyList<XY> points, BoundaryPathFlags flags)
		{
			var path = new Hatch.BoundaryPath();
			path.Flags = flags | BoundaryPathFlags.Polyline;
			path.Edges.Add(new Hatch.BoundaryPath.Polyline(points.Select(p => new XYZ(p.X, p.Y, 0.0)), isClosed: true));
			return path;
		}

		private static string renderEntity(Entity entity, out RenderLog log)
		{
			var pdf = new PdfDocument();
			var page = pdf.Pages.AddPage();
			page.Layout = new Layout("L")
			{
				PaperUnits = PlotPaperUnits.Pixels,
				DenominatorScale = 1.0,
				PaperWidth = 500,
				PaperHeight = 500,
			};
			page.Entities.Add(entity);

			var cfg = new PdfConfiguration
			{
				UseSceneGraph = true,
				DecimalFormat = "0.####",
			};

			string content = page.Contents.GetPdfForm(cfg);
			log = cfg.LastRenderLog;
			return content;
		}

		private static int countClosePathOps(string content)
		{
			return Regex.Matches(content, @"\nh\n").Count;
		}

		private static List<List<XY>> parseClosedSubpaths(string content)
		{
			var result = new List<List<XY>>();
			List<XY> current = null;
			string[] lines = content.Split('\n');
			foreach (string raw in lines)
			{
				string line = raw.Trim();
				if (line.EndsWith(" m", StringComparison.Ordinal))
				{
					Match m = Regex.Match(line, @"^(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) m$");
					if (m.Success)
					{
						current = new List<XY>();
						current.Add(new XY(parse(m.Groups[1].Value), parse(m.Groups[2].Value)));
					}
				}
				else if (line.EndsWith(" l", StringComparison.Ordinal) && current != null)
				{
					Match m = Regex.Match(line, @"^(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) l$");
					if (m.Success)
					{
						current.Add(new XY(parse(m.Groups[1].Value), parse(m.Groups[2].Value)));
					}
				}
				else if (line == "h" && current != null)
				{
					result.Add(current);
					current = null;
				}
			}

			return result;
		}

		private static double signedArea(IReadOnlyList<XY> polygon)
		{
			double area = 0.0;
			for (int i = 0; i < polygon.Count; i++)
			{
				XY a = polygon[i];
				XY b = polygon[(i + 1) % polygon.Count];
				area += a.X * b.Y - b.X * a.Y;
			}
			return area * 0.5;
		}

		private static List<Segment> parseSegments(string content)
		{
			var segments = new List<Segment>();
			MatchCollection matches = Regex.Matches(
				content,
				@"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) m\s+(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) l");

			foreach (Match match in matches.Cast<Match>())
			{
				segments.Add(new Segment(
					parse(match.Groups[1].Value),
					parse(match.Groups[2].Value),
					parse(match.Groups[3].Value),
					parse(match.Groups[4].Value)));
			}

			return segments;
		}

		private static bool insideRect(double x, double y, double minX, double minY, double maxX, double maxY)
		{
			return x > minX && x < maxX && y > minY && y < maxY;
		}

		private static bool isHorizontal(Segment s)
		{
			return Math.Abs(s.Y2 - s.Y1) < 1e-3 && Math.Abs(s.X2 - s.X1) > 1e-3;
		}

		private static bool isVertical(Segment s)
		{
			return Math.Abs(s.X2 - s.X1) < 1e-3 && Math.Abs(s.Y2 - s.Y1) > 1e-3;
		}

		private static void AssertClose(double expected, double actual, double tolerance)
		{
			Assert.True(Math.Abs(actual - expected) <= tolerance, $"Expected {expected} +/- {tolerance}, got {actual}");
		}

		private static bool pointInPolygonInclusive(XY point, IReadOnlyList<XY> polygon)
		{
			bool inside = false;
			for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
			{
				XY a = polygon[j];
				XY b = polygon[i];

				if (pointOnSegment(point, a, b))
				{
					return true;
				}

				bool intersect = ((b.Y > point.Y) != (a.Y > point.Y))
					&& (point.X < (a.X - b.X) * (point.Y - b.Y) / (a.Y - b.Y) + b.X);
				if (intersect)
				{
					inside = !inside;
				}
			}

			return inside;
		}

		private static bool pointOnSegment(XY p, XY a, XY b)
		{
			double cross = (p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X);
			if (Math.Abs(cross) > 1e-7)
			{
				return false;
			}

			double dot = (p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y);
			if (dot < -1e-7)
			{
				return false;
			}

			double len2 = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
			if (dot - len2 > 1e-7)
			{
				return false;
			}

			return true;
		}

		private static double parse(string value)
		{
			return double.Parse(value, CultureInfo.InvariantCulture);
		}
	}
}
