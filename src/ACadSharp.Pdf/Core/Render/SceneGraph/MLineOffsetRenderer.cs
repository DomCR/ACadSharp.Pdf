using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf.Core.Render.Style;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACadSharp.Pdf.Core.Render.SceneGraph
{
	internal sealed class MLineOffsetRenderer
	{
		private const double Epsilon = 1e-9;

		private readonly Layout _layout;
		private readonly PdfConfiguration _configuration;
		private readonly PropertyResolver _resolver;
		private readonly RenderLog _log;

		private readonly struct AdjustedElement
		{
			public MLineStyle.Element Element { get; }
			public double Offset { get; }

			public AdjustedElement(MLineStyle.Element element, double offset)
			{
				this.Element = element;
				this.Offset = offset;
			}
		}

		public MLineOffsetRenderer(Layout layout, PdfConfiguration configuration, PropertyResolver resolver, RenderLog log)
		{
			this._layout = layout ?? throw new ArgumentNullException(nameof(layout));
			this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
			this._resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
			this._log = log ?? throw new ArgumentNullException(nameof(log));
		}

		public RenderNode Render(
			MLine mline,
			double styleScaleToPaper,
			ACadSharp.Color? byBlockColor,
			LineWeightType? byBlockLineWeight,
			LineType byBlockLineType)
		{
			if (mline == null)
			{
				return null;
			}

			MLineStyle style = mline.Style ?? MLineStyle.Default;
			List<MLineStyle.Element> elements = style.Elements?.ToList() ?? new List<MLineStyle.Element>();
			if (elements.Count == 0)
			{
				this._log.Add(mline.Handle, mline.SubclassMarker, RenderStatus.Skipped, "MLINE style has no elements.");
				return null;
			}

			bool isClosed = mline.Flags.HasFlag(MLineFlags.Closed);

			List<StrokeStyle> strokes = new List<StrokeStyle>(elements.Count);
			for (int i = 0; i < elements.Count; i++)
			{
				strokes.Add(resolveElementStroke(mline, elements[i], styleScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType));
			}

			List<AdjustedElement> adjusted = adjustElements(elements, mline.Justification, mline.ScaleFactor);
			List<int> orderedIndices = adjusted
				.Select((e, i) => new { e.Offset, Index = i })
				.OrderBy(p => p.Offset)
				.Select(p => p.Index)
				.ToList();

			int bottomIndex = orderedIndices[0];
			int topIndex = orderedIndices[orderedIndices.Count - 1];

			List<List<XY>> miterPoints = null;
			bool fromVertexParams = tryBuildMiterPointsFromVertexParams(mline, elements.Count, out miterPoints);

			if (fromVertexParams && hasUnsupportedVertexParametrization(mline, elements.Count))
			{
				this._log.Add(
					mline.Handle,
					mline.SubclassMarker,
					RenderStatus.Rendered,
					"MLINE advanced line/fill parametrization (gaps) is not yet supported; rendering as continuous lines/fill.");
			}

			if (!fromVertexParams)
			{
				List<XY> path = extractPathVertices(mline, isClosed);
				if ((!isClosed && path.Count < 2) || (isClosed && path.Count < 3))
				{
					this._log.Add(mline.Handle, mline.SubclassMarker, RenderStatus.Skipped, "MLINE has insufficient vertices.");
					return null;
				}

				miterPoints = buildMiterPointsFromOffsetAlgorithm(path, adjusted, isClosed);
			}

			int vertexCount = miterPoints?.Count ?? 0;
			if (vertexCount < 2)
			{
				this._log.Add(mline.Handle, mline.SubclassMarker, RenderStatus.Skipped, "MLINE produced no usable geometry.");
				return null;
			}

			var nodes = new List<RenderNode>(elements.Count + 16);

			if (style.Flags.HasFlag(MLineStyleFlags.FillOn) && elements.Count >= 2)
			{
				PathNode fill = createFillPath(mline, style, miterPoints, bottomIndex, topIndex, styleScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType);
				if (fill != null)
				{
					nodes.Add(fill);
				}
			}

			for (int elementIndex = 0; elementIndex < elements.Count; elementIndex++)
			{
				StrokeStyle stroke = strokes[elementIndex];
				if (stroke == null)
				{
					continue;
				}

				List<XY> polyline = new List<XY>(vertexCount);
				for (int v = 0; v < vertexCount; v++)
				{
					polyline.Add(miterPoints[v][elementIndex]);
				}

				PathNode pathNode = createPolylinePath(mline.Handle, polyline, stroke, closed: isClosed);
				if (pathNode != null)
				{
					nodes.Add(pathNode);
				}
			}

			if (style.Flags.HasFlag(MLineStyleFlags.DisplayJoints) && elements.Count >= 2 && vertexCount >= 3)
			{
				int start = isClosed ? 0 : 1;
				int end = isClosed ? vertexCount - 1 : vertexCount - 2;
				for (int v = start; v <= end; v++)
				{
					nodes.AddRange(createMiterJointLines(mline.Handle, miterPoints[v], topIndex, bottomIndex, strokes));
				}
			}

			if (!isClosed && elements.Count >= 2)
			{
				int lastVertex = vertexCount - 1;

				if (!mline.Flags.HasFlag(MLineFlags.NoStartCaps))
				{
					nodes.AddRange(createCaps(mline.Handle, style, miterPoints[0], orderedIndices, topIndex, bottomIndex, strokes, isStart: true));
				}

				if (!mline.Flags.HasFlag(MLineFlags.NoEndCaps))
				{
					nodes.AddRange(createCaps(mline.Handle, style, miterPoints[lastVertex], orderedIndices, topIndex, bottomIndex, strokes, isStart: false));
				}
			}

			nodes.RemoveAll(n => n == null);
			if (nodes.Count == 0)
			{
				this._log.Add(mline.Handle, mline.SubclassMarker, RenderStatus.Skipped, "MLINE produced no visible primitives.");
				return null;
			}

			this._log.Add(
				mline.Handle,
				mline.SubclassMarker,
				RenderStatus.Rendered,
				fromVertexParams
					? $"Rendered MLINE from DXF vertex parameters ({elements.Count} element(s))."
					: $"Rendered MLINE from offset approximation ({elements.Count} element(s)).");

			if (nodes.Count == 1)
			{
				return nodes[0];
			}

			return new GroupNode(mline.Handle, Matrix4.Identity, nodes);
		}

		private static bool tryBuildMiterPointsFromVertexParams(MLine mline, int elementCount, out List<List<XY>> miterPoints)
		{
			miterPoints = null;

			if (mline?.Vertices == null || mline.Vertices.Count < 2)
			{
				return false;
			}

			var points = new List<List<XY>>(mline.Vertices.Count);
			for (int i = 0; i < mline.Vertices.Count; i++)
			{
				MLine.Vertex vertex = mline.Vertices[i];
				if (vertex == null)
				{
					return false;
				}

				if (vertex.Segments == null || vertex.Segments.Count != elementCount)
				{
					return false;
				}

				if (isZeroVector(vertex.Miter))
				{
					return false;
				}

				var row = new List<XY>(elementCount);
				for (int e = 0; e < elementCount; e++)
				{
					MLine.Vertex.Segment segment = vertex.Segments[e];
					double length = 0.0;
					if (segment?.Parameters != null && segment.Parameters.Count > 0)
					{
						length = segment.Parameters[0];
					}
					else
					{
						// Missing miter offset value.
						return false;
					}

					XYZ p = vertex.Position + vertex.Miter * length;
					if (!isFinite(p.X) || !isFinite(p.Y))
					{
						return false;
					}

					row.Add(new XY(p.X, p.Y));
				}

				points.Add(row);
			}

			miterPoints = points;
			return true;
		}

		private static bool hasUnsupportedVertexParametrization(MLine mline, int elementCount)
		{
			if (mline?.Vertices == null || mline.Vertices.Count == 0 || elementCount <= 0)
			{
				return false;
			}

			for (int i = 0; i < mline.Vertices.Count; i++)
			{
				MLine.Vertex vertex = mline.Vertices[i];
				if (vertex?.Segments == null)
				{
					continue;
				}

				int n = Math.Min(elementCount, vertex.Segments.Count);
				for (int e = 0; e < n; e++)
				{
					MLine.Vertex.Segment segment = vertex.Segments[e];
					if (segment == null)
					{
						continue;
					}

					if (segment.Parameters != null)
					{
						// Parameters beyond [miter-offset, line-start-offset] include dash/gap ranges.
						if (segment.Parameters.Count > 2)
						{
							return true;
						}

						if (segment.Parameters.Count > 1 && Math.Abs(segment.Parameters[1]) > Epsilon)
						{
							return true;
						}
					}

					if (segment.AreaFillParameters != null)
					{
						for (int p = 0; p < segment.AreaFillParameters.Count; p++)
						{
							if (Math.Abs(segment.AreaFillParameters[p]) > Epsilon)
							{
								return true;
							}
						}
					}
				}
			}

			return false;
		}

		private static List<List<XY>> buildMiterPointsFromOffsetAlgorithm(IReadOnlyList<XY> path, IReadOnlyList<AdjustedElement> adjusted, bool closed)
		{
			int vertexCount = path?.Count ?? 0;
			int elementCount = adjusted?.Count ?? 0;
			var elementPolylines = new List<List<XY>>(elementCount);

			for (int i = 0; i < elementCount; i++)
			{
				elementPolylines.Add(buildOffsetPolylineRaw(path, adjusted[i].Offset, closed));
			}

			var grid = new List<List<XY>>(vertexCount);
			for (int v = 0; v < vertexCount; v++)
			{
				var row = new List<XY>(elementCount);
				for (int e = 0; e < elementCount; e++)
				{
					row.Add(elementPolylines[e][v]);
				}
				grid.Add(row);
			}

			return grid;
		}

		private static List<AdjustedElement> adjustElements(IReadOnlyList<MLineStyle.Element> elements, MLineJustification justification, double scaleFactor)
		{
			if (elements == null || elements.Count == 0)
			{
				return new List<AdjustedElement>();
			}

			double maxOffset = elements.Max(e => e.Offset);
			double minOffset = elements.Min(e => e.Offset);
			double shift = 0.0;

			switch (justification)
			{
				case MLineJustification.Top:
					shift = -maxOffset;
					break;
				case MLineJustification.Bottom:
					shift = -minOffset;
					break;
				case MLineJustification.Zero:
				default:
					shift = 0.0;
					break;
			}

			double safeScale = isFinite(scaleFactor) ? scaleFactor : 1.0;
			var result = new List<AdjustedElement>(elements.Count);
			for (int i = 0; i < elements.Count; i++)
			{
				double adjusted = (elements[i].Offset + shift) * safeScale;
				result.Add(new AdjustedElement(elements[i], adjusted));
			}

			return result;
		}

		private static List<XY> extractPathVertices(MLine mline, bool isClosed)
		{
			var points = new List<XY>(mline.Vertices?.Count ?? 0);
			if (mline.Vertices != null)
			{
				foreach (MLine.Vertex vertex in mline.Vertices)
				{
					XY point = new XY(vertex.Position.X, vertex.Position.Y);
					if (!isFinite(point.X) || !isFinite(point.Y))
					{
						continue;
					}

					if (points.Count == 0 || distance(points[points.Count - 1], point) > Epsilon)
					{
						points.Add(point);
					}
				}
			}

			if (isClosed && points.Count > 1 && distance(points[0], points[points.Count - 1]) <= Epsilon)
			{
				points.RemoveAt(points.Count - 1);
			}

			return points;
		}

		private static List<XY> buildOffsetPolylineRaw(IReadOnlyList<XY> vertices, double offset, bool closed)
		{
			int count = vertices?.Count ?? 0;
			var output = new List<XY>(count);
			if (count == 0)
			{
				return output;
			}

			if (Math.Abs(offset) <= Epsilon)
			{
				for (int i = 0; i < count; i++)
				{
					output.Add(vertices[i]);
				}
				return output;
			}

			for (int i = 0; i < count; i++)
			{
				if (!closed && i == 0)
				{
					XY first = vertices[0];
					XY next = vertices[1];
					if (tryNormalize(next - first, out XY dir))
					{
						output.Add(first + perpendicularLeft(dir) * offset);
					}
					else
					{
						output.Add(first);
					}
					continue;
				}

				if (!closed && i == count - 1)
				{
					XY prev = vertices[count - 2];
					XY last = vertices[count - 1];
					if (tryNormalize(last - prev, out XY dir))
					{
						output.Add(last + perpendicularLeft(dir) * offset);
					}
					else
					{
						output.Add(last);
					}
					continue;
				}

				int prevIndex = i == 0 ? count - 1 : i - 1;
				int nextIndex = i == count - 1 ? 0 : i + 1;
				output.Add(computeCornerOffset(vertices[prevIndex], vertices[i], vertices[nextIndex], offset));
			}

			return output;
		}

		private static XY computeCornerOffset(XY prev, XY curr, XY next, double offset)
		{
			if (!tryNormalize(curr - prev, out XY dPrev) || !tryNormalize(next - curr, out XY dNext))
			{
				return curr;
			}

			XY nPrev = perpendicularLeft(dPrev);
			XY nNext = perpendicularLeft(dNext);
			double parallel = dot(dPrev, dNext);

			if (parallel > 0.999999 || parallel < -0.999999)
			{
				return curr + nPrev * offset;
			}

			XY a1 = curr + nPrev * offset;
			XY a2 = a1 + dPrev;
			XY b1 = curr + nNext * offset;
			XY b2 = b1 + dNext;

			if (tryIntersectInfiniteLines(a1, a2, b1, b2, out XY intersection))
			{
				double maxMiter = Math.Max(1e-3, Math.Abs(offset) * 10.0);
				if (distance(intersection, curr) <= maxMiter)
				{
					return intersection;
				}
			}

			// Bevel-style fallback when miter becomes unstable or excessively long.
			return curr + nPrev * offset;
		}

		private PathNode createFillPath(
			MLine mline,
			MLineStyle style,
			IReadOnlyList<List<XY>> miterPoints,
			int bottomIndex,
			int topIndex,
			double styleScaleToPaper,
			ACadSharp.Color? byBlockColor,
			LineWeightType? byBlockLineWeight,
			LineType byBlockLineType)
		{
			if (miterPoints == null || miterPoints.Count < 2)
			{
				return null;
			}

			bool isClosed = mline.Flags.HasFlag(MLineFlags.Closed);

			var bottom = new List<XY>(miterPoints.Count);
			var top = new List<XY>(miterPoints.Count);
			for (int i = 0; i < miterPoints.Count; i++)
			{
				List<XY> row = miterPoints[i];
				if (row == null || row.Count <= Math.Max(bottomIndex, topIndex))
				{
					return null;
				}
				bottom.Add(row[bottomIndex]);
				top.Add(row[topIndex]);
			}

			if (bottom.Count < 2 || top.Count < 2 || Math.Abs(signedArea(bottom, top)) <= Epsilon)
			{
				return null;
			}

			bool startRound = !isClosed
				&& !mline.Flags.HasFlag(MLineFlags.NoStartCaps)
				&& style.Flags.HasFlag(MLineStyleFlags.StartRoundCap);
			bool endRound = !isClosed
				&& !mline.Flags.HasFlag(MLineFlags.NoEndCaps)
				&& style.Flags.HasFlag(MLineStyleFlags.EndRoundCap);

			var polygon = new List<XY>(bottom.Count + top.Count + 256);
			polygon.AddRange(bottom);

			if (!isClosed)
			{
				XY bottomEnd = bottom[bottom.Count - 1];
				XY topEnd = top[top.Count - 1];
				if (endRound)
				{
					appendSemicircle(polygon, bottomEnd, topEnd, includeStart: false);
				}
				else
				{
					polygon.Add(topEnd);
				}
			}

			for (int i = top.Count - 1; i >= 0; i--)
			{
				XY p = top[i];
				if (polygon.Count == 0 || distance(polygon[polygon.Count - 1], p) > Epsilon)
				{
					polygon.Add(p);
				}
			}

			if (!isClosed)
			{
				XY topStart = top[0];
				XY bottomStart = bottom[0];
				if (startRound)
				{
					appendSemicircle(polygon, topStart, bottomStart, includeStart: false);
				}
			}

			List<XY> cleaned = sanitizePolygon(polygon);
			if (cleaned.Count < 3)
			{
				return null;
			}

			ACadSharp.Color fillColor = resolveFillColor(style.FillColor, mline, styleScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType);
			var segments = new List<PathSegment>(cleaned.Count + 1)
			{
				new MoveTo(cleaned[0]),
			};
			for (int i = 1; i < cleaned.Count; i++)
			{
				segments.Add(new LineTo(cleaned[i]));
			}
			segments.Add(new ClosePath());

			return new PathNode(mline.Handle, segments, stroke: null, fill: new FillStyle(fillColor));
		}

		private static double signedArea(List<XY> bottom, List<XY> top)
		{
			if (bottom == null || top == null || bottom.Count < 2 || top.Count < 2)
			{
				return 0.0;
			}

			var polygon = new List<XY>(bottom.Count + top.Count);
			polygon.AddRange(bottom);
			for (int i = top.Count - 1; i >= 0; i--)
			{
				polygon.Add(top[i]);
			}

			double area = 0.0;
			for (int i = 0; i < polygon.Count; i++)
			{
				XY a = polygon[i];
				XY b = polygon[(i + 1) % polygon.Count];
				area += a.X * b.Y - b.X * a.Y;
			}

			return area;
		}

		private void appendSemicircle(List<XY> points, XY start, XY end, bool includeStart)
		{
			XY center = (start + end) * 0.5;
			double radius = distance(start, end) * 0.5;
			if (radius <= Epsilon)
			{
				return;
			}

			double baseAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
			double endAngle = baseAngle + Math.PI;
			List<XY> arc = buildArcPoints(center, radius, baseAngle, endAngle);

			int index = includeStart ? 0 : 1;
			for (int i = index; i < arc.Count; i++)
			{
				points.Add(arc[i]);
			}
		}

		private List<RenderNode> createCaps(
			ulong handle,
			MLineStyle style,
			IReadOnlyList<XY> miterAtEndpoint,
			IReadOnlyList<int> orderedIndices,
			int topIndex,
			int bottomIndex,
			IReadOnlyList<StrokeStyle> strokes,
			bool isStart)
		{
			var nodes = new List<RenderNode>();
			if (miterAtEndpoint == null || miterAtEndpoint.Count < 2)
			{
				return nodes;
			}

			if (isStart)
			{
				if (style.Flags.HasFlag(MLineStyleFlags.StartSquareCap))
				{
					nodes.AddRange(createMiterJointLines(handle, miterAtEndpoint, topIndex, bottomIndex, strokes));
				}

				if (style.Flags.HasFlag(MLineStyleFlags.StartRoundCap))
				{
					nodes.AddRange(createRoundCap(handle, miterAtEndpoint[topIndex], miterAtEndpoint[bottomIndex], strokes[topIndex], strokes[bottomIndex]));
				}

				if (style.Flags.HasFlag(MLineStyleFlags.StartInnerArcsCap) && orderedIndices.Count > 3)
				{
					int startIndex = orderedIndices[orderedIndices.Count - 2];
					int endIndex = orderedIndices[1];
					nodes.AddRange(createRoundCap(handle, miterAtEndpoint[startIndex], miterAtEndpoint[endIndex], strokes[startIndex], strokes[endIndex]));
				}
			}
			else
			{
				if (style.Flags.HasFlag(MLineStyleFlags.EndSquareCap))
				{
					nodes.AddRange(createMiterJointLines(handle, miterAtEndpoint, topIndex, bottomIndex, strokes));
				}

				if (style.Flags.HasFlag(MLineStyleFlags.EndRoundCap))
				{
					nodes.AddRange(createRoundCap(handle, miterAtEndpoint[bottomIndex], miterAtEndpoint[topIndex], strokes[bottomIndex], strokes[topIndex]));
				}

				if (style.Flags.HasFlag(MLineStyleFlags.EndInnerArcsCap) && orderedIndices.Count > 3)
				{
					int startIndex = orderedIndices[1];
					int endIndex = orderedIndices[orderedIndices.Count - 2];
					nodes.AddRange(createRoundCap(handle, miterAtEndpoint[startIndex], miterAtEndpoint[endIndex], strokes[startIndex], strokes[endIndex]));
				}
			}

			return nodes;
		}

		private static List<RenderNode> createMiterJointLines(
			ulong handle,
			IReadOnlyList<XY> miterAtVertex,
			int topIndex,
			int bottomIndex,
			IReadOnlyList<StrokeStyle> strokes)
		{
			var nodes = new List<RenderNode>(2);
			if (miterAtVertex == null || strokes == null)
			{
				return nodes;
			}

			if (topIndex < 0 || bottomIndex < 0 || topIndex >= miterAtVertex.Count || bottomIndex >= miterAtVertex.Count)
			{
				return nodes;
			}

			XY top = miterAtVertex[topIndex];
			XY bottom = miterAtVertex[bottomIndex];
			XY mid = (top + bottom) * 0.5;

			PathNode t = createLinePath(handle, top, mid, strokes[topIndex]);
			if (t != null) nodes.Add(t);

			PathNode b = createLinePath(handle, bottom, mid, strokes[bottomIndex]);
			if (b != null) nodes.Add(b);

			return nodes;
		}

		private List<RenderNode> createRoundCap(ulong handle, XY start, XY end, StrokeStyle stroke1, StrokeStyle stroke2)
		{
			var nodes = new List<RenderNode>(2);
			if (stroke1 == null || stroke2 == null)
			{
				return nodes;
			}

			XY center = (start + end) * 0.5;
			double radius = distance(start, end) * 0.5;
			if (radius <= Epsilon)
			{
				return nodes;
			}

			double a0 = Math.Atan2(start.Y - center.Y, start.X - center.X);
			bool sameColor = stroke1.Color.Equals(stroke2.Color);

			if (sameColor)
			{
				List<XY> arc = buildArcPoints(center, radius, a0, a0 + Math.PI);
				PathNode path = createPolylinePath(handle, arc, stroke1, closed: false);
				if (path != null)
				{
					nodes.Add(path);
				}
				return nodes;
			}

			List<XY> arc1 = buildArcPoints(center, radius, a0, a0 + Math.PI / 2.0);
			PathNode p1 = createPolylinePath(handle, arc1, stroke1, closed: false);
			if (p1 != null)
			{
				nodes.Add(p1);
			}

			List<XY> arc2 = buildArcPoints(center, radius, a0 + Math.PI / 2.0, a0 + Math.PI);
			PathNode p2 = createPolylinePath(handle, arc2, stroke2, closed: false);
			if (p2 != null)
			{
				nodes.Add(p2);
			}

			return nodes;
		}

		private List<XY> buildArcPoints(XY center, double radius, double startAngle, double endAngle)
		{
			if (radius <= Epsilon)
			{
				return new List<XY>();
			}

			double sweep = endAngle - startAngle;
			if (sweep <= 0.0)
			{
				sweep += Math.PI * 2.0;
			}

			int segments = Math.Max(8, (int)Math.Ceiling(this._configuration.ArcPrecision * (sweep / (Math.PI * 2.0))));
			segments = Math.Min(4096, segments);

			var points = new List<XY>(segments + 1);
			for (int i = 0; i <= segments; i++)
			{
				double t = (double)i / segments;
				double angle = startAngle + sweep * t;
				points.Add(new XY(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
			}

			return points;
		}

		private StrokeStyle resolveElementStroke(
			MLine mline,
			MLineStyle.Element element,
			double styleScaleToPaper,
			ACadSharp.Color? byBlockColor,
			LineWeightType? byBlockLineWeight,
			LineType byBlockLineType)
		{
			ACadSharp.Color colorRef = resolveElementColorReference(element?.Color ?? ACadSharp.Color.ByLayer, mline.Color);
			LineType lineTypeRef = resolveElementLineTypeReference(element?.LineType, mline.LineType);

			var proxy = new Line
			{
				StartPoint = XYZ.Zero,
				EndPoint = new XYZ(1.0, 0.0, 0.0),
				Layer = mline.Layer,
				Color = colorRef,
				LineWeight = mline.LineWeight,
				LineType = lineTypeRef,
				LineTypeScale = mline.LineTypeScale,
			};

			return this._resolver.ResolveStroke(proxy, this._layout, styleScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType);
		}

		private ACadSharp.Color resolveFillColor(
			ACadSharp.Color fillColor,
			MLine mline,
			double styleScaleToPaper,
			ACadSharp.Color? byBlockColor,
			LineWeightType? byBlockLineWeight,
			LineType byBlockLineType)
		{
			ACadSharp.Color colorRef = resolveElementColorReference(fillColor, mline.Color);
			var proxy = new Line
			{
				StartPoint = XYZ.Zero,
				EndPoint = new XYZ(1.0, 0.0, 0.0),
				Layer = mline.Layer,
				Color = colorRef,
				LineWeight = mline.LineWeight,
				LineType = LineType.ByLayer,
			};

			return this._resolver.ResolveStroke(proxy, this._layout, styleScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType).Color;
		}

		private static ACadSharp.Color resolveElementColorReference(ACadSharp.Color color, ACadSharp.Color mlineColor)
		{
			return color.IsByBlock ? mlineColor : color;
		}

		private static LineType resolveElementLineTypeReference(LineType elementLineType, LineType mlineLineType)
		{
			LineType source = elementLineType ?? LineType.ByLayer;
			if (string.Equals(source.Name, LineType.ByBlockName, StringComparison.InvariantCultureIgnoreCase))
			{
				return mlineLineType ?? LineType.ByLayer;
			}

			return source;
		}

		private static PathNode createLinePath(ulong handle, XY start, XY end, StrokeStyle stroke)
		{
			if (stroke == null || distance(start, end) <= Epsilon)
			{
				return null;
			}

			var segments = new PathSegment[]
			{
				new MoveTo(start),
				new LineTo(end),
			};
			return new PathNode(handle, segments, stroke, fill: null);
		}

		private static PathNode createPolylinePath(ulong handle, IReadOnlyList<XY> points, StrokeStyle stroke, bool closed)
		{
			if (stroke == null || points == null)
			{
				return null;
			}

			List<XY> cleaned = sanitizePolyline(points, closed);
			if ((!closed && cleaned.Count < 2) || (closed && cleaned.Count < 3))
			{
				return null;
			}

			var segments = new List<PathSegment>(cleaned.Count + 1)
			{
				new MoveTo(cleaned[0]),
			};
			for (int i = 1; i < cleaned.Count; i++)
			{
				segments.Add(new LineTo(cleaned[i]));
			}
			if (closed)
			{
				segments.Add(new ClosePath());
			}

			return new PathNode(handle, segments, stroke, fill: null);
		}

		private static List<XY> sanitizePolyline(IReadOnlyList<XY> points, bool closed)
		{
			var cleaned = new List<XY>(points?.Count ?? 0);
			if (points == null || points.Count == 0)
			{
				return cleaned;
			}

			for (int i = 0; i < points.Count; i++)
			{
				XY point = points[i];
				if (cleaned.Count == 0 || distance(cleaned[cleaned.Count - 1], point) > Epsilon)
				{
					cleaned.Add(point);
				}
			}

			if (closed && cleaned.Count > 1 && distance(cleaned[0], cleaned[cleaned.Count - 1]) <= Epsilon)
			{
				cleaned.RemoveAt(cleaned.Count - 1);
			}

			return cleaned;
		}

		private static List<XY> sanitizePolygon(IReadOnlyList<XY> points)
		{
			List<XY> cleaned = sanitizePolyline(points, closed: true);
			if (cleaned.Count < 3)
			{
				return new List<XY>();
			}

			double area = 0.0;
			for (int i = 0; i < cleaned.Count; i++)
			{
				XY a = cleaned[i];
				XY b = cleaned[(i + 1) % cleaned.Count];
				area += a.X * b.Y - b.X * a.Y;
			}

			if (Math.Abs(area) <= Epsilon)
			{
				return new List<XY>();
			}

			return cleaned;
		}

		private static bool tryNormalize(XY value, out XY normalized)
		{
			double length = Math.Sqrt(value.X * value.X + value.Y * value.Y);
			if (length <= Epsilon)
			{
				normalized = XY.Zero;
				return false;
			}

			normalized = new XY(value.X / length, value.Y / length);
			return true;
		}

		private static bool isFinite(double value)
		{
			return !double.IsNaN(value) && !double.IsInfinity(value);
		}

		private static bool isZeroVector(XYZ value)
		{
			return Math.Abs(value.X) <= 1e-12
				&& Math.Abs(value.Y) <= 1e-12
				&& Math.Abs(value.Z) <= 1e-12;
		}

		private static XY perpendicularLeft(XY value)
		{
			return new XY(-value.Y, value.X);
		}

		private static double dot(XY a, XY b)
		{
			return a.X * b.X + a.Y * b.Y;
		}

		private static double distance(XY a, XY b)
		{
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;
			return Math.Sqrt(dx * dx + dy * dy);
		}

		private static bool tryIntersectInfiniteLines(XY a1, XY a2, XY b1, XY b2, out XY intersection)
		{
			double x1 = a1.X;
			double y1 = a1.Y;
			double x2 = a2.X;
			double y2 = a2.Y;
			double x3 = b1.X;
			double y3 = b1.Y;
			double x4 = b2.X;
			double y4 = b2.Y;

			double denominator = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
			if (Math.Abs(denominator) <= 1e-12)
			{
				intersection = XY.Zero;
				return false;
			}

			double det1 = x1 * y2 - y1 * x2;
			double det2 = x3 * y4 - y3 * x4;
			double px = (det1 * (x3 - x4) - (x1 - x2) * det2) / denominator;
			double py = (det1 * (y3 - y4) - (y1 - y2) * det2) / denominator;
			intersection = new XY(px, py);
			return true;
		}
	}
}
