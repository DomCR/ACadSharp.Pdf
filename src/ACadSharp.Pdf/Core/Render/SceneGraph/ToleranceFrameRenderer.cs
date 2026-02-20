using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf.Core.Render.Style;
using ACadSharp.Pdf.Core.Render.Text;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACadSharp.Pdf.Core.Render.SceneGraph
{
	/// <summary>
	/// Stage 09: renders TOLERANCE (feature control frames / GD&amp;T) into the scene graph.
	/// </summary>
	internal sealed class ToleranceFrameRenderer
	{
		private const double Epsilon = 1e-9;
		private const double Kappa = 0.5522847498307933984022516322796;

		private readonly Layout _layout;
		private readonly PdfConfiguration _configuration;
		private readonly PropertyResolver _resolver;
		private readonly RenderLog _log;
		private readonly TextLayoutEngine _textLayout;

		public ToleranceFrameRenderer(
			Layout layout,
			PdfConfiguration configuration,
			PropertyResolver resolver,
			RenderLog log,
			TextLayoutEngine textLayout)
		{
			this._layout = layout ?? throw new ArgumentNullException(nameof(layout));
			this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
			this._resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
			this._log = log ?? throw new ArgumentNullException(nameof(log));
			this._textLayout = textLayout ?? throw new ArgumentNullException(nameof(textLayout));
		}

		public RenderNode Render(
			Tolerance tolerance,
			double styleScaleToPaper,
			double textScaleToPaper,
			Color? byBlockColor,
			LineWeightType? byBlockLineWeight,
			LineType byBlockLineType)
		{
			if (tolerance == null)
			{
				return null;
			}

			FeatureControlFrame frame = parseFrame(tolerance.Text);
			if (frame == null || frame.Rows.Count == 0)
			{
				this._log.Add(tolerance.Handle, tolerance.SubclassMarker, RenderStatus.Skipped, "Tolerance text is empty or unparseable.");
				return null;
			}

			if (tolerance.Normal != XYZ.AxisZ && tolerance.Normal != XYZ.Zero)
			{
				this._log.Add(tolerance.Handle, tolerance.SubclassMarker, RenderStatus.Rendered, "Tolerance normal is not supported (top-view only); rendering as if AxisZ.");
			}

			DimensionStyle style = tolerance.Style ?? DimensionStyle.Default;
			double scale = dimensionScale(style);
			double textHeight = Math.Max(1e-6, style.TextHeight * scale);
			double rowHeight = Math.Max(textHeight * 2.0, textHeight + 1e-6);
			double gap = Math.Max(0.0, Math.Abs(style.DimensionLineGap * scale));
			double minCompartmentWidth = rowHeight * 0.75;

			XY xAxis = new XY(tolerance.Direction.X, tolerance.Direction.Y);
			if (!tryNormalize(xAxis, out xAxis))
			{
				xAxis = XY.AxisX;
			}
			XY yAxis = perpendicularLeft(xAxis);
			double rotation = Math.Atan2(xAxis.Y, xAxis.X);
			XY origin = (XY)tolerance.InsertionPoint;

			StrokeStyle borderStroke = createBorderStroke(tolerance, style, styleScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType);
			StrokeStyle symbolStroke = borderStroke != null
				? new StrokeStyle(borderStroke.Color, borderStroke.WidthPt, Array.Empty<double>(), 0)
				: null;

			double totalHeight = frame.Rows.Count * rowHeight;
			var nodes = new List<RenderNode>();

			for (int rowIndex = 0; rowIndex < frame.Rows.Count; rowIndex++)
			{
				FrameRow row = frame.Rows[rowIndex];
				if (row == null || row.Compartments.Count == 0)
				{
					continue;
				}

				double rowY = totalHeight - (rowIndex + 1) * rowHeight;
				double x = 0.0;

				for (int col = 0; col < row.Compartments.Count; col++)
				{
					Compartment c = row.Compartments[col];
					double w = measureCompartmentWidth(c, col, rowHeight, textHeight, gap, minCompartmentWidth);

					PathNode border = createRect(
						tolerance.Handle,
						origin,
						xAxis,
						yAxis,
						x,
						rowY,
						w,
						rowHeight,
						borderStroke);
					if (border != null)
					{
						nodes.Add(border);
					}

					renderCompartmentContent(nodes, tolerance, style, c, origin, xAxis, yAxis, x, rowY, w, rowHeight, textHeight, gap, rotation, textScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType, symbolStroke);

					x += w;
				}
			}

			if (nodes.Count == 0)
			{
				this._log.Add(tolerance.Handle, tolerance.SubclassMarker, RenderStatus.Skipped, "Tolerance produced no visible primitives.");
				return null;
			}

			this._log.Add(tolerance.Handle, tolerance.SubclassMarker, RenderStatus.Rendered, $"Rendered tolerance frame with {frame.Rows.Count} row(s).");
			return new GroupNode(tolerance.Handle, Matrix4.Identity, nodes);
		}

		private void renderCompartmentContent(
			List<RenderNode> nodes,
			Tolerance tolerance,
			DimensionStyle style,
			Compartment compartment,
			XY origin,
			XY xAxis,
			XY yAxis,
			double x,
			double y,
			double width,
			double height,
			double textHeight,
			double gap,
			double rotation,
			double textScaleToPaper,
			Color? byBlockColor,
			LineWeightType? byBlockLineWeight,
			LineType byBlockLineType,
			StrokeStyle symbolStroke)
		{
			if (compartment == null)
			{
				return;
			}

			switch (compartment.Kind)
			{
				case CompartmentKind.Symbol:
					{
						XY centerFrame = new XY(x + (width * 0.5), y + (height * 0.5));
						XY centerWorld = frameToWorld(origin, xAxis, yAxis, centerFrame.X, centerFrame.Y);
						double size = Math.Min(width, height);
						nodes.AddRange(drawGdtSymbol(tolerance.Handle, compartment.Symbol, centerWorld, xAxis, yAxis, size, symbolStroke));
						break;
					}
				case CompartmentKind.Tolerance:
				case CompartmentKind.Datum:
				case CompartmentKind.Text:
					{
						string text = compartment.Text ?? string.Empty;
						double left = x + gap;
						double right = x + width - gap;
						double available = Math.Max(0.0, right - left);

						var decorations = new List<Action<double>>();
						double decorationWidth = 0.0;
						double decorationGap = Math.Max(0.0, gap * 0.5);

						if (compartment.HasDiameterPrefix)
						{
							double dw = Math.Max(textHeight, height * 0.45);
							decorationWidth += dw;
							decorations.Add(startX => nodes.Add(createDiameterSymbol(tolerance.Handle, origin, xAxis, yAxis, startX + (dw * 0.5), y + (height * 0.5), dw * 0.75, symbolStroke)));
							decorationWidth += decorationGap;
						}

						double textWidth = estimateTextWidth(text, textHeight);
						double modifierWidth = 0.0;
						if (compartment.Modifier != MaterialModifier.None)
						{
							modifierWidth = Math.Max(textHeight, height * 0.45);
						}

						double contentWidth = decorationWidth + textWidth + (modifierWidth > 0 ? decorationGap + modifierWidth : 0.0);
						double start = left + (available - contentWidth) * 0.5;
						if (!isFiniteNumber(start))
						{
							return;
						}

						double cursor = start;
						foreach (var draw in decorations)
						{
							draw(cursor);
							cursor += Math.Max(textHeight, height * 0.45) + decorationGap;
						}

						if (!string.IsNullOrWhiteSpace(text))
						{
							XY textCenterFrame = new XY(cursor + (textWidth * 0.5), y + (height * 0.5));
							XY textCenterWorld = frameToWorld(origin, xAxis, yAxis, textCenterFrame.X, textCenterFrame.Y);
							addCenteredText(nodes, tolerance, style, text, textHeight, textCenterWorld, rotation, textScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType);
							cursor += textWidth;
						}

						if (compartment.Modifier != MaterialModifier.None)
						{
							cursor += decorationGap;
							XY modCenterFrame = new XY(cursor + (modifierWidth * 0.5), y + (height * 0.5));
							XY modCenterWorld = frameToWorld(origin, xAxis, yAxis, modCenterFrame.X, modCenterFrame.Y);
							nodes.Add(createCircledLetter(tolerance.Handle, origin, xAxis, yAxis, modCenterWorld.X, modCenterWorld.Y, modifierWidth * 0.8, modifierLetter(compartment.Modifier), style, tolerance, rotation, textScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType, symbolStroke));
						}

						break;
					}
			}
		}

		private void addCenteredText(
			List<RenderNode> nodes,
			Tolerance tolerance,
			DimensionStyle style,
			string text,
			double height,
			XY center,
			double rotation,
			double textScaleToPaper,
			Color? byBlockColor,
			LineWeightType? byBlockLineWeight,
			LineType byBlockLineType)
		{
			if (nodes == null || string.IsNullOrWhiteSpace(text))
			{
				return;
			}

			var textEntity = new TextEntity
			{
				Value = text,
				Height = height,
				InsertPoint = new XYZ(center.X, center.Y, tolerance.InsertionPoint.Z),
				AlignmentPoint = new XYZ(center.X, center.Y, tolerance.InsertionPoint.Z),
				HorizontalAlignment = TextHorizontalAlignment.Center,
				VerticalAlignment = TextVerticalAlignmentType.Middle,
				Rotation = rotation,
				Style = style.Style ?? TextStyle.Default,
				Color = style.TextColor,
				Layer = tolerance.Layer,
				Normal = tolerance.Normal,
			};

			Color color = this._resolver.ResolveStroke(textEntity, this._layout, textScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType).Color;
			TextRunNode node = this._textLayout.LayoutText(textEntity, textScaleToPaper, color);
			if (node != null)
			{
				nodes.Add(node);
			}
		}

		private static double measureCompartmentWidth(Compartment c, int index, double rowHeight, double textHeight, double gap, double minWidth)
		{
			if (c == null)
			{
				return 0.0;
			}

			if (index == 0 && c.Kind == CompartmentKind.Symbol)
			{
				return rowHeight;
			}

			double width = estimateTextWidth(c.Text ?? string.Empty, textHeight) + (2.0 * gap);
			if (c.HasDiameterPrefix)
			{
				width += Math.Max(textHeight, rowHeight * 0.45);
			}
			if (c.Modifier != MaterialModifier.None)
			{
				width += Math.Max(textHeight, rowHeight * 0.45);
			}

			return Math.Max(minWidth, width);
		}

		private StrokeStyle createBorderStroke(
			Tolerance tolerance,
			DimensionStyle style,
			double styleScaleToPaper,
			Color? byBlockColor,
			LineWeightType? byBlockLineWeight,
			LineType byBlockLineType)
		{
			double globalLtScale = tolerance?.Document?.Header?.LineTypeScale ?? 1.0;
			var proxy = new Line
			{
				StartPoint = XYZ.Zero,
				EndPoint = XY.AxisX.Convert<XYZ>(),
				Layer = tolerance?.Layer,
				Color = style?.DimensionLineColor ?? tolerance?.Color ?? Color.ByLayer,
				LineWeight = style?.DimensionLineWeight ?? tolerance?.LineWeight ?? LineWeightType.ByLayer,
				LineType = style?.LineType ?? tolerance?.LineType ?? LineType.ByLayer,
				LineTypeScale = globalLtScale,
			};

			return this._resolver.ResolveStroke(proxy, this._layout, styleScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType);
		}

		private static PathNode createRect(
			ulong handle,
			XY origin,
			XY xAxis,
			XY yAxis,
			double x,
			double y,
			double width,
			double height,
			StrokeStyle stroke)
		{
			if (stroke == null || width <= Epsilon || height <= Epsilon)
			{
				return null;
			}

			XY p1 = frameToWorld(origin, xAxis, yAxis, x, y);
			XY p2 = frameToWorld(origin, xAxis, yAxis, x + width, y);
			XY p3 = frameToWorld(origin, xAxis, yAxis, x + width, y + height);
			XY p4 = frameToWorld(origin, xAxis, yAxis, x, y + height);

			var segments = new PathSegment[]
			{
				new MoveTo(p1),
				new LineTo(p2),
				new LineTo(p3),
				new LineTo(p4),
				new ClosePath(),
			};
			return new PathNode(handle, segments, stroke, fill: null);
		}

		private static PathNode createDiameterSymbol(
			ulong handle,
			XY origin,
			XY xAxis,
			XY yAxis,
			double cxFrame,
			double cyFrame,
			double size,
			StrokeStyle stroke)
		{
			if (stroke == null || size <= Epsilon)
			{
				return null;
			}

			XY center = frameToWorld(origin, xAxis, yAxis, cxFrame, cyFrame);
			double r = size * 0.5;

			// Circle + slash (simplified).
			var nodes = new List<PathSegment>();
			appendCircle(nodes, center, r);

			XY p1 = frameToWorld(origin, xAxis, yAxis, cxFrame - r * 0.7, cyFrame - r * 0.7);
			XY p2 = frameToWorld(origin, xAxis, yAxis, cxFrame + r * 0.7, cyFrame + r * 0.7);
			nodes.Add(new MoveTo(p1));
			nodes.Add(new LineTo(p2));

			return new PathNode(handle, nodes, stroke, fill: null);
		}

		private RenderNode createCircledLetter(
			ulong handle,
			XY origin,
			XY xAxis,
			XY yAxis,
			double cxWorld,
			double cyWorld,
			double size,
			char letter,
			DimensionStyle style,
			Tolerance tolerance,
			double rotation,
			double textScaleToPaper,
			Color? byBlockColor,
			LineWeightType? byBlockLineWeight,
			LineType byBlockLineType,
			StrokeStyle stroke)
		{
			if (size <= Epsilon)
			{
				return null;
			}

			double r = size * 0.5;
			var circleSegments = new List<PathSegment>();
			appendCircle(circleSegments, new XY(cxWorld, cyWorld), r);
			var circle = new PathNode(handle, circleSegments, stroke, fill: null);

			var groupChildren = new List<RenderNode> { circle };

			string value = letter == '\0' ? string.Empty : letter.ToString();
			if (!string.IsNullOrWhiteSpace(value))
			{
				var textEntity = new TextEntity
				{
					Value = value,
					Height = size * 0.65,
					InsertPoint = new XYZ(cxWorld, cyWorld, tolerance.InsertionPoint.Z),
					AlignmentPoint = new XYZ(cxWorld, cyWorld, tolerance.InsertionPoint.Z),
					HorizontalAlignment = TextHorizontalAlignment.Center,
					VerticalAlignment = TextVerticalAlignmentType.Middle,
					Rotation = rotation,
					Style = style.Style ?? TextStyle.Default,
					Color = style.TextColor,
					Layer = tolerance.Layer,
					Normal = tolerance.Normal,
				};

				Color color = this._resolver.ResolveStroke(textEntity, this._layout, textScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType).Color;
				TextRunNode node = this._textLayout.LayoutText(textEntity, textScaleToPaper, color);
				if (node != null)
				{
					groupChildren.Add(node);
				}
			}

			return new GroupNode(handle, Matrix4.Identity, groupChildren);
		}

		private static IReadOnlyList<RenderNode> drawGdtSymbol(
			ulong handle,
			GdtSymbol symbol,
			XY centerWorld,
			XY xAxis,
			XY yAxis,
			double size,
			StrokeStyle stroke)
		{
			if (stroke == null || size <= Epsilon)
			{
				return Array.Empty<RenderNode>();
			}

			double r = size * 0.35;
			double cx = centerWorld.X;
			double cy = centerWorld.Y;

			var nodes = new List<RenderNode>();

			switch (symbol)
			{
				case GdtSymbol.Position:
					{
						nodes.Add(circlePath(handle, new XY(cx, cy), r, stroke));
						nodes.Add(linePath(handle, new XY(cx, cy - r * 1.2), new XY(cx, cy + r * 1.2), stroke));
						nodes.Add(linePath(handle, new XY(cx - r * 1.2, cy), new XY(cx + r * 1.2, cy), stroke));
						break;
					}
				case GdtSymbol.Flatness:
					{
						double hw = r * 0.9;
						double hh = r * 0.5;
						double skew = r * 0.35;
						var pts = new[]
						{
							new XY(cx - hw + skew, cy - hh),
							new XY(cx + hw + skew, cy - hh),
							new XY(cx + hw - skew, cy + hh),
							new XY(cx - hw - skew, cy + hh),
						};
						nodes.Add(polyPath(handle, pts, stroke, closed: true));
						break;
					}
				case GdtSymbol.Straightness:
					nodes.Add(linePath(handle, new XY(cx - r, cy), new XY(cx + r, cy), stroke));
					break;
				case GdtSymbol.Circularity:
					nodes.Add(circlePath(handle, new XY(cx, cy), r, stroke));
					break;
				case GdtSymbol.Cylindricity:
					{
						nodes.Add(circlePath(handle, new XY(cx, cy), r * 0.7, stroke));
						nodes.Add(linePath(handle, new XY(cx - r, cy - r * 0.7), new XY(cx - r, cy + r * 0.7), stroke));
						nodes.Add(linePath(handle, new XY(cx + r, cy - r * 0.7), new XY(cx + r, cy + r * 0.7), stroke));
						break;
					}
				case GdtSymbol.Perpendicularity:
					{
						nodes.Add(linePath(handle, new XY(cx, cy - r), new XY(cx, cy + r * 0.5), stroke));
						nodes.Add(linePath(handle, new XY(cx - r * 0.7, cy + r * 0.5), new XY(cx + r * 0.7, cy + r * 0.5), stroke));
						break;
					}
				case GdtSymbol.Parallelism:
					{
						double o = r * 0.25;
						nodes.Add(linePath(handle, new XY(cx - o, cy - r), new XY(cx - o, cy + r), stroke));
						nodes.Add(linePath(handle, new XY(cx + o, cy - r), new XY(cx + o, cy + r), stroke));
						break;
					}
				case GdtSymbol.Angularity:
					{
						nodes.Add(linePath(handle, new XY(cx - r, cy - r * 0.5), new XY(cx + r, cy - r * 0.5), stroke));
						nodes.Add(linePath(handle, new XY(cx - r, cy - r * 0.5), new XY(cx + r * 0.5, cy + r * 0.8), stroke));
						break;
					}
				case GdtSymbol.Concentricity:
					nodes.Add(circlePath(handle, new XY(cx, cy), r, stroke));
					nodes.Add(circlePath(handle, new XY(cx, cy), r * 0.4, stroke));
					break;
				case GdtSymbol.Symmetry:
					nodes.Add(linePath(handle, new XY(cx - r, cy - r * 0.5), new XY(cx + r, cy - r * 0.5), stroke));
					nodes.Add(linePath(handle, new XY(cx - r, cy), new XY(cx + r, cy), stroke));
					nodes.Add(linePath(handle, new XY(cx - r, cy + r * 0.5), new XY(cx + r, cy + r * 0.5), stroke));
					break;
				case GdtSymbol.ProfileOfALine:
					{
						nodes.Add(arcPath(handle, new XY(cx, cy - r * 0.1), r * 0.9, startRad: Math.PI * 0.1, endRad: Math.PI * 0.9, stroke));
						break;
					}
				case GdtSymbol.CircularRunout:
				case GdtSymbol.TotalRunout:
					{
						nodes.Add(linePath(handle, new XY(cx - r, cy - r), new XY(cx + r, cy + r), stroke));
						nodes.Add(arrowTip(handle, new XY(cx + r, cy + r), new XY(-1, -1), r * 0.35, stroke));
						if (symbol == GdtSymbol.TotalRunout)
						{
							nodes.Add(linePath(handle, new XY(cx - r * 0.8, cy - r), new XY(cx + r * 0.8, cy + r), stroke));
						}
						break;
					}
				default:
					// Unknown symbol: draw a small X.
					nodes.Add(linePath(handle, new XY(cx - r, cy - r), new XY(cx + r, cy + r), stroke));
					nodes.Add(linePath(handle, new XY(cx - r, cy + r), new XY(cx + r, cy - r), stroke));
					break;
			}

			return nodes.Where(n => n != null).ToArray();
		}

		private static PathNode circlePath(ulong handle, XY center, double radius, StrokeStyle stroke)
		{
			if (radius <= Epsilon || stroke == null)
			{
				return null;
			}

			var segments = new List<PathSegment>(6);
			appendCircle(segments, center, radius);
			return new PathNode(handle, segments, stroke, fill: null);
		}

		private static void appendCircle(List<PathSegment> segments, XY center, double radius)
		{
			double k = Kappa * radius;
			XY p0 = new XY(center.X + radius, center.Y);
			XY p1 = new XY(center.X + radius, center.Y + k);
			XY p2 = new XY(center.X + k, center.Y + radius);
			XY p3 = new XY(center.X, center.Y + radius);
			XY p4 = new XY(center.X - k, center.Y + radius);
			XY p5 = new XY(center.X - radius, center.Y + k);
			XY p6 = new XY(center.X - radius, center.Y);
			XY p7 = new XY(center.X - radius, center.Y - k);
			XY p8 = new XY(center.X - k, center.Y - radius);
			XY p9 = new XY(center.X, center.Y - radius);
			XY p10 = new XY(center.X + k, center.Y - radius);
			XY p11 = new XY(center.X + radius, center.Y - k);

			segments.Add(new MoveTo(p0));
			segments.Add(new CubicTo(p1, p2, p3));
			segments.Add(new CubicTo(p4, p5, p6));
			segments.Add(new CubicTo(p7, p8, p9));
			segments.Add(new CubicTo(p10, p11, p0));
			segments.Add(new ClosePath());
		}

		private static PathNode arcPath(ulong handle, XY center, double radius, double startRad, double endRad, StrokeStyle stroke)
		{
			if (radius <= Epsilon || stroke == null)
			{
				return null;
			}

			double sweep = endRad - startRad;
			if (sweep < 0)
			{
				sweep += Math.PI * 2.0;
			}

			int segmentsCount = Math.Max(8, (int)Math.Ceiling(24.0 * (sweep / (Math.PI * 2.0))));
			var points = new List<XY>(segmentsCount + 1);
			for (int i = 0; i <= segmentsCount; i++)
			{
				double t = (double)i / segmentsCount;
				double a = startRad + sweep * t;
				points.Add(new XY(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
			}

			return polyPath(handle, points, stroke, closed: false);
		}

		private static PathNode linePath(ulong handle, XY a, XY b, StrokeStyle stroke)
		{
			if (stroke == null)
			{
				return null;
			}

			if (distance(a, b) <= Epsilon)
			{
				return null;
			}

			return new PathNode(handle, new PathSegment[] { new MoveTo(a), new LineTo(b) }, stroke, fill: null);
		}

		private static PathNode polyPath(ulong handle, IReadOnlyList<XY> points, StrokeStyle stroke, bool closed)
		{
			if (stroke == null || points == null || points.Count < 2)
			{
				return null;
			}

			var segs = new List<PathSegment>(points.Count + 1)
			{
				new MoveTo(points[0]),
			};
			for (int i = 1; i < points.Count; i++)
			{
				segs.Add(new LineTo(points[i]));
			}
			if (closed)
			{
				segs.Add(new ClosePath());
			}
			return new PathNode(handle, segs, stroke, fill: null);
		}

		private static PathNode arrowTip(ulong handle, XY tip, XY direction, double size, StrokeStyle stroke)
		{
			if (stroke == null || size <= Epsilon)
			{
				return null;
			}

			XY dir = direction;
			if (!tryNormalize(dir, out dir))
			{
				return null;
			}

			XY left = rotate(dir, Math.PI * 3.0 / 4.0);
			XY right = rotate(dir, -Math.PI * 3.0 / 4.0);
			XY p1 = tip + left * size;
			XY p2 = tip + right * size;
			return new PathNode(handle, new PathSegment[] { new MoveTo(p1), new LineTo(tip), new LineTo(p2) }, stroke, fill: null);
		}

		private static FeatureControlFrame parseFrame(string content)
		{
			if (string.IsNullOrWhiteSpace(content))
			{
				return null;
			}

			string[] rowParts = content.Split(new[] { "\\X", "\\x" }, StringSplitOptions.None);
			var rows = new List<FrameRow>();
			foreach (string rowText in rowParts)
			{
				FrameRow row = parseRow(rowText);
				if (row != null && row.Compartments.Count > 0)
				{
					rows.Add(row);
				}
			}

			return rows.Count == 0 ? null : new FeatureControlFrame(rows);
		}

		private static FrameRow parseRow(string rowText)
		{
			if (string.IsNullOrWhiteSpace(rowText))
			{
				return null;
			}

			string[] parts = rowText.Split(new[] { "%%v" }, StringSplitOptions.None);
			if (parts.Length == 0)
			{
				return null;
			}

			int lastNonEmpty = -1;
			for (int i = 0; i < parts.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(parts[i]))
				{
					lastNonEmpty = i;
				}
			}
			if (lastNonEmpty < 0)
			{
				return null;
			}

			var compartments = new List<Compartment>(lastNonEmpty + 1);
			for (int i = 0; i <= lastNonEmpty; i++)
			{
				string part = (parts[i] ?? string.Empty).Trim();
				if (i == 0)
				{
					compartments.Add(parseSymbolCompartment(part));
				}
				else if (i == 1)
				{
					compartments.Add(parseToleranceCompartment(part));
				}
				else
				{
					compartments.Add(parseDatumCompartment(part));
				}
			}

			// Trim trailing empty datum compartments.
			int lastMeaningful = compartments.Count - 1;
			for (int i = compartments.Count - 1; i >= 0; i--)
			{
				if (i <= 1)
				{
					break;
				}
				if (!string.IsNullOrWhiteSpace(compartments[i].Text) || compartments[i].Modifier != MaterialModifier.None)
				{
					lastMeaningful = i;
					break;
				}
				lastMeaningful = i - 1;
			}
			if (lastMeaningful < compartments.Count - 1)
			{
				compartments = compartments.Take(lastMeaningful + 1).ToList();
			}

			return new FrameRow(compartments);
		}

		private static Compartment parseSymbolCompartment(string part)
		{
			if (tryParseGdtSymbol(part, out char code))
			{
				return new Compartment(CompartmentKind.Symbol, text: string.Empty, hasDiameterPrefix: false, modifier: MaterialModifier.None, symbol: mapGdtSymbol(code));
			}

			// Fallback: treat as plain text in the first compartment.
			return new Compartment(CompartmentKind.Text, decodeTextEscapes(part), hasDiameterPrefix: false, modifier: MaterialModifier.None, symbol: GdtSymbol.Unknown);
		}

		private static Compartment parseToleranceCompartment(string part)
		{
			string value = part ?? string.Empty;
			bool hasDia = false;

			if (value.StartsWith("%%c", StringComparison.OrdinalIgnoreCase))
			{
				hasDia = true;
				value = value.Substring(3);
			}

			MaterialModifier modifier = consumeTrailingModifier(ref value);
			value = decodeTextEscapes(value);
			return new Compartment(CompartmentKind.Tolerance, value, hasDia, modifier, GdtSymbol.Unknown);
		}

		private static Compartment parseDatumCompartment(string part)
		{
			string value = part ?? string.Empty;
			MaterialModifier modifier = consumeTrailingModifier(ref value);
			value = decodeTextEscapes(value);
			return new Compartment(CompartmentKind.Datum, value, hasDiameterPrefix: false, modifier: modifier, symbol: GdtSymbol.Unknown);
		}

		private static MaterialModifier consumeTrailingModifier(ref string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return MaterialModifier.None;
			}

			string trimmed = value.TrimEnd();
			if (trimmed.EndsWith("%%cm", StringComparison.OrdinalIgnoreCase))
			{
				value = trimmed.Substring(0, trimmed.Length - 4);
				return MaterialModifier.MMC;
			}
			if (trimmed.EndsWith("%%cl", StringComparison.OrdinalIgnoreCase))
			{
				value = trimmed.Substring(0, trimmed.Length - 4);
				return MaterialModifier.LMC;
			}
			if (trimmed.EndsWith("%%cs", StringComparison.OrdinalIgnoreCase))
			{
				value = trimmed.Substring(0, trimmed.Length - 4);
				return MaterialModifier.RFS;
			}
			if (trimmed.EndsWith("%%cp", StringComparison.OrdinalIgnoreCase))
			{
				value = trimmed.Substring(0, trimmed.Length - 4);
				return MaterialModifier.Projected;
			}

			value = trimmed;
			return MaterialModifier.None;
		}

		private static string decodeTextEscapes(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}

			// Keep output ASCII-friendly (the PDF backend is not Unicode-aware).
			return value
				.Replace("%%p", "+/-", StringComparison.OrdinalIgnoreCase)
				.Replace("%%d", "deg", StringComparison.OrdinalIgnoreCase);
		}

		private static bool tryParseGdtSymbol(string value, out char code)
		{
			code = '\0';
			if (string.IsNullOrWhiteSpace(value))
			{
				return false;
			}

			string v = value.Trim();
			int idx = v.IndexOf("Fgdt;", StringComparison.OrdinalIgnoreCase);
			if (idx < 0 || idx + 5 >= v.Length)
			{
				return false;
			}

			char c = v[idx + 5];
			if (c == '}' || c == ';')
			{
				return false;
			}

			code = c;
			return true;
		}

		private static GdtSymbol mapGdtSymbol(char c)
		{
			switch (char.ToLowerInvariant(c))
			{
				case 'j':
					return GdtSymbol.Position;
				case 'e':
					return GdtSymbol.Flatness;
				case 'a':
					return GdtSymbol.Straightness;
				case 'g':
					return GdtSymbol.Circularity;
				case 'h':
					return GdtSymbol.Cylindricity;
				case 'b':
					return GdtSymbol.Perpendicularity;
				case 'f':
					return GdtSymbol.Parallelism;
				case 'd':
					return GdtSymbol.Angularity;
				case 'r':
					return GdtSymbol.CircularRunout;
				case 't':
					return GdtSymbol.TotalRunout;
				case 'i':
					return GdtSymbol.Concentricity;
				case 'k':
					return GdtSymbol.Symmetry;
				case 'c':
					return GdtSymbol.ProfileOfALine;
				default:
					return GdtSymbol.Unknown;
			}
		}

		private static char modifierLetter(MaterialModifier modifier)
		{
			switch (modifier)
			{
				case MaterialModifier.MMC:
					return 'M';
				case MaterialModifier.LMC:
					return 'L';
				case MaterialModifier.RFS:
					return 'S';
				case MaterialModifier.Projected:
					return 'P';
				default:
					return '\0';
			}
		}

		private static double estimateTextWidth(string text, double height)
		{
			if (string.IsNullOrEmpty(text) || height <= Epsilon)
			{
				return 0.0;
			}

			// Keep consistent with TextLayoutEngine's internal ApproximateTextMetrics.
			const double averageGlyphWidth = 0.55;
			return text.Length * averageGlyphWidth * height;
		}

		private static double dimensionScale(DimensionStyle style)
		{
			if (style == null || style.ScaleFactor <= Epsilon)
			{
				return 1.0;
			}

			return style.ScaleFactor;
		}

		private static XY frameToWorld(XY origin, XY xAxis, XY yAxis, double x, double y)
		{
			return new XY(
				origin.X + (xAxis.X * x) + (yAxis.X * y),
				origin.Y + (xAxis.Y * x) + (yAxis.Y * y));
		}

		private static XY rotate(XY value, double angle)
		{
			double c = Math.Cos(angle);
			double s = Math.Sin(angle);
			return new XY(
				value.X * c - value.Y * s,
				value.X * s + value.Y * c);
		}

		private static XY perpendicularLeft(XY value)
		{
			return new XY(-value.Y, value.X);
		}

		private static bool tryNormalize(XY value, out XY normalized)
		{
			double len = Math.Sqrt(value.X * value.X + value.Y * value.Y);
			if (len < 1e-12)
			{
				normalized = XY.Zero;
				return false;
			}

			normalized = new XY(value.X / len, value.Y / len);
			return true;
		}

		private static bool isFiniteNumber(double value)
		{
			return !double.IsNaN(value) && !double.IsInfinity(value);
		}

		private static double distance(XY a, XY b)
		{
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;
			return Math.Sqrt(dx * dx + dy * dy);
		}

		private sealed class FeatureControlFrame
		{
			public IReadOnlyList<FrameRow> Rows { get; }

			public FeatureControlFrame(IReadOnlyList<FrameRow> rows)
			{
				this.Rows = rows ?? Array.Empty<FrameRow>();
			}
		}

		private sealed class FrameRow
		{
			public IReadOnlyList<Compartment> Compartments { get; }

			public FrameRow(IReadOnlyList<Compartment> compartments)
			{
				this.Compartments = compartments ?? Array.Empty<Compartment>();
			}
		}

		private sealed class Compartment
		{
			public CompartmentKind Kind { get; }
			public string Text { get; }
			public bool HasDiameterPrefix { get; }
			public MaterialModifier Modifier { get; }
			public GdtSymbol Symbol { get; }

			public Compartment(CompartmentKind kind, string text, bool hasDiameterPrefix, MaterialModifier modifier, GdtSymbol symbol)
			{
				this.Kind = kind;
				this.Text = text ?? string.Empty;
				this.HasDiameterPrefix = hasDiameterPrefix;
				this.Modifier = modifier;
				this.Symbol = symbol;
			}
		}

		private enum CompartmentKind
		{
			Symbol = 0,
			Tolerance = 1,
			Datum = 2,
			Text = 3,
		}

		private enum MaterialModifier
		{
			None = 0,
			MMC = 1,
			LMC = 2,
			RFS = 3,
			Projected = 4,
		}

		private enum GdtSymbol
		{
			Unknown = 0,
			Position = 1,
			Flatness = 2,
			Straightness = 3,
			Circularity = 4,
			Cylindricity = 5,
			Perpendicularity = 6,
			Parallelism = 7,
			Angularity = 8,
			CircularRunout = 9,
			TotalRunout = 10,
			Concentricity = 11,
			Symmetry = 12,
			ProfileOfALine = 13,
		}
	}
}
