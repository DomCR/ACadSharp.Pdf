using ACadSharp.Entities;
using ACadSharp.Pdf.Core.Render.Transforms;
using Clipper2Lib;
using CSMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACadSharp.Pdf.Core.Render.SceneGraph
{
	internal sealed class HatchPatternGenerator
	{
		private const double Epsilon = 1e-9;
		private const double AreaEpsilon = 1e-9;
		private const int MaxPatternLinesPerFamily = 6000;
		private const int MaxVisiblePatternSegments = 200000;
		private const int MaxDashIntervalsPerLine = 2000;
		private const int MaxOpenSubjectsPerClipBatch = 5000;

		private readonly PdfConfiguration _configuration;
		private readonly RenderLog _log;

		private sealed class BoundaryLoop
		{
			public IReadOnlyList<XY> Points { get; }
			public double SignedArea { get; }
			public double AbsArea { get; }
			public BoundaryPathFlags Flags { get; }
			public int ParentIndex { get; set; } = -1;
			public int Depth { get; set; } = 0;

			public BoundaryLoop(IReadOnlyList<XY> points, double signedArea, BoundaryPathFlags flags)
			{
				this.Points = points;
				this.SignedArea = signedArea;
				this.AbsArea = Math.Abs(signedArea);
				this.Flags = flags;
			}
		}

		private sealed class PatternLineFamily
		{
			public double Angle { get; }
			public XY BasePoint { get; }
			public XY Offset { get; }
			public IReadOnlyList<double> Dashes { get; }

			public PatternLineFamily(double angle, XY basePoint, XY offset, IReadOnlyList<double> dashes)
			{
				this.Angle = angle;
				this.BasePoint = basePoint;
				this.Offset = offset;
				this.Dashes = dashes ?? Array.Empty<double>();
			}
		}

		private readonly struct LineSegment
		{
			public XY Start { get; }
			public XY End { get; }

			public LineSegment(XY start, XY end)
			{
				this.Start = start;
				this.End = end;
			}
		}

		public HatchPatternGenerator(PdfConfiguration configuration, RenderLog log)
		{
			this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
			this._log = log ?? throw new ArgumentNullException(nameof(log));
		}

		public IReadOnlyList<RenderNode> Render(Hatch hatch, StrokeStyle style)
		{
			if (hatch == null) throw new ArgumentNullException(nameof(hatch));
			if (style == null) throw new ArgumentNullException(nameof(style));

			List<BoundaryLoop> loops = extractBoundaryLoops(hatch);
			if (loops.Count == 0)
			{
				this._log.Add(hatch.Handle, hatch.SubclassMarker, RenderStatus.Skipped, "HATCH has no valid boundaries.");
				return Array.Empty<RenderNode>();
			}

			assignLoopDepths(loops);

			if (isGradient(hatch))
			{
				ACadSharp.Color fillColor = resolveGradientPrimaryColor(hatch, style.Color);
				this._log.Add(hatch.Handle, hatch.SubclassMarker, RenderStatus.Rendered, "Gradient HATCH approximated as solid fill.");
				return renderSolidFill(hatch, loops, fillColor);
			}

			if (isSolid(hatch))
			{
				return renderSolidFill(hatch, loops, style.Color);
			}

			return renderPatternFill(hatch, loops, style);
		}

		private IReadOnlyList<RenderNode> renderSolidFill(Hatch hatch, IReadOnlyList<BoundaryLoop> loops, ACadSharp.Color fillColor)
		{
			var ordered = loops
				.OrderBy(loop => loop.Depth)
				.ThenByDescending(loop => loop.AbsArea)
				.ToList();

			Matrix4 ocsToWcs = TransformHelper.OcsToWcs(safeNormal(hatch.Normal));
			var segments = new List<PathSegment>(ordered.Count * 64);

			foreach (BoundaryLoop loop in ordered)
			{
				int contribution = getLoopContribution(hatch.Style, loop.Depth);
				if (contribution == 0)
				{
					continue;
				}

				bool ccw = contribution > 0;
				List<XY> winding = ensureWinding(loop.Points, ccw);
				if (winding.Count < 3)
				{
					continue;
				}

				segments.Add(new MoveTo(toWorldPoint(winding[0], ocsToWcs, hatch.Elevation)));
				for (int i = 1; i < winding.Count; i++)
				{
					segments.Add(new LineTo(toWorldPoint(winding[i], ocsToWcs, hatch.Elevation)));
				}
				segments.Add(new ClosePath());
			}

			if (segments.Count == 0)
			{
				this._log.Add(hatch.Handle, hatch.SubclassMarker, RenderStatus.Skipped, "HATCH produced no visible solid-fill geometry.");
				return Array.Empty<RenderNode>();
			}

			this._log.Add(hatch.Handle, hatch.SubclassMarker, RenderStatus.Rendered, "Rendered as solid HATCH.");
			return new RenderNode[]
			{
				new PathNode(hatch.Handle, segments, stroke: null, fill: new FillStyle(fillColor))
			};
		}

		private IReadOnlyList<RenderNode> renderPatternFill(Hatch hatch, IReadOnlyList<BoundaryLoop> loops, StrokeStyle style)
		{
			IReadOnlyList<PatternLineFamily> families = resolvePatternFamilies(hatch);
			if (families.Count == 0)
			{
				this._log.Add(hatch.Handle, hatch.SubclassMarker, RenderStatus.Rendered, $"Pattern '{hatch.Pattern?.Name}' not found; falling back to solid fill.");
				return renderSolidFill(hatch, loops, style.Color);
			}

			if (!tryGetBounds(loops, out double minX, out double minY, out double maxX, out double maxY))
			{
				this._log.Add(hatch.Handle, hatch.SubclassMarker, RenderStatus.Skipped, "Invalid HATCH boundary bounds.");
				return Array.Empty<RenderNode>();
			}

			if (!tryBuildClipPaths(loops, hatch.Style, out PathsD clipPaths, out FillRule fillRule))
			{
				this._log.Add(hatch.Handle, hatch.SubclassMarker, RenderStatus.Skipped, "Unable to build HATCH clip region.");
				return Array.Empty<RenderNode>();
			}

			Matrix4 ocsToWcs = TransformHelper.OcsToWcs(safeNormal(hatch.Normal));
			XY[] corners = new[]
			{
				new XY(minX, minY),
				new XY(maxX, minY),
				new XY(maxX, maxY),
				new XY(minX, maxY),
			};

			double diagonal = distance(new XY(minX, minY), new XY(maxX, maxY));
			double margin = Math.Max(diagonal, 1.0);

			var outputSegments = new List<PathSegment>(4096);
			int visibleSegments = 0;
			bool truncated = false;

			foreach (PatternLineFamily family in families)
			{
				XY direction = new XY(Math.Cos(family.Angle), Math.Sin(family.Angle));
				if (!tryNormalize(direction, out direction))
				{
					continue;
				}

				XY normal = perpendicularLeft(direction);
				double spacing = dot(family.Offset, normal);
				double shift = dot(family.Offset, direction);

				getProjectionRange(corners, normal, out double minN, out double maxN);
				getProjectionRange(corners, direction, out double minT, out double maxT);

				int lineStart;
				int lineEnd;

				if (Math.Abs(spacing) < Epsilon)
				{
					lineStart = 0;
					lineEnd = 0;
				}
				else
				{
					double baseN = dot(family.BasePoint, normal);
					double i0 = (minN - baseN) / spacing;
					double i1 = (maxN - baseN) / spacing;
					lineStart = (int)Math.Floor(Math.Min(i0, i1)) - 1;
					lineEnd = (int)Math.Ceiling(Math.Max(i0, i1)) + 1;
				}

				int count = lineEnd - lineStart + 1;
				if (count > MaxPatternLinesPerFamily)
				{
					int trim = count - MaxPatternLinesPerFamily;
					lineStart += trim / 2;
					lineEnd -= trim - (trim / 2);
					truncated = true;
				}

				for (int i = lineStart; i <= lineEnd; i++)
				{
					XY origin = family.BasePoint + family.Offset * i;
					XY start = origin + direction * (minT - margin);
					XY end = origin + direction * (maxT + margin);
					double dashPhase = shift * i;

					List<LineSegment> dashed = applyDashPattern(
						start,
						end,
						origin,
						direction,
						family.Dashes,
						dashPhase);

					foreach (LineSegment segment in clipOpenSegmentsToClipRegion(dashed, clipPaths, fillRule))
					{
						if (distance(segment.Start, segment.End) <= Epsilon)
						{
							continue;
						}

						outputSegments.Add(new MoveTo(toWorldPoint(segment.Start, ocsToWcs, hatch.Elevation)));
						outputSegments.Add(new LineTo(toWorldPoint(segment.End, ocsToWcs, hatch.Elevation)));
						visibleSegments++;

						if (visibleSegments >= MaxVisiblePatternSegments)
						{
							truncated = true;
							break;
						}
					}

					if (visibleSegments >= MaxVisiblePatternSegments)
					{
						break;
					}
				}

				if (visibleSegments >= MaxVisiblePatternSegments)
				{
					break;
				}
			}

			if (outputSegments.Count == 0)
			{
				this._log.Add(hatch.Handle, hatch.SubclassMarker, RenderStatus.Skipped, "Pattern HATCH produced no visible segments.");
				return Array.Empty<RenderNode>();
			}

			if (truncated)
			{
				this._log.Add(hatch.Handle, hatch.SubclassMarker, RenderStatus.Rendered, "Pattern HATCH output truncated to safe limits.");
			}
			else
			{
				this._log.Add(hatch.Handle, hatch.SubclassMarker, RenderStatus.Rendered, "Rendered as pattern HATCH.");
			}

			var stroke = new StrokeStyle(
				style.Color,
				Math.Max(style.WidthPt, 0.01),
				Array.Empty<double>(),
				0.0);

			return new RenderNode[]
			{
				new PathNode(hatch.Handle, outputSegments, stroke, fill: null)
			};
		}

		private static bool tryBuildClipPaths(IReadOnlyList<BoundaryLoop> loops, HatchStyleType style, out PathsD clipPaths, out FillRule fillRule)
		{
			clipPaths = new PathsD();
			fillRule = FillRule.EvenOdd;

			if (loops == null || loops.Count == 0)
			{
				return false;
			}

			PathsD all = new PathsD();
			foreach (BoundaryLoop loop in loops)
			{
				if (loop?.Points == null || loop.Points.Count < 3)
				{
					continue;
				}

				all.Add(toPathD(loop.Points));
			}

			switch (style)
			{
				case HatchStyleType.Ignore:
					{
						PathsD exteriors = new PathsD();
						foreach (BoundaryLoop loop in loops)
						{
							if (loop != null && loop.Depth == 0 && loop.Points != null && loop.Points.Count >= 3)
							{
								exteriors.Add(toPathD(loop.Points));
							}
						}

						if (exteriors.Count == 0)
						{
							return false;
						}

						clipPaths = exteriors;
						return true;
					}
				case HatchStyleType.Outer:
					{
						PathsD exteriors = new PathsD();
						PathsD islands = new PathsD();
						foreach (BoundaryLoop loop in loops)
						{
							if (loop == null || loop.Points == null || loop.Points.Count < 3)
							{
								continue;
							}

							if (loop.Depth == 0)
							{
								exteriors.Add(toPathD(loop.Points));
							}
							else if (loop.Depth == 1)
							{
								islands.Add(toPathD(loop.Points));
							}
						}

						if (exteriors.Count == 0)
						{
							return false;
						}

						if (islands.Count == 0)
						{
							clipPaths = exteriors;
							return true;
						}

						var c = new ClipperD();
						c.AddSubject(exteriors);
						c.AddClip(islands);
						var solution = new PathsD();
						c.Execute(Clipper2Lib.ClipType.Difference, FillRule.EvenOdd, solution);
						clipPaths = solution;
						return clipPaths.Count > 0;
					}
				case HatchStyleType.Normal:
				default:
					clipPaths = all;
					return clipPaths.Count > 0;
			}
		}

		private static IEnumerable<LineSegment> clipOpenSegmentsToClipRegion(IReadOnlyList<LineSegment> subjects, PathsD clipPaths, FillRule fillRule)
		{
			if (subjects == null || subjects.Count == 0 || clipPaths == null || clipPaths.Count == 0)
			{
				yield break;
			}

			int index = 0;
			while (index < subjects.Count)
			{
				int take = Math.Min(MaxOpenSubjectsPerClipBatch, subjects.Count - index);
				var clipper = new ClipperD();
				clipper.AddClip(clipPaths);

				for (int i = 0; i < take; i++)
				{
					LineSegment seg = subjects[index + i];
					var path = new PathD
					{
						new PointD(seg.Start.X, seg.Start.Y),
						new PointD(seg.End.X, seg.End.Y),
					};
					clipper.AddOpenSubject(path);
				}

				var closedSolution = new PathsD();
				var openSolution = new PathsD();
				clipper.Execute(Clipper2Lib.ClipType.Intersection, fillRule, closedSolution, openSolution);

				foreach (PathD path in openSolution)
				{
					if (path == null || path.Count < 2)
					{
						continue;
					}

					for (int i = 1; i < path.Count; i++)
					{
						PointD a = path[i - 1];
						PointD b = path[i];
						yield return new LineSegment(new XY(a.x, a.y), new XY(b.x, b.y));
					}
				}

				index += take;
			}
		}

		private static PathD toPathD(IReadOnlyList<XY> points)
		{
			var path = new PathD(points.Count);
			for (int i = 0; i < points.Count; i++)
			{
				XY p = points[i];
				path.Add(new PointD(p.X, p.Y));
			}
			return path;
		}

		private List<BoundaryLoop> extractBoundaryLoops(Hatch hatch)
		{
			var loops = new List<BoundaryLoop>();
			if (hatch.Paths == null || hatch.Paths.Count == 0)
			{
				return loops;
			}

			int precision = Math.Max(8, (int)this._configuration.ArcPrecision);

			foreach (Hatch.BoundaryPath path in hatch.Paths)
			{
				if (path == null)
				{
					continue;
				}

				List<XY> points = new List<XY>();
				foreach (XYZ p in path.GetPoints(precision))
				{
					if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y))
					{
						continue;
					}

					points.Add(new XY(p.X, p.Y));
				}

				List<XY> cleaned = sanitizePolygon(points);
				if (cleaned.Count < 3)
				{
					continue;
				}

				double area = signedArea(cleaned);
				if (Math.Abs(area) <= AreaEpsilon)
				{
					continue;
				}

				loops.Add(new BoundaryLoop(cleaned, area, path.Flags));
			}

			return loops;
		}

		private static List<XY> sanitizePolygon(IReadOnlyList<XY> points)
		{
			var cleaned = new List<XY>(points?.Count ?? 0);
			if (points == null || points.Count == 0)
			{
				return cleaned;
			}

			for (int i = 0; i < points.Count; i++)
			{
				XY p = points[i];
				if (cleaned.Count == 0 || distance(cleaned[cleaned.Count - 1], p) > Epsilon)
				{
					cleaned.Add(p);
				}
			}

			if (cleaned.Count > 1 && distance(cleaned[0], cleaned[cleaned.Count - 1]) <= Epsilon)
			{
				cleaned.RemoveAt(cleaned.Count - 1);
			}

			return cleaned;
		}

		private static void assignLoopDepths(IReadOnlyList<BoundaryLoop> loops)
		{
			var order = loops
				.Select((loop, index) => new { loop, index })
				.OrderByDescending(item => item.loop.AbsArea)
				.Select(item => item.index)
				.ToArray();

			for (int oi = 0; oi < order.Length; oi++)
			{
				int idx = order[oi];
				BoundaryLoop loop = loops[idx];
				XY probe = getProbePoint(loop.Points);

				int parent = -1;
				double parentArea = double.MaxValue;

				for (int oj = 0; oj < oi; oj++)
				{
					int candidateIndex = order[oj];
					BoundaryLoop candidate = loops[candidateIndex];
					if (candidate.AbsArea >= parentArea)
					{
						continue;
					}

					if (!pointInPolygonInclusive(probe, candidate.Points))
					{
						continue;
					}

					parent = candidateIndex;
					parentArea = candidate.AbsArea;
				}

				loop.ParentIndex = parent;
				loop.Depth = parent < 0 ? 0 : loops[parent].Depth + 1;
			}
		}

		private static XY getProbePoint(IReadOnlyList<XY> polygon)
		{
			if (polygon == null || polygon.Count == 0)
			{
				return XY.Zero;
			}

			if (tryCentroid(polygon, out XY centroid) && pointInPolygonInclusive(centroid, polygon))
			{
				return centroid;
			}

			XY average = XY.Zero;
			for (int i = 0; i < polygon.Count; i++)
			{
				average += polygon[i];
			}
			average /= polygon.Count;
			if (pointInPolygonInclusive(average, polygon))
			{
				return average;
			}

			return polygon[0];
		}

		private static bool tryCentroid(IReadOnlyList<XY> polygon, out XY centroid)
		{
			double a = 0.0;
			double cx = 0.0;
			double cy = 0.0;

			for (int i = 0; i < polygon.Count; i++)
			{
				XY p0 = polygon[i];
				XY p1 = polygon[(i + 1) % polygon.Count];
				double cross = p0.X * p1.Y - p1.X * p0.Y;
				a += cross;
				cx += (p0.X + p1.X) * cross;
				cy += (p0.Y + p1.Y) * cross;
			}

			if (Math.Abs(a) <= AreaEpsilon)
			{
				centroid = XY.Zero;
				return false;
			}

			centroid = new XY(cx / (3.0 * a), cy / (3.0 * a));
			return true;
		}

		private static IReadOnlyList<PatternLineFamily> resolvePatternFamilies(Hatch hatch)
		{
			IReadOnlyList<HatchPattern.Line> source = hatch.Pattern?.Lines;
			bool sourceIsFinal = source != null && source.Count > 0;
			if (!sourceIsFinal)
			{
				source = getStandardPatternLines(hatch.Pattern?.Name);
			}

			if (source == null || source.Count == 0)
			{
				return Array.Empty<PatternLineFamily>();
			}

			// ACadSharp mutates HatchPattern geometry in the Hatch.PatternAngle/PatternScale setters (and on transforms)
			// by calling HatchPattern.Update(). When pattern lines are present, treat them as already transformed.
			double scale = sourceIsFinal ? 1.0 : hatch.PatternScale;
			if (scale <= Epsilon)
			{
				scale = 1.0;
			}

			double rotation = sourceIsFinal ? 0.0 : hatch.PatternAngle;
			var families = new List<PatternLineFamily>(source.Count);

			foreach (HatchPattern.Line line in source)
			{
				if (line == null)
				{
					continue;
				}

				XY basePoint = rotate(new XY(line.BasePoint.X * scale, line.BasePoint.Y * scale), rotation);
				XY offset = rotate(new XY(line.Offset.X * scale, line.Offset.Y * scale), rotation);
				var dashes = new List<double>();
				if (line.DashLengths != null && line.DashLengths.Count > 0)
				{
					for (int i = 0; i < line.DashLengths.Count; i++)
					{
						dashes.Add(line.DashLengths[i] * scale);
					}
				}

				families.Add(new PatternLineFamily(
					line.Angle + rotation,
					basePoint,
					offset,
					dashes));
			}

			return families;
		}

		private static IReadOnlyList<HatchPattern.Line> getStandardPatternLines(string name)
		{
			switch ((name ?? string.Empty).Trim().ToUpperInvariant())
			{
				case "ANSI31":
					return new[]
					{
						makePatternLine(45.0, 0.0, 0.0, -0.0884, 0.0884),
					};
				case "ANSI32":
					return new[]
					{
						makePatternLine(45.0, 0.0, 0.0, -0.0884, 0.0884),
						makePatternLine(45.0, 0.1768, 0.0, -0.0884, 0.0884),
					};
				case "ANSI33":
					return new[]
					{
						makePatternLine(45.0, 0.0, 0.0, -0.0884, 0.0884),
						makePatternLine(45.0, 0.1768, 0.0, -0.0884, 0.0884),
						makePatternLine(45.0, 0.3536, 0.0, -0.0884, 0.0884),
						makePatternLine(45.0, 0.5304, 0.0, -0.0884, 0.0884),
					};
				case "ANSI34":
					return new[]
					{
						makePatternLine(45.0, 0.0, 0.0, -0.0884, 0.0884),
						makePatternLine(-45.0, 0.0, 0.0, -0.0884, 0.0884),
					};
				case "BRICK":
					return new[]
					{
						makePatternLine(0.0, 0.0, 0.0, 0.0, 0.25),
						makePatternLine(90.0, 0.0, 0.0, 0.25, 0.0, 0.125, -0.125),
						makePatternLine(90.0, 0.125, 0.0, 0.25, 0.0, -0.125, 0.125),
					};
				default:
					return Array.Empty<HatchPattern.Line>();
			}
		}

		private static HatchPattern.Line makePatternLine(double angleDeg, double baseX, double baseY, double offsetX, double offsetY, params double[] dashes)
		{
			var line = new HatchPattern.Line
			{
				Angle = angleDeg * Math.PI / 180.0,
				BasePoint = new XY(baseX, baseY),
				Offset = new XY(offsetX, offsetY),
			};

			if (dashes != null && dashes.Length > 0)
			{
				line.DashLengths.AddRange(dashes);
			}

			return line;
		}

		private static List<LineSegment> applyDashPattern(
			XY start,
			XY end,
			XY origin,
			XY direction,
			IReadOnlyList<double> dashes,
			double phase)
		{
			var segments = new List<LineSegment>();
			double length = distance(start, end);
			if (length <= Epsilon)
			{
				return segments;
			}

			if (dashes == null || dashes.Count == 0)
			{
				segments.Add(new LineSegment(start, end));
				return segments;
			}

			bool hasVisible = dashes.Any(d => d >= 0.0);
			if (!hasVisible)
			{
				return segments;
			}

			double minNonZero = dashes
				.Where(d => Math.Abs(d) > Epsilon)
				.Select(d => Math.Abs(d))
				.DefaultIfEmpty(0.1)
				.Min();
			double dotLength = Math.Max(minNonZero * 0.2, 1e-4);

			var lengths = new double[dashes.Count];
			double cycle = 0.0;
			for (int i = 0; i < dashes.Count; i++)
			{
				double len = Math.Abs(dashes[i]);
				if (len <= Epsilon)
				{
					len = dotLength;
				}

				lengths[i] = len;
				cycle += len;
			}

			if (cycle <= Epsilon)
			{
				segments.Add(new LineSegment(start, end));
				return segments;
			}

			double startS = dot(start - origin, direction);
			double endS = dot(end - origin, direction);
			if (endS < startS)
			{
				double t = startS;
				startS = endS;
				endS = t;
				XY p = start;
				start = end;
				end = p;
			}

			double lineLength = endS - startS;
			if (lineLength <= Epsilon)
			{
				segments.Add(new LineSegment(start, end));
				return segments;
			}

			double offset = positiveMod(startS - phase, cycle);
			int index = 0;
			while (offset > lengths[index] && index < lengths.Length - 1)
			{
				offset -= lengths[index];
				index++;
			}

			double pos = 0.0;
			double remaining = lengths[index] - offset;
			int guard = 0;

			while (pos < lineLength - Epsilon && guard < MaxDashIntervalsPerLine)
			{
				double next = Math.Min(pos + remaining, lineLength);
				bool draw = dashes[index] >= 0.0;
				if (draw && next - pos > Epsilon)
				{
					XY p0 = start + direction * pos;
					XY p1 = start + direction * next;
					segments.Add(new LineSegment(p0, p1));
				}

				pos = next;
				index = (index + 1) % lengths.Length;
				remaining = lengths[index];
				guard++;
			}

			return segments;
		}

		private static bool isPointFilledByStyle(XY point, IReadOnlyList<BoundaryLoop> loops, HatchStyleType style)
		{
			int insideCount = 0;
			for (int i = 0; i < loops.Count; i++)
			{
				if (pointInPolygonInclusive(point, loops[i].Points))
				{
					insideCount++;
				}
			}

			switch (style)
			{
				case HatchStyleType.Outer:
					return insideCount == 1;
				case HatchStyleType.Ignore:
					return insideCount >= 1;
				case HatchStyleType.Normal:
				default:
					return (insideCount % 2) == 1;
			}
		}

		private static int getLoopContribution(HatchStyleType style, int depth)
		{
			switch (style)
			{
				case HatchStyleType.Outer:
					if (depth == 0) return 1;
					if (depth == 1) return -1;
					return 0;
				case HatchStyleType.Ignore:
					return depth == 0 ? 1 : 0;
				case HatchStyleType.Normal:
				default:
					return (depth % 2 == 0) ? 1 : -1;
			}
		}

		private static bool isSolid(Hatch hatch)
		{
			if (hatch.IsSolid || hatch.PatternType == HatchPatternType.SolidFill)
			{
				return true;
			}

			return string.Equals(hatch.Pattern?.Name, "SOLID", StringComparison.OrdinalIgnoreCase);
		}

		private static bool isGradient(Hatch hatch)
		{
			return hatch.GradientColor != null && hatch.GradientColor.Enabled;
		}

		private static ACadSharp.Color resolveGradientPrimaryColor(Hatch hatch, ACadSharp.Color fallback)
		{
			if (hatch.GradientColor?.Colors == null || hatch.GradientColor.Colors.Count == 0)
			{
				return fallback;
			}

			GradientColor color = hatch.GradientColor.Colors
				.OrderBy(c => c.Value)
				.FirstOrDefault();

			if (color == null)
			{
				return fallback;
			}

			return color.Color;
		}

		private static bool tryGetBounds(IReadOnlyList<BoundaryLoop> loops, out double minX, out double minY, out double maxX, out double maxY)
		{
			minX = double.MaxValue;
			minY = double.MaxValue;
			maxX = double.MinValue;
			maxY = double.MinValue;

			for (int i = 0; i < loops.Count; i++)
			{
				IReadOnlyList<XY> pts = loops[i].Points;
				for (int j = 0; j < pts.Count; j++)
				{
					XY p = pts[j];
					minX = Math.Min(minX, p.X);
					minY = Math.Min(minY, p.Y);
					maxX = Math.Max(maxX, p.X);
					maxY = Math.Max(maxY, p.Y);
				}
			}

			return minX < maxX && minY < maxY;
		}

		private static void getProjectionRange(IReadOnlyList<XY> points, XY axis, out double min, out double max)
		{
			min = double.MaxValue;
			max = double.MinValue;
			for (int i = 0; i < points.Count; i++)
			{
				double d = dot(points[i], axis);
				min = Math.Min(min, d);
				max = Math.Max(max, d);
			}
		}

		private static XY toWorldPoint(XY ocs, Matrix4 ocsToWcs, double elevation)
		{
			XYZ world = ocsToWcs * new XYZ(ocs.X, ocs.Y, elevation);
			return new XY(world.X, world.Y);
		}

			private static XYZ safeNormal(XYZ normal)
			{
				double len = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
				if (len <= Epsilon)
				{
					return XYZ.AxisZ;
				}

				return normal.Normalize();
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
			XY ap = p - a;
			XY ab = b - a;
			double c = Math.Abs(cross(ap, ab));
			if (c > 1e-7)
			{
				return false;
			}

			double d = dot(ap, ab);
			if (d < -1e-7)
			{
				return false;
			}

			double ab2 = dot(ab, ab);
			if (d - ab2 > 1e-7)
			{
				return false;
			}

			return true;
		}

		private static List<XY> ensureWinding(IReadOnlyList<XY> points, bool ccw)
		{
			double area = signedArea(points);
			bool currentCcw = area >= 0.0;
			if (currentCcw == ccw)
			{
				return new List<XY>(points);
			}

			var reversed = new List<XY>(points.Count);
			for (int i = points.Count - 1; i >= 0; i--)
			{
				reversed.Add(points[i]);
			}

			return reversed;
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

		private static XY rotate(XY value, double angle)
		{
			if (Math.Abs(angle) <= Epsilon)
			{
				return value;
			}

			double c = Math.Cos(angle);
			double s = Math.Sin(angle);
			return new XY(
				value.X * c - value.Y * s,
				value.X * s + value.Y * c);
		}

		private static bool tryNormalize(XY value, out XY normalized)
		{
			double len = Math.Sqrt(value.X * value.X + value.Y * value.Y);
			if (len <= Epsilon)
			{
				normalized = XY.Zero;
				return false;
			}

			normalized = new XY(value.X / len, value.Y / len);
			return true;
		}

		private static double positiveMod(double value, double period)
		{
			double m = value % period;
			if (m < 0.0)
			{
				m += period;
			}

			return m;
		}

			private static XY perpendicularLeft(XY value)
			{
				return new XY(-value.Y, value.X);
			}

		private static double dot(XY a, XY b)
		{
			return a.X * b.X + a.Y * b.Y;
		}

		private static double cross(XY a, XY b)
		{
			return a.X * b.Y - a.Y * b.X;
		}

		private static double distance(XY a, XY b)
		{
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;
			return Math.Sqrt(dx * dx + dy * dy);
		}
	}
}
