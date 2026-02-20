using ACadSharp.Entities;
using ACadSharp.Extensions;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Pdf.Core.Render.Style;
using ACadSharp.Pdf.Core.Render.Text;
using ACadSharp.Pdf.Core.Render.Transforms;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ACadSharp.Pdf.Core.Render.SceneGraph
{
	internal sealed class SceneGraphBuilder
	{
		private readonly Layout _layout;
		private readonly PdfConfiguration _configuration;
		private readonly PropertyResolver _resolver;
		private readonly RenderLog _log;
		private readonly BlockExpander _blockExpander;
		private readonly TextLayoutEngine _textLayout;
		private readonly HatchPatternGenerator _hatchGenerator;
		private readonly MLineOffsetRenderer _mlineRenderer;
		private readonly UnderlayRasterCache _underlayRasterCache;
		private readonly ToleranceFrameRenderer _toleranceRenderer;

		private readonly struct InsertRenderContext
		{
			public ACadSharp.Color ByBlockColor { get; }
			public LineWeightType ByBlockLineWeight { get; }
			public LineType ByBlockLineType { get; }
			public Layer InsertLayer { get; }

			public InsertRenderContext(ACadSharp.Color byBlockColor, LineWeightType byBlockLineWeight, LineType byBlockLineType, Layer insertLayer)
			{
				this.ByBlockColor = byBlockColor;
				this.ByBlockLineWeight = byBlockLineWeight;
				this.ByBlockLineType = byBlockLineType;
				this.InsertLayer = insertLayer;
			}
		}

		public SceneGraphBuilder(Layout layout, PdfConfiguration configuration, PropertyResolver resolver, RenderLog log)
		{
			this._layout = layout ?? throw new ArgumentNullException(nameof(layout));
			this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
			this._resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
			this._log = log ?? throw new ArgumentNullException(nameof(log));
			this._blockExpander = new BlockExpander(log);
			this._textLayout = new TextLayoutEngine(layout, configuration, log);
			this._hatchGenerator = new HatchPatternGenerator(configuration, log);
			this._mlineRenderer = new MLineOffsetRenderer(layout, configuration, resolver, log);
			this._underlayRasterCache = new UnderlayRasterCache(configuration);
			this._toleranceRenderer = new ToleranceFrameRenderer(layout, configuration, resolver, log, this._textLayout);
		}

		public IReadOnlyList<RenderNode> Build(IReadOnlyList<Viewport> viewports, IReadOnlyList<Entity> paperEntities)
		{
			var nodes = new List<RenderNode>();

			if (viewports != null)
			{
				foreach (var vp in viewports)
				{
					var n = buildViewport(vp);
					if (n != null)
					{
						nodes.Add(n);
					}
				}
			}

			if (paperEntities != null)
			{
				foreach (var e in paperEntities)
				{
					var en = buildEntityNode(
						e,
						viewport: null,
						styleScaleToPaper: 1.0,
						textScaleToPaper: 1.0,
						containingInsert: null,
						parentTransform: Matrix4.Identity,
						depth: 0,
						activeBlocks: null);
					if (en != null)
					{
						nodes.Add(en);
					}
				}
			}

			return nodes;
		}

		private RenderNode buildViewport(Viewport viewport)
		{
			if (viewport == null)
			{
				return null;
			}

			// Clip rectangle in paper space
			BoundingBox box = viewport.GetBoundingBox();
			var clipPath = rectanglePath(viewport.Handle, (XY)box.Min, (XY)box.Max);

			Matrix4 modelToPaper = TransformHelper.ViewportModelToPaper(viewport);

			if (viewport.TwistAngle != 0)
			{
				this._log.Add(viewport.Handle, viewport.SubclassMarker, RenderStatus.Rendered, "Viewport twist angle ignored in Stage 00.");
			}

			if (viewport.ViewDirection != XYZ.AxisZ && viewport.ViewDirection != XYZ.Zero)
			{
				this._log.Add(viewport.Handle, viewport.SubclassMarker, RenderStatus.Rendered, "Viewport view direction not supported (orthographic top-view only); rendering as if top-view.");
			}

			var children = new List<RenderNode>();
			List<Entity> modelEntities;
			try
			{
				modelEntities = selectViewportEntities(viewport);
			}
			catch (Exception ex)
			{
				this._log.Add(viewport.Handle, viewport.SubclassMarker, RenderStatus.Error, $"Viewport entity selection failed: {ex.Message}");
				return null;
			}

			foreach (var e in modelEntities)
			{
				var child = buildEntityNode(
					e,
					viewport,
					styleScaleToPaper: viewport.ScaleFactor,
					textScaleToPaper: viewport.ScaleFactor,
					containingInsert: null,
					parentTransform: Matrix4.Identity,
					depth: 0,
					activeBlocks: null);
				if (child != null)
				{
					children.Add(child);
				}
			}

			var group = new GroupNode(viewport.Handle, modelToPaper, children);
			return new ClipNode(viewport.Handle, clipPath, new[] { group });
		}

		private List<Entity> selectViewportEntities(Viewport viewport)
		{
			if (viewport == null)
			{
				return new List<Entity>();
			}

			if (viewport.Document == null)
			{
				throw new InvalidOperationException("Viewport needs to be assigned to a document.");
			}

			BoundingBox viewBox = viewport.GetModelBoundingBox();

			BoundingBox clipRect = viewBox;
			if (tryCreateExpandedClipRect((XY)viewBox.Min, (XY)viewBox.Max, out BoundingBox expanded))
			{
				clipRect = expanded;
			}

			var entities = new List<Entity>();
			foreach (Entity entity in viewport.Document.Entities)
			{
				if (entity == null)
				{
					continue;
				}

				if (entity is Ray ray)
				{
					XY origin = new XY(ray.StartPoint.X, ray.StartPoint.Y);
					XY direction = new XY(ray.Direction.X, ray.Direction.Y);
					var clipped = InfiniteLineClipper.ClipRay(origin, direction, clipRect);
					if (clipped.HasValue)
					{
						entities.Add(entity);
					}
					else
					{
						this._log.Add(ray.Handle, ray.SubclassMarker, RenderStatus.Skipped, "RAY outside viewport clip rectangle.");
					}
					continue;
				}

				if (entity is XLine xline)
				{
					XY origin = new XY(xline.FirstPoint.X, xline.FirstPoint.Y);
					XY direction = new XY(xline.Direction.X, xline.Direction.Y);
					var clipped = InfiniteLineClipper.ClipXLine(origin, direction, clipRect);
					if (clipped.HasValue)
					{
						entities.Add(entity);
					}
					else
					{
						this._log.Add(xline.Handle, xline.SubclassMarker, RenderStatus.Skipped, "XLINE outside viewport clip rectangle.");
					}
					continue;
				}

				BoundingBox box = entity.GetBoundingBox();
				if (box.Extent == BoundingBoxExtent.Infinite)
				{
					// Allow INSERTs with infinite child bounds (e.g., RAY/XLINE inside a block) through coarse selection.
					if (entity is Insert)
					{
						entities.Add(entity);
					}
					continue;
				}

				if (viewBox.IsIn(box, out bool partialIn) || partialIn)
				{
					entities.Add(entity);
				}
			}

			return entities;
		}

			private RenderNode buildEntityNode(
				Entity entity,
				Viewport viewport,
				double styleScaleToPaper,
				double textScaleToPaper,
				InsertRenderContext? containingInsert,
				Matrix4 parentTransform,
				int depth,
				HashSet<string> activeBlocks)
			{
			if (entity == null)
			{
				return null;
			}

			HashSet<string> stack = activeBlocks ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				try
				{
					Entity prepared = applyLayerZeroInheritance(entity, containingInsert);

					if (prepared is Insert insert)
					{
						Layer effectiveInsertLayer = resolveEffectiveLayer(insert.Layer, containingInsert);
						var visInsert = getVisibilityWithLayer(insert, effectiveInsertLayer, viewport);
						if (visInsert != VisibilityDecision.Visible)
						{
							this._log.Add(insert.Handle, insert.SubclassMarker, RenderStatus.Skipped, $"Visibility gate: {visInsert}");
							return null;
						}

						return buildInsert(insert, effectiveInsertLayer, viewport, styleScaleToPaper, textScaleToPaper, containingInsert, parentTransform, depth, stack);
					}

					var vis = this._resolver.GetVisibility(prepared, viewport);
					if (vis != VisibilityDecision.Visible)
					{
						this._log.Add(prepared.Handle, prepared.SubclassMarker, RenderStatus.Skipped, $"Visibility gate: {vis}");
						return null;
					}

					if (prepared is MultiLeader mleader)
					{
						return buildMultiLeader(mleader, viewport, styleScaleToPaper, textScaleToPaper, containingInsert, parentTransform, depth, stack);
					}

				if (!isIdentity(parentTransform) && !(prepared is Ray) && !(prepared is XLine))
				{
					prepared = cloneWithTransform(prepared, parentTransform);
				}

					switch (prepared)
					{
						case Line line:
							return buildLine(line, styleScaleToPaper, containingInsert);
						case Ray ray:
							return buildRay(ray, viewport, styleScaleToPaper, containingInsert, parentTransform);
						case XLine xline:
							return buildXLine(xline, viewport, styleScaleToPaper, containingInsert, parentTransform);
						case Arc arc:
							return buildArc(arc, styleScaleToPaper, containingInsert);
						case Circle circle:
							return buildCircle(circle, styleScaleToPaper, containingInsert);
						case Ellipse ellipse:
							return buildEllipse(ellipse, styleScaleToPaper, containingInsert);
						case Point point:
							// Points are a constant paper-size dot; only viewport scaling should affect compensation.
							return buildPoint(point, textScaleToPaper, containingInsert);
						case IPolyline polyline:
							return buildPolyline(polyline, styleScaleToPaper, containingInsert);
						case Hatch hatch:
							return buildHatch(hatch, styleScaleToPaper, containingInsert);
						case MLine mline:
							return this._mlineRenderer.Render(
								mline,
								styleScaleToPaper,
								containingInsert?.ByBlockColor,
								containingInsert?.ByBlockLineWeight,
								containingInsert?.ByBlockLineType);
						case RasterImage rasterImage:
							return buildRasterImage(rasterImage);
						case PdfUnderlay pdfUnderlay:
							return buildPdfUnderlay(pdfUnderlay, styleScaleToPaper);
						case TextEntity text:
							return buildText(text, textScaleToPaper, containingInsert);
						case MText mtext:
							return buildMText(mtext, textScaleToPaper, containingInsert);
						case Tolerance tolerance:
							return this._toleranceRenderer.Render(
								tolerance,
								styleScaleToPaper,
								textScaleToPaper,
								containingInsert?.ByBlockColor,
								containingInsert?.ByBlockLineWeight,
								containingInsert?.ByBlockLineType);
						case Dimension dimension:
							return buildDimension(dimension, viewport, styleScaleToPaper, textScaleToPaper, containingInsert, parentTransform, depth, stack);
					default:
						this._log.Add(prepared.Handle, prepared.SubclassMarker, RenderStatus.NotImplemented, "Entity not supported in Stage 00 frontend.");
						this._configuration.Notify($"[{prepared.SubclassMarker}] Drawing not implemented (scene graph pipeline).", NotificationType.NotImplemented);
						return null;
				}
			}
			catch (Exception ex)
			{
				this._log.Add(entity.Handle, entity.SubclassMarker, RenderStatus.Error, ex.Message);
				this._configuration.Notify($"[{entity.SubclassMarker}] Scene-graph render failed: {ex.Message}", NotificationType.Warning, ex);
				return null;
			}
		}

			private RenderNode buildInsert(
				Insert insert,
				Layer effectiveInsertLayer,
				Viewport viewport,
				double styleScaleToPaper,
				double textScaleToPaper,
				InsertRenderContext? containingInsert,
				Matrix4 parentTransform,
				int depth,
				HashSet<string> activeBlocks)
			{
				if (!this._blockExpander.TryEnter(insert, depth, activeBlocks, out BlockRecord block, out string blockKey))
				{
					return null;
				}

			try
			{
				IReadOnlyList<Matrix4> cellTransforms = this._blockExpander.ComputeCellTransforms(insert, parentTransform);
					if (cellTransforms.Count == 0)
					{
						return null;
					}

					double childStyleScale = styleScaleToPaper * this._blockExpander.ComputeInsertScaleFactor(insert);
					InsertRenderContext childContext = createInsertContext(insert, effectiveInsertLayer, containingInsert);
					var nodes = new List<RenderNode>();

				foreach (var cellTransform in cellTransforms)
				{
					foreach (Entity blockEntity in block.Entities)
					{
						if (blockEntity is AttributeDefinition)
						{
							continue;
						}

						var node = buildEntityNode(
							blockEntity,
							viewport,
							childStyleScale,
							textScaleToPaper,
							childContext,
							cellTransform,
							depth + 1,
							activeBlocks);
						if (node != null)
						{
							nodes.Add(node);
						}
					}

					foreach (var attNode in buildInsertAttributes(insert, block, viewport, childStyleScale, textScaleToPaper, childContext, cellTransform, depth + 1, activeBlocks))
					{
						nodes.Add(attNode);
					}
				}

				if (nodes.Count == 0)
				{
					this._log.Add(insert.Handle, insert.SubclassMarker, RenderStatus.Skipped, $"Expanded block '{block.Name}' has no visible entities.");
					return null;
				}

				this._log.Add(insert.Handle, insert.SubclassMarker, RenderStatus.Rendered, $"Expanded block '{block.Name}' ({nodes.Count} node(s)).");
				return new GroupNode(insert.Handle, Matrix4.Identity, nodes);
			}
			finally
			{
				this._blockExpander.Leave(blockKey, activeBlocks);
			}
		}

		private IReadOnlyList<RenderNode> buildInsertAttributes(
			Insert insert,
			BlockRecord block,
			Viewport viewport,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext childContext,
			Matrix4 cellTransform,
			int depth,
			HashSet<string> activeBlocks)
		{
			if (insert.Attributes == null || insert.Attributes.Count == 0)
			{
				return Array.Empty<RenderNode>();
			}

			var nodes = new List<RenderNode>();
			foreach (AttributeEntity att in insert.Attributes)
			{
				if (att == null)
				{
					continue;
				}

				if (att.IsInvisible || att.Flags.HasFlag(AttributeFlags.Hidden))
				{
					this._log.Add(att.Handle, att.SubclassMarker, RenderStatus.Skipped, "ATTRIB hidden/invisible.");
					continue;
				}

				AttributeEntity renderAtt = att.CloneTyped();
				AttributeDefinition def = findAttributeDefinition(block, att.Tag);
				if (string.IsNullOrEmpty(renderAtt.Value) && def != null)
				{
					renderAtt.Value = def.Value ?? string.Empty;
				}

					if (string.IsNullOrEmpty(renderAtt.Value))
					{
						if (renderAtt.MText == null)
						{
							this._log.Add(renderAtt.Handle, renderAtt.SubclassMarker, RenderStatus.Skipped, "ATTRIB value is empty.");
							continue;
						}
					}

					Entity renderEntity = renderAtt;
					if (renderAtt.MText != null)
					{
						MText mtext = this._textLayout.BuildAttributeMText(renderAtt);
						if (mtext != null)
						{
							renderEntity = mtext;
						}
					}

					var node = buildEntityNode(
						renderEntity,
						viewport,
						styleScaleToPaper,
						textScaleToPaper,
						childContext,
						cellTransform,
						depth,
						activeBlocks);
				if (node != null)
				{
					nodes.Add(node);
				}
			}

			return nodes;
		}

		private static AttributeDefinition findAttributeDefinition(BlockRecord block, string tag)
		{
			if (block == null || string.IsNullOrWhiteSpace(tag))
			{
				return null;
			}

			foreach (AttributeDefinition def in block.AttributeDefinitions)
			{
				if (string.Equals(def.Tag, tag, StringComparison.OrdinalIgnoreCase))
				{
					return def;
				}
			}

			return null;
		}

			private InsertRenderContext createInsertContext(Insert insert, Layer effectiveLayer, InsertRenderContext? parentContext)
			{
				ACadSharp.Color resolvedColor = resolveColorForInsertContext(insert, effectiveLayer, parentContext);
				LineWeightType resolvedLw = resolveLineWeightForInsertContext(insert, effectiveLayer, parentContext);
				LineType resolvedLt = resolveLineTypeForInsertContext(insert, effectiveLayer, parentContext);

				return new InsertRenderContext(resolvedColor, resolvedLw, resolvedLt, effectiveLayer);
			}

			private static Entity applyLayerZeroInheritance(Entity entity, InsertRenderContext? context)
			{
				if (!context.HasValue)
				{
					return entity;
			}

			if (context.Value.InsertLayer == null || entity.Layer == null)
			{
				return entity;
			}

			if (!isLayerZero(entity.Layer.Name))
			{
				return entity;
			}

			// Cloning INSERT can recursively clone its owned block graph; keep INSERT untouched here.
			if (entity is Insert)
			{
				return entity;
			}

			Entity clone = entity.CloneTyped();
			clone.Layer = context.Value.InsertLayer;
			return clone;
		}

		private static Entity cloneWithTransform(Entity entity, Matrix4 transform)
		{
			Entity clone = entity.CloneTyped();
			if (clone is CadWipeoutBase wipeout)
			{
				wipeout.InsertPoint = transform * wipeout.InsertPoint;
				wipeout.UVector = transformDirection(transform, wipeout.UVector);
				wipeout.VVector = transformDirection(transform, wipeout.VVector);
				return clone;
			}

			clone.ApplyTransform(new Transform(transform));
			return clone;
		}

		private static XYZ transformDirection(Matrix4 matrix, XYZ vector)
		{
			XYZM r = matrix * new XYZM(vector.X, vector.Y, vector.Z, 0.0);
			return new XYZ(r.X, r.Y, r.Z);
		}

			private StrokeStyle resolveStroke(Entity entity, double styleScaleToPaper, InsertRenderContext? containingInsert)
			{
				ACadSharp.Color? byBlockColor = containingInsert?.ByBlockColor;
				LineWeightType? byBlockLineWeight = containingInsert?.ByBlockLineWeight;
				LineType byBlockLineType = containingInsert?.ByBlockLineType;
				return this._resolver.ResolveStroke(entity, this._layout, styleScaleToPaper, byBlockColor, byBlockLineWeight, byBlockLineType);
			}

			private static Layer resolveEffectiveLayer(Layer entityLayer, InsertRenderContext? context)
			{
				if (entityLayer == null || !context.HasValue)
				{
					return entityLayer;
				}

				if (!isLayerZero(entityLayer.Name))
				{
					return entityLayer;
				}

				return context.Value.InsertLayer ?? entityLayer;
			}

			private VisibilityDecision getVisibilityWithLayer(Entity entity, Layer effectiveLayer, Viewport viewport)
			{
				if (entity == null) throw new ArgumentNullException(nameof(entity));

				if (entity.IsInvisible)
				{
					return VisibilityDecision.InvisibleFlag;
				}

				Layer layer = effectiveLayer ?? entity.Layer;
				if (layer != null)
				{
					if (!layer.IsOn)
					{
						return VisibilityDecision.LayerOff;
					}

					if (layer.Flags.HasFlag(LayerFlags.Frozen))
					{
						return VisibilityDecision.LayerFrozen;
					}

					if (!layer.PlotFlag)
					{
						return VisibilityDecision.LayerNotPlottable;
					}

					if (viewport != null && viewport.FrozenLayers != null && viewport.FrozenLayers.Count > 0)
					{
						if (viewport.FrozenLayers.Any(l => string.Equals(l?.Name, layer.Name, StringComparison.OrdinalIgnoreCase)))
						{
							return VisibilityDecision.ViewportFrozenLayer;
						}
					}
				}

				return VisibilityDecision.Visible;
			}

			private static ACadSharp.Color resolveColorForInsertContext(Insert insert, Layer effectiveLayer, InsertRenderContext? parentContext)
			{
				ACadSharp.Color color = insert.Color;

				if (color.IsTrueColor)
				{
					return mapAci7(color);
				}

				if (!color.IsByLayer && !color.IsByBlock && color.Index > 0)
				{
					return mapAci7(color);
				}

				if (color.IsByLayer)
				{
					return mapAci7(effectiveLayer?.Color ?? ACadSharp.Color.Default);
				}

				// ByBlock
				if (parentContext.HasValue)
				{
					return mapAci7(parentContext.Value.ByBlockColor);
				}

				return mapAci7(effectiveLayer?.Color ?? ACadSharp.Color.Default);
			}

			private static LineWeightType resolveLineWeightForInsertContext(Insert insert, Layer effectiveLayer, InsertRenderContext? parentContext)
			{
				LineWeightType lw = insert.LineWeight;
				switch (lw)
				{
					case LineWeightType.ByLayer:
						return effectiveLayer?.LineWeight ?? LineWeightType.Default;
					case LineWeightType.ByBlock:
						{
							LineWeightType resolved = parentContext?.ByBlockLineWeight
								?? (insert.Owner is BlockRecord record ? record.BlockEntity.LineWeight : LineWeightType.Default);
							if (resolved == LineWeightType.ByBlock)
							{
								resolved = LineWeightType.Default;
							}
							return resolved;
						}
					case LineWeightType.ByDIPs:
					case LineWeightType.Default:
						return LineWeightType.Default;
					default:
						return lw;
				}
			}

			private static LineType resolveLineTypeForInsertContext(Insert insert, Layer effectiveLayer, InsertRenderContext? parentContext)
			{
				LineType lt = insert.LineType ?? LineType.Continuous;

				if (string.Equals(lt.Name, LineType.ByLayerName, StringComparison.InvariantCultureIgnoreCase))
				{
					return effectiveLayer?.LineType ?? LineType.Continuous;
				}

				if (string.Equals(lt.Name, LineType.ByBlockName, StringComparison.InvariantCultureIgnoreCase))
				{
					return parentContext?.ByBlockLineType ?? LineType.Continuous;
				}

				return lt;
			}

			private static ACadSharp.Color mapAci7(ACadSharp.Color color)
			{
				if (!color.IsTrueColor && color.Index == 7)
				{
					return new ACadSharp.Color(0, 0, 0);
				}

				return color;
			}

			private static bool isLayerZero(string name)
			{
				return string.Equals(name, Layer.DefaultName, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(name, "0", StringComparison.OrdinalIgnoreCase);
		}

		private static bool isIdentity(Matrix4 matrix)
		{
			const double eps = 1e-12;
			return
				Math.Abs(matrix.M00 - 1.0) < eps &&
				Math.Abs(matrix.M11 - 1.0) < eps &&
				Math.Abs(matrix.M22 - 1.0) < eps &&
				Math.Abs(matrix.M33 - 1.0) < eps &&
				Math.Abs(matrix.M01) < eps &&
				Math.Abs(matrix.M02) < eps &&
				Math.Abs(matrix.M03) < eps &&
				Math.Abs(matrix.M10) < eps &&
				Math.Abs(matrix.M12) < eps &&
				Math.Abs(matrix.M13) < eps &&
				Math.Abs(matrix.M20) < eps &&
				Math.Abs(matrix.M21) < eps &&
				Math.Abs(matrix.M23) < eps &&
				Math.Abs(matrix.M30) < eps &&
				Math.Abs(matrix.M31) < eps &&
				Math.Abs(matrix.M32) < eps;
		}

		private PathNode buildLine(Line line, double styleScaleToPaper, InsertRenderContext? containingInsert)
		{
			var stroke = resolveStroke(line, styleScaleToPaper, containingInsert);
			var segs = new PathSegment[]
			{
				new MoveTo((XY)line.StartPoint),
				new LineTo((XY)line.EndPoint),
			};

			this._log.Add(line.Handle, line.SubclassMarker, RenderStatus.Rendered, "Rendered as Path.");
			return new PathNode(line.Handle, segs, stroke, fill: null);
		}

		private RenderNode buildRay(
			Ray ray,
			Viewport viewport,
			double styleScaleToPaper,
			InsertRenderContext? containingInsert,
			Matrix4 parentTransform)
		{
			XY origin = transformPointToXY(ray.StartPoint, parentTransform);
			XYZ directionWorld = transformDirection(parentTransform, ray.Direction);
			XY direction = new XY(directionWorld.X, directionWorld.Y);

			if (!isFiniteNumber(origin.X) || !isFiniteNumber(origin.Y)
				|| !isFiniteNumber(direction.X) || !isFiniteNumber(direction.Y)
				|| (Math.Abs(direction.X) <= 1e-12 && Math.Abs(direction.Y) <= 1e-12))
			{
				this._log.Add(ray.Handle, ray.SubclassMarker, RenderStatus.Skipped, "RAY has invalid or zero direction.");
				return null;
			}

			BoundingBox clipRect = getInfiniteLineClipRect(viewport);
			var clipped = InfiniteLineClipper.ClipRay(origin, direction, clipRect);
			if (!clipped.HasValue)
			{
				this._log.Add(ray.Handle, ray.SubclassMarker, RenderStatus.Skipped, "RAY outside clip rectangle.");
				return null;
			}

			var stroke = resolveStroke(ray, styleScaleToPaper, containingInsert);
			PathNode path = createLinePath(ray.Handle, clipped.Value.Start, clipped.Value.End, stroke);
			if (path == null)
			{
				this._log.Add(ray.Handle, ray.SubclassMarker, RenderStatus.Skipped, "RAY clipped to degenerate segment.");
				return null;
			}

			this._log.Add(ray.Handle, ray.SubclassMarker, RenderStatus.Rendered, "Rendered as clipped ray segment.");
			return path;
		}

		private RenderNode buildXLine(
			XLine xline,
			Viewport viewport,
			double styleScaleToPaper,
			InsertRenderContext? containingInsert,
			Matrix4 parentTransform)
		{
			XY origin = transformPointToXY(xline.FirstPoint, parentTransform);
			XYZ directionWorld = transformDirection(parentTransform, xline.Direction);
			XY direction = new XY(directionWorld.X, directionWorld.Y);

			if (!isFiniteNumber(origin.X) || !isFiniteNumber(origin.Y)
				|| !isFiniteNumber(direction.X) || !isFiniteNumber(direction.Y)
				|| (Math.Abs(direction.X) <= 1e-12 && Math.Abs(direction.Y) <= 1e-12))
			{
				this._log.Add(xline.Handle, xline.SubclassMarker, RenderStatus.Skipped, "XLINE has invalid or zero direction.");
				return null;
			}

			BoundingBox clipRect = getInfiniteLineClipRect(viewport);
			var clipped = InfiniteLineClipper.ClipXLine(origin, direction, clipRect);
			if (!clipped.HasValue)
			{
				this._log.Add(xline.Handle, xline.SubclassMarker, RenderStatus.Skipped, "XLINE outside clip rectangle.");
				return null;
			}

			var stroke = resolveStroke(xline, styleScaleToPaper, containingInsert);
			PathNode path = createLinePath(xline.Handle, clipped.Value.Start, clipped.Value.End, stroke);
			if (path == null)
			{
				this._log.Add(xline.Handle, xline.SubclassMarker, RenderStatus.Skipped, "XLINE clipped to degenerate segment.");
				return null;
			}

			this._log.Add(xline.Handle, xline.SubclassMarker, RenderStatus.Rendered, "Rendered as clipped xline segment.");
			return path;
		}

		private BoundingBox getInfiniteLineClipRect(Viewport viewport)
		{
			if (viewport != null)
			{
				BoundingBox viewportBox = viewport.GetModelBoundingBox();
				if (tryCreateExpandedClipRect((XY)viewportBox.Min, (XY)viewportBox.Max, out BoundingBox expandedViewport))
				{
					return expandedViewport;
				}
			}

			if (this._layout.IsPaperSpace)
			{
				if (tryCreateExpandedClipRect(new XY(0.0, 0.0), new XY(this._layout.PaperWidth, this._layout.PaperHeight), out BoundingBox paperRect))
				{
					return paperRect;
				}
			}

			if (tryCreateExpandedClipRect((XY)this._layout.MinExtents, (XY)this._layout.MaxExtents, out BoundingBox extentsRect))
			{
				return extentsRect;
			}

			if (tryCreateExpandedClipRect(this._layout.MinLimits, this._layout.MaxLimits, out BoundingBox limitsRect))
			{
				return limitsRect;
			}

			return new BoundingBox(new XYZ(-10000.0, -10000.0, 0.0), new XYZ(10000.0, 10000.0, 0.0));
		}

		private static bool tryCreateExpandedClipRect(XY min, XY max, out BoundingBox clipRect)
		{
			clipRect = BoundingBox.Null;

			if (!isFiniteNumber(min.X) || !isFiniteNumber(min.Y) || !isFiniteNumber(max.X) || !isFiniteNumber(max.Y))
			{
				return false;
			}

			double minX = Math.Min(min.X, max.X);
			double minY = Math.Min(min.Y, max.Y);
			double maxX = Math.Max(min.X, max.X);
			double maxY = Math.Max(min.Y, max.Y);

			double width = maxX - minX;
			double height = maxY - minY;

			if (width <= 1e-9 && height <= 1e-9)
			{
				return false;
			}

			double margin = Math.Max(width, height) * 0.02;
			if (!isFiniteNumber(margin) || margin <= 0.0)
			{
				margin = 1.0;
			}

			clipRect = new BoundingBox(
				new XYZ(minX - margin, minY - margin, 0.0),
				new XYZ(maxX + margin, maxY + margin, 0.0));
			return true;
		}

		private PathNode buildPolyline(IPolyline polyline, double styleScaleToPaper, InsertRenderContext? containingInsert)
		{
			Entity polyEntity = (Entity)polyline;
			var pts = polyline.GetPoints<XYZ>(this._configuration.ArcPrecision).ToArray();
			if (pts.Length < 2)
			{
				this._log.Add(polyEntity.Handle, polyEntity.SubclassMarker, RenderStatus.Skipped, "Degenerate polyline.");
				return null;
			}

			var stroke = resolveStroke(polyEntity, styleScaleToPaper, containingInsert);
			var segs = new List<PathSegment>(pts.Length + 2);
			segs.Add(new MoveTo((XY)pts[0]));
			for (int i = 1; i < pts.Length; i++)
			{
				segs.Add(new LineTo((XY)pts[i]));
			}

			if (polyline.IsClosed)
			{
				segs.Add(new ClosePath());
			}

			this._log.Add(polyEntity.Handle, polyEntity.SubclassMarker, RenderStatus.Rendered, "Rendered as Path.");
			return new PathNode(polyEntity.Handle, segs, stroke, fill: null);
		}

		private PathNode buildArc(Arc arc, double styleScaleToPaper, InsertRenderContext? containingInsert)
		{
			var pts = arc.PolygonalVertexes(this._configuration.ArcPrecision).ToArray();
			if (pts.Length < 2)
			{
				this._log.Add(arc.Handle, arc.SubclassMarker, RenderStatus.Skipped, "Degenerate arc.");
				return null;
			}

			var stroke = resolveStroke(arc, styleScaleToPaper, containingInsert);
			var segs = new List<PathSegment>(pts.Length);
			segs.Add(new MoveTo((XY)pts[0]));
			for (int i = 1; i < pts.Length; i++)
			{
				segs.Add(new LineTo((XY)pts[i]));
			}

			this._log.Add(arc.Handle, arc.SubclassMarker, RenderStatus.Rendered, "Rendered as polygonal Path (Stage 00).");
			return new PathNode(arc.Handle, segs, stroke, fill: null);
		}

		private PathNode buildEllipse(Ellipse ellipse, double styleScaleToPaper, InsertRenderContext? containingInsert)
		{
			var pts = ellipse.PolygonalVertexes(this._configuration.ArcPrecision).ToArray();
			if (pts.Length < 2)
			{
				this._log.Add(ellipse.Handle, ellipse.SubclassMarker, RenderStatus.Skipped, "Degenerate ellipse.");
				return null;
			}

			var stroke = resolveStroke(ellipse, styleScaleToPaper, containingInsert);
			var segs = new List<PathSegment>(pts.Length);
			segs.Add(new MoveTo((XY)pts[0]));
			for (int i = 1; i < pts.Length; i++)
			{
				segs.Add(new LineTo((XY)pts[i]));
			}

			this._log.Add(ellipse.Handle, ellipse.SubclassMarker, RenderStatus.Rendered, "Rendered as polygonal Path (Stage 00).");
			return new PathNode(ellipse.Handle, segs, stroke, fill: null);
		}

		private PathNode buildCircle(Circle circle, double styleScaleToPaper, InsertRenderContext? containingInsert)
		{
			var pts = circle.PolygonalVertexes(this._configuration.ArcPrecision).ToArray();
			if (pts.Length < 2)
			{
				this._log.Add(circle.Handle, circle.SubclassMarker, RenderStatus.Skipped, "Degenerate circle.");
				return null;
			}

			var stroke = resolveStroke(circle, styleScaleToPaper, containingInsert);
			var segs = new List<PathSegment>(pts.Length + 1);
			segs.Add(new MoveTo((XY)pts[0]));
			for (int i = 1; i < pts.Length; i++)
			{
				segs.Add(new LineTo((XY)pts[i]));
			}
			segs.Add(new ClosePath());

			this._log.Add(circle.Handle, circle.SubclassMarker, RenderStatus.Rendered, "Rendered as polygonal Path (Stage 00).");
			return new PathNode(circle.Handle, segs, stroke, fill: null);
		}

		private RenderNode buildHatch(Hatch hatch, double styleScaleToPaper, InsertRenderContext? containingInsert)
		{
			StrokeStyle style = resolveStroke(hatch, styleScaleToPaper, containingInsert);
			IReadOnlyList<RenderNode> nodes = this._hatchGenerator.Render(hatch, style);
			if (nodes == null || nodes.Count == 0)
			{
				return null;
			}

			if (nodes.Count == 1)
			{
				return nodes[0];
			}

			return new GroupNode(hatch.Handle, Matrix4.Identity, nodes);
		}

		private RenderNode buildRasterImage(RasterImage image)
		{
			if (image == null)
			{
				return null;
			}

			if (!image.ShowImage)
			{
				this._log.Add(image.Handle, image.SubclassMarker, RenderStatus.Skipped, "IMAGE hidden by display flags.");
				return null;
			}

			if (image.Definition == null || string.IsNullOrWhiteSpace(image.Definition.FileName))
			{
				return failExternalReference(image.Handle, image.SubclassMarker, "IMAGE has no IMAGEDEF file reference.");
			}

			if (isDegenerate(image.UVector) || isDegenerate(image.VVector))
			{
				this._log.Add(image.Handle, image.SubclassMarker, RenderStatus.Skipped, "IMAGE has degenerate U/V vectors.");
				return null;
			}

			if (!this._underlayRasterCache.TryLoadRasterImage(image.Definition.FileName, out var raster, out string resolvedPath, out string reason))
			{
				return failExternalReference(image.Handle, image.SubclassMarker, $"IMAGE load failed: {reason}");
			}

			double displayWidth = image.Size.X > 0 ? image.Size.X : raster.Width;
			double displayHeight = image.Size.Y > 0 ? image.Size.Y : raster.Height;
			if (displayWidth <= 0 || displayHeight <= 0)
			{
				this._log.Add(image.Handle, image.SubclassMarker, RenderStatus.Skipped, "IMAGE has invalid display dimensions.");
				return null;
			}

			if (image.Flags.HasFlag(ImageDisplayFlags.TransparencyIsOn))
			{
				this._log.Add(image.Handle, image.SubclassMarker, RenderStatus.Rendered, "IMAGE transparency flag present; alpha is composited onto white (no soft mask support yet).");
			}

			byte[] rgb24 = UnderlayRasterCache.ApplyRasterImageAdjustments(raster.Rgb24Data, image.Brightness, image.Contrast, image.Fade);
			RenderNode leaf = new ImageNode(image.Handle, rgb24, raster.Width, raster.Height, displayWidth, displayHeight);

			PathNode clipPath = buildRasterImageClipPath(image, displayWidth, displayHeight);
			if (clipPath != null)
			{
				leaf = new ClipNode(image.Handle, clipPath, new[] { leaf });
			}

			Matrix4 imageTransform = TransformHelper.ImagePixelToWcs(image.InsertPoint, image.UVector, image.VVector);
			this._log.Add(image.Handle, image.SubclassMarker, RenderStatus.Rendered, $"Rendered IMAGE from '{resolvedPath}'.");
			return new GroupNode(image.Handle, imageTransform, new[] { leaf });
		}

		private RenderNode buildPdfUnderlay(PdfUnderlay underlay, double geometricScaleToPaper)
		{
			if (underlay == null)
			{
				return null;
			}

			if (!underlay.Flags.HasFlag(UnderlayDisplayFlags.ShowUnderlay))
			{
				this._log.Add(underlay.Handle, underlay.SubclassMarker, RenderStatus.Skipped, "PDFUNDERLAY hidden by display flags.");
				return null;
			}

			if (underlay.Definition == null || string.IsNullOrWhiteSpace(underlay.Definition.File))
			{
				return failExternalReference(underlay.Handle, underlay.SubclassMarker, "PDFUNDERLAY has no PDF definition file reference.");
			}

			int pageIndex = parseUnderlayPageIndex(underlay.Definition.Page);
			int dpi = determineUnderlayDpi(geometricScaleToPaper);

			if (!this._underlayRasterCache.TryRasterizePdf(underlay.Definition.File, pageIndex, dpi, out var raster, out string resolvedPath, out string reason))
			{
				return failExternalReference(underlay.Handle, underlay.SubclassMarker, $"PDFUNDERLAY rasterization failed: {reason}");
			}

			if (raster.Width <= 0 || raster.Height <= 0)
			{
				this._log.Add(underlay.Handle, underlay.SubclassMarker, RenderStatus.Skipped, "PDFUNDERLAY rasterized to invalid dimensions.");
				return null;
			}

			double displayWidth = raster.Width;
			double displayHeight = raster.Height;
			bool monochrome = underlay.Flags.HasFlag(UnderlayDisplayFlags.Monochrome);
			byte[] rgb24 = UnderlayRasterCache.ApplyUnderlayAdjustments(raster.Rgb24Data, underlay.Contrast, underlay.Fade, monochrome);
			RenderNode leaf = new ImageNode(underlay.Handle, rgb24, raster.Width, raster.Height, displayWidth, displayHeight);

			PathNode clipPath = buildPdfUnderlayClipPath(underlay, displayWidth, displayHeight);
			if (clipPath != null)
			{
				leaf = new ClipNode(underlay.Handle, clipPath, new[] { leaf });
			}

			Matrix4 underlayTransform = buildPdfUnderlayPixelTransform(underlay, displayWidth, displayHeight);
			this._log.Add(underlay.Handle, underlay.SubclassMarker, RenderStatus.Rendered, $"Rendered PDFUNDERLAY from '{resolvedPath}' page {pageIndex + 1}.");
			return new GroupNode(underlay.Handle, underlayTransform, new[] { leaf });
		}

		private int determineUnderlayDpi(double geometricScaleToPaper)
		{
			int baseDpi = this._configuration.PdfUnderlayDpi;
			if (baseDpi <= 0)
			{
				baseDpi = 150;
			}

			double scale = geometricScaleToPaper <= 0 ? 1.0 : geometricScaleToPaper;
			int dpi = (int)Math.Round(baseDpi * scale);
			return clampInt(dpi, 72, 600);
		}

		private static int clampInt(int value, int min, int max)
		{
			if (value < min) return min;
			if (value > max) return max;
			return value;
		}

		private RenderNode failExternalReference(ulong handle, string subclassMarker, string reason)
		{
			if (this._configuration.SkipMissingImages)
			{
				this._log.Add(handle, subclassMarker, RenderStatus.Skipped, reason);
				return null;
			}

			throw new FileNotFoundException(reason);
		}

		private static bool isDegenerate(XYZ vector)
		{
			const double eps = 1e-12;
			return vector.GetLength() <= eps;
		}

		private static int parseUnderlayPageIndex(string page)
		{
			if (string.IsNullOrWhiteSpace(page))
			{
				return 0;
			}

			if (int.TryParse(page, out int parsed) && parsed > 0)
			{
				return parsed - 1;
			}

			return 0;
		}

		private Matrix4 buildPdfUnderlayPixelTransform(PdfUnderlay underlay, double displayWidth, double displayHeight)
		{
			XYZ normal = underlay.Normal == XYZ.Zero ? XYZ.AxisZ : underlay.Normal;
			Matrix4 ocsToWcs = TransformHelper.OcsToWcs(normal);

			double width = displayWidth <= 0.0 ? 1.0 : displayWidth;
			double height = displayHeight <= 0.0 ? 1.0 : displayHeight;

			double ux = underlay.XScale / width;
			double uy = underlay.YScale / height;

			double cos = Math.Cos(underlay.Rotation);
			double sin = Math.Sin(underlay.Rotation);

			XYZ uOcs = new XYZ(cos * ux, sin * ux, 0.0);
			XYZ vOcs = new XYZ(-sin * uy, cos * uy, 0.0);

			XYZ uWcs = ocsToWcs * uOcs;
			XYZ vWcs = ocsToWcs * vOcs;

			return TransformHelper.ImagePixelToWcs(underlay.InsertPoint, uWcs, vWcs);
		}

		private PathNode buildRasterImageClipPath(RasterImage image, double fullWidth, double fullHeight)
		{
			if (!image.ClippingState || !image.Flags.HasFlag(ImageDisplayFlags.UseClippingBoundary))
			{
				return null;
			}

			if (image.ClipMode == ClipMode.Outside)
			{
				this._log.Add(image.Handle, image.SubclassMarker, RenderStatus.Rendered, "IMAGE outside clipping mode is not supported; rendering unclipped.");
				return null;
			}

			List<XY> clipVertices = image.ClipBoundaryVertices;
			if (clipVertices == null || clipVertices.Count == 0)
			{
				return rectanglePath(image.Handle, new XY(-0.5, -0.5), new XY(fullWidth - 0.5, fullHeight - 0.5));
			}

			if (image.ClipType == ClipType.Rectangular)
			{
				if (clipVertices.Count < 2)
				{
					return null;
				}

				double minX = Math.Min(clipVertices[0].X, clipVertices[1].X);
				double minY = Math.Min(clipVertices[0].Y, clipVertices[1].Y);
				double maxX = Math.Max(clipVertices[0].X, clipVertices[1].X);
				double maxY = Math.Max(clipVertices[0].Y, clipVertices[1].Y);
				return rectanglePath(image.Handle, new XY(minX, minY), new XY(maxX, maxY));
			}

			if (clipVertices.Count < 3)
			{
				return null;
			}

			return closedPolygonPath(image.Handle, clipVertices);
		}

		private PathNode buildPdfUnderlayClipPath(PdfUnderlay underlay, double displayWidth, double displayHeight)
		{
			if (!underlay.Flags.HasFlag(UnderlayDisplayFlags.ClippingOn))
			{
				return null;
			}

			if (!underlay.Flags.HasFlag(UnderlayDisplayFlags.ClipInsideMode))
			{
				this._log.Add(underlay.Handle, underlay.SubclassMarker, RenderStatus.Rendered, "PDFUNDERLAY outside clipping mode is not supported; rendering unclipped.");
				return null;
			}

			if (underlay.ClipBoundaryVertices == null || underlay.ClipBoundaryVertices.Count == 0)
			{
				return null;
			}

			double sx = Math.Abs(underlay.XScale) <= 1e-12 ? 1.0 : underlay.XScale;
			double sy = Math.Abs(underlay.YScale) <= 1e-12 ? 1.0 : underlay.YScale;

			var points = new List<XY>(underlay.ClipBoundaryVertices.Count);
			foreach (XY vertex in underlay.ClipBoundaryVertices)
			{
				points.Add(new XY(vertex.X / sx * displayWidth, vertex.Y / sy * displayHeight));
			}

			if (points.Count == 2)
			{
				double minX = Math.Min(points[0].X, points[1].X);
				double minY = Math.Min(points[0].Y, points[1].Y);
				double maxX = Math.Max(points[0].X, points[1].X);
				double maxY = Math.Max(points[0].Y, points[1].Y);
				return rectanglePath(underlay.Handle, new XY(minX, minY), new XY(maxX, maxY));
			}

			if (points.Count < 3)
			{
				return null;
			}

			return closedPolygonPath(underlay.Handle, points);
		}

		private static PathNode closedPolygonPath(ulong handle, IReadOnlyList<XY> points)
		{
			if (points == null || points.Count < 3)
			{
				return null;
			}

			var segments = new List<PathSegment>(points.Count + 1)
			{
				new MoveTo(points[0]),
			};

			for (int i = 1; i < points.Count; i++)
			{
				segments.Add(new LineTo(points[i]));
			}
			segments.Add(new ClosePath());

			return new PathNode(handle, segments, stroke: null, fill: null);
		}

		private PathNode buildPoint(Point point, double pointScaleToPaper, InsertRenderContext? containingInsert)
		{
			double sizePaper = this._configuration.DotSize;
			double sizeLocal = pointScaleToPaper <= 0 ? sizePaper : sizePaper / pointScaleToPaper;
			double diff = sizeLocal / 2;

			XY p = (XY)point.Location;
			XY min = new XY(p.X - diff, p.Y - diff);
			XY max = new XY(p.X + diff, p.Y + diff);

			var fillColor = resolveStroke(point, pointScaleToPaper, containingInsert).Color;
			var fill = new FillStyle(fillColor);

			var rect = rectanglePath(point.Handle, min, max);
			this._log.Add(point.Handle, point.SubclassMarker, RenderStatus.Rendered, "Rendered as filled rectangle (dot).");
			return new PathNode(point.Handle, rect.Segments, stroke: null, fill: fill);
		}

		private TextRunNode buildText(TextEntity text, double textScaleToPaper, InsertRenderContext? containingInsert)
		{
			ACadSharp.Color color = resolveStroke(text, textScaleToPaper, containingInsert).Color;
			TextRunNode node = this._textLayout.LayoutText(text, textScaleToPaper, color);
			if (node == null)
			{
				return null;
			}

			this._log.Add(text.Handle, text.SubclassMarker, RenderStatus.Rendered, "Rendered as TextRun.");
			return node;
		}

		private RenderNode buildMText(MText mtext, double textScaleToPaper, InsertRenderContext? containingInsert)
		{
			ACadSharp.Color color = resolveStroke(mtext, textScaleToPaper, containingInsert).Color;
			RenderNode node = this._textLayout.LayoutMText(mtext, textScaleToPaper, color);
			if (node == null)
			{
				return null;
			}

			this._log.Add(mtext.Handle, mtext.SubclassMarker, RenderStatus.Rendered, "Rendered as MTEXT runs.");
			return node;
		}

		private RenderNode buildMultiLeader(
			MultiLeader mleader,
			Viewport viewport,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext? containingInsert,
			Matrix4 parentTransform,
			int depth,
			HashSet<string> activeBlocks)
		{
			if (mleader.ContextData == null)
			{
				this._log.Add(mleader.Handle, mleader.SubclassMarker, RenderStatus.Skipped, "MULTILEADER has no context data.");
				return null;
			}

			var style = resolveMLeaderStyle(mleader);
			var nodes = new List<RenderNode>();
			XYZ contentAnchor = resolveMLeaderContentAnchor(style, mleader.ContextData);

			StrokeStyle baseStroke = null;
			if (style.PathType != MultiLeaderPathType.Invisible)
			{
				baseStroke = createMLeaderStroke(mleader, style.LineColor, style.LineWeight, style.LineType, styleScaleToPaper, containingInsert);

					foreach (var root in mleader.ContextData.LeaderRoots)
					{
						if (root == null)
						{
							continue;
						}

						bool renderedLeaderLine = false;
						foreach (var line in root.Lines)
						{
							if (line == null)
							{
								continue;
							}

						var lineStyle = resolveMLeaderLineStyle(style, line);
						if (lineStyle.PathType == MultiLeaderPathType.Invisible)
						{
							continue;
						}

						StrokeStyle lineStroke = createMLeaderStroke(mleader, lineStyle.LineColor, lineStyle.LineWeight, lineStyle.LineType, styleScaleToPaper, containingInsert);
						bool horizontalAttachment = hasHorizontalAttachment(root, style.TextAttachmentDirection);
						double landingDistance = resolveLandingDistance(style, root);
						XYZ doglegDirection = resolveDoglegDirection(root, horizontalAttachment, contentAnchor);
						XYZ doglegEndpoint = root.ConnectionPoint + doglegDirection * landingDistance;
						bool drawDogleg = shouldDrawDogleg(style, lineStyle.PathType, horizontalAttachment, landingDistance, doglegDirection);
						XYZ leaderEnd = horizontalAttachment
							? (drawDogleg ? root.ConnectionPoint : doglegEndpoint)
							: root.ConnectionPoint;

							var leaderVertices = buildLeaderVertices(line, leaderEnd);
							if (leaderVertices.Count < 2)
							{
								continue;
							}

							renderedLeaderLine = true;
							if (lineStyle.PathType == MultiLeaderPathType.Spline)
							{
								List<XYZ> spline = tessellateSpline(leaderVertices);
								if (spline.Count >= 2)
								{
								var points = new List<XY>(spline.Count);
								foreach (var point in spline)
								{
									points.Add(transformPointToXY(point, parentTransform));
								}

								PathNode splinePath = createPolylinePath(mleader.Handle, points, lineStroke, closed: false);
								if (splinePath != null)
								{
									nodes.Add(splinePath);
								}
							}
						}
						else
						{
							bool hasBreaks = line.StartEndPoints != null && line.StartEndPoints.Count > 0;
							if (!hasBreaks)
							{
								var points = new List<XY>(leaderVertices.Count);
								foreach (var point in leaderVertices)
								{
									points.Add(transformPointToXY(point, parentTransform));
								}

								PathNode path = createPolylinePath(mleader.Handle, points, lineStroke, closed: false);
								if (path != null)
								{
									nodes.Add(path);
								}
							}
							else
							{
								int breakSegmentIndex = line.SegmentIndex;
								if (breakSegmentIndex < 0 || breakSegmentIndex >= leaderVertices.Count - 1)
								{
									breakSegmentIndex = 0;
								}

								for (int segmentIndex = 0; segmentIndex < leaderVertices.Count - 1; segmentIndex++)
								{
									IList<MultiLeaderObjectContextData.StartEndPointPair> segmentBreaks =
										segmentIndex == breakSegmentIndex ? line.StartEndPoints : null;

									foreach (var piece in splitSegmentByBreaks(leaderVertices[segmentIndex], leaderVertices[segmentIndex + 1], segmentBreaks))
									{
										PathNode piecePath = createLinePath(
											mleader.Handle,
											transformPointToXY(piece.Start, parentTransform),
											transformPointToXY(piece.End, parentTransform),
											lineStroke);

										if (piecePath != null)
										{
											nodes.Add(piecePath);
										}
									}
								}
							}
						}

						addMLeaderArrow(
							nodes,
							mleader,
							lineStyle,
							lineStroke,
							leaderVertices,
							viewport,
							styleScaleToPaper,
							textScaleToPaper,
							parentTransform,
							depth,
							activeBlocks);
					}

						bool rootHorizontalAttachment = hasHorizontalAttachment(root, style.TextAttachmentDirection);
						double rootLandingDistance = resolveLandingDistance(style, root);
						XYZ rootDoglegDirection = resolveDoglegDirection(root, rootHorizontalAttachment, contentAnchor);
						if (renderedLeaderLine && shouldDrawDogleg(style, style.PathType, rootHorizontalAttachment, rootLandingDistance, rootDoglegDirection))
						{
							XYZ doglegStart = root.ConnectionPoint;
							XYZ doglegEnd = doglegStart + rootDoglegDirection * rootLandingDistance;

							foreach (var piece in splitSegmentByBreaks(doglegStart, doglegEnd, root.BreakStartEndPointsPairs))
						{
							PathNode doglegPath = createLinePath(
								mleader.Handle,
								transformPointToXY(piece.Start, parentTransform),
								transformPointToXY(piece.End, parentTransform),
								baseStroke);

							if (doglegPath != null)
							{
								nodes.Add(doglegPath);
							}
						}
					}
				}
			}

			RenderNode contentNode = buildMLeaderContent(
				mleader,
				style,
				viewport,
				styleScaleToPaper,
				textScaleToPaper,
				containingInsert,
				parentTransform,
				depth,
				activeBlocks);

			if (contentNode != null)
			{
				nodes.Add(contentNode);
			}

			if (nodes.Count == 0)
			{
				this._log.Add(mleader.Handle, mleader.SubclassMarker, RenderStatus.Skipped, "MULTILEADER produced no visible primitives.");
				return null;
			}

			this._log.Add(mleader.Handle, mleader.SubclassMarker, RenderStatus.Rendered, "Rendered as computed MULTILEADER geometry.");
			return new GroupNode(mleader.Handle, Matrix4.Identity, nodes);
		}

		private RenderNode buildMLeaderContent(
			MultiLeader mleader,
			MLeaderResolvedStyle style,
			Viewport viewport,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext? containingInsert,
			Matrix4 parentTransform,
			int depth,
			HashSet<string> activeBlocks)
		{
			switch (style.ContentType)
			{
				case LeaderContentType.None:
					return null;
				case LeaderContentType.MText:
					{
						MText text = createMLeaderText(mleader, style);
						if (text == null)
						{
							return null;
						}

						MText transformed = text;
						if (!isIdentity(parentTransform))
						{
							transformed = (MText)cloneWithTransform(text, parentTransform);
						}

						ACadSharp.Color color = resolveStroke(transformed, textScaleToPaper, containingInsert).Color;
						return this._textLayout.LayoutMText(transformed, textScaleToPaper, color);
					}
				case LeaderContentType.Block:
					{
						Insert insert = createMLeaderBlockInsert(mleader, style);
						if (insert == null)
						{
							return null;
						}

						return buildEntityNode(
							insert,
							viewport,
							styleScaleToPaper,
							textScaleToPaper,
							containingInsert,
							parentTransform,
							depth + 1,
							activeBlocks);
					}
				default:
					this._log.Add(mleader.Handle, mleader.SubclassMarker, RenderStatus.NotImplemented, $"MULTILEADER content type '{style.ContentType}' not supported.");
					this._configuration.Notify($"[{mleader.SubclassMarker}] MULTILEADER content type not implemented (scene graph pipeline).", NotificationType.NotImplemented);
					return null;
			}
		}

		private void addMLeaderArrow(
			List<RenderNode> nodes,
			MultiLeader mleader,
			MLeaderLineStyle lineStyle,
			StrokeStyle stroke,
			List<XYZ> vertices,
			Viewport viewport,
			double styleScaleToPaper,
			double textScaleToPaper,
			Matrix4 parentTransform,
			int depth,
			HashSet<string> activeBlocks)
		{
			if (lineStyle.ArrowheadSize <= 1e-9 || vertices == null || vertices.Count < 2)
			{
				return;
			}

			XYZ tip = vertices[0];
			XYZ next = vertices[1];
			XYZ forward = next - tip;
			double length = Math.Sqrt(forward.Dot(forward));
			if (length <= 1e-9 || length <= lineStyle.ArrowheadSize * 2.0)
			{
				return;
			}

			XYZ directionToTip = safeNormalize(tip - next, new XYZ(-1.0, 0.0, 0.0));

			if (lineStyle.Arrowhead != null)
			{
				var arrowInsert = new Insert(lineStyle.Arrowhead)
				{
					InsertPoint = tip,
					XScale = lineStyle.ArrowheadSize,
					YScale = lineStyle.ArrowheadSize,
					ZScale = 1.0,
					Rotation = Math.Atan2(directionToTip.Y, directionToTip.X),
						Layer = mleader.Layer,
						Color = stroke.Color,
						LineWeight = LineWeightType.ByLayer,
						LineType = LineType.ByLayer,
						Normal = XYZ.AxisZ,
					};

				RenderNode arrowNode = buildEntityNode(
					arrowInsert,
					viewport,
					styleScaleToPaper,
					textScaleToPaper,
					containingInsert: null,
					parentTransform,
					depth + 1,
					activeBlocks);

				if (arrowNode != null)
				{
					nodes.Add(arrowNode);
					return;
				}
			}

			XY dir = new XY(directionToTip.X, directionToTip.Y);
			if (!tryNormalize(dir, out XY normDir))
			{
				return;
			}

			XY perp = perpendicularLeft(normDir);
			XYZ back = tip - directionToTip * lineStyle.ArrowheadSize;
			XYZ left = back + new XYZ(perp.X, perp.Y, 0.0) * (lineStyle.ArrowheadSize * 0.3);
			XYZ right = back - new XYZ(perp.X, perp.Y, 0.0) * (lineStyle.ArrowheadSize * 0.3);

			var segs = new PathSegment[]
			{
				new MoveTo(transformPointToXY(tip, parentTransform)),
				new LineTo(transformPointToXY(left, parentTransform)),
				new LineTo(transformPointToXY(right, parentTransform)),
				new ClosePath(),
			};

			nodes.Add(new PathNode(mleader.Handle, segs, stroke: null, fill: new FillStyle(stroke.Color)));
		}

		private MLeaderResolvedStyle resolveMLeaderStyle(MultiLeader mleader)
		{
			MultiLeaderStyle style = mleader.Style ?? MultiLeaderStyle.Default;
			MultiLeaderObjectContextData context = mleader.ContextData;
			MultiLeaderPropertyOverrideFlags flags = mleader.PropertyOverrideFlags;

			MultiLeaderPathType pathType = hasFlag(flags, MultiLeaderPropertyOverrideFlags.PathType)
				? mleader.PathType
				: style.PathType;
			if (pathType == MultiLeaderPathType.Invisible && mleader.PathType != MultiLeaderPathType.Invisible)
			{
				pathType = mleader.PathType;
			}

			ACadSharp.Color lineColor = hasFlag(flags, MultiLeaderPropertyOverrideFlags.LineColor)
				? mleader.LineColor
				: style.LineColor;
			if (lineColor.IsByBlock && !mleader.LineColor.IsByBlock)
			{
				lineColor = mleader.LineColor;
			}

			LineType lineType = hasFlag(flags, MultiLeaderPropertyOverrideFlags.LeaderLineType)
				? mleader.LeaderLineType
				: style.LeaderLineType;
			lineType ??= mleader.LeaderLineType ?? LineType.ByLayer;

			LineWeightType lineWeight = hasFlag(flags, MultiLeaderPropertyOverrideFlags.LeaderLineWeight)
				? mleader.LeaderLineWeight
				: style.LeaderLineWeight;
			if (lineWeight == LineWeightType.ByBlock && mleader.LeaderLineWeight != LineWeightType.ByBlock)
			{
				lineWeight = mleader.LeaderLineWeight;
			}

			bool enableDogleg = hasFlag(flags, MultiLeaderPropertyOverrideFlags.EnableDogleg)
				? mleader.EnableDogleg
				: style.EnableDogleg;

			bool enableLanding = hasFlag(flags, MultiLeaderPropertyOverrideFlags.EnableLanding)
				? mleader.EnableLanding
				: style.EnableLanding;

			double landingDistance = hasFlag(flags, MultiLeaderPropertyOverrideFlags.LandingDistance)
				? mleader.LandingDistance
				: style.LandingDistance;
			if (landingDistance <= 1e-9 && mleader.LandingDistance > 1e-9)
			{
				landingDistance = mleader.LandingDistance;
			}

			double landingGap = context.LandingGap > 1e-9 ? context.LandingGap : style.LandingGap;

			BlockRecord arrowhead = hasFlag(flags, MultiLeaderPropertyOverrideFlags.Arrowhead)
				? mleader.Arrowhead
				: style.Arrowhead;
			if (arrowhead == null && mleader.Arrowhead != null)
			{
				arrowhead = mleader.Arrowhead;
			}

			double arrowheadSize = context.ArrowheadSize > 1e-9
				? context.ArrowheadSize
				: (hasFlag(flags, MultiLeaderPropertyOverrideFlags.ArrowheadSize) ? mleader.ArrowheadSize : style.ArrowheadSize);
			if (arrowheadSize <= 1e-9 && mleader.ArrowheadSize > 1e-9)
			{
				arrowheadSize = mleader.ArrowheadSize;
			}

			LeaderContentType contentType = hasFlag(flags, MultiLeaderPropertyOverrideFlags.ContentType)
				? mleader.ContentType
				: style.ContentType;
			if (context.HasTextContents)
			{
				contentType = LeaderContentType.MText;
			}
			else if (context.HasContentsBlock)
			{
				contentType = LeaderContentType.Block;
			}

			TextStyle textStyle = context.TextStyle
				?? (hasFlag(flags, MultiLeaderPropertyOverrideFlags.TextStyle) ? mleader.TextStyle : style.TextStyle)
				?? TextStyle.Default;

			ACadSharp.Color textColor;
			if (context.HasTextContents || !string.IsNullOrWhiteSpace(context.TextLabel))
			{
				textColor = context.TextColor;
			}
			else
			{
				textColor = hasFlag(flags, MultiLeaderPropertyOverrideFlags.TextColor)
					? mleader.TextColor
					: style.TextColor;
			}

			double textHeight = context.TextHeight > 1e-9
				? context.TextHeight
				: (hasFlag(flags, MultiLeaderPropertyOverrideFlags.TextHeight) ? mleader.ContextData.TextHeight : style.TextHeight);
			if (textHeight <= 1e-9)
			{
				textHeight = style.TextHeight > 1e-9 ? style.TextHeight : 1.0;
			}

			TextAttachmentDirectionType textAttachmentDirection = hasFlag(flags, MultiLeaderPropertyOverrideFlags.TextAttachmentDirection)
				? mleader.TextAttachmentDirection
				: style.TextAttachmentDirection;

			BlockRecord blockContent = context.BlockContent
				?? (hasFlag(flags, MultiLeaderPropertyOverrideFlags.BlockContent) ? mleader.BlockContent : style.BlockContent)
				?? mleader.BlockContent;

			ACadSharp.Color blockColor = hasFlag(flags, MultiLeaderPropertyOverrideFlags.BlockContentColor)
				? mleader.BlockContentColor
				: style.BlockContentColor;
			if (!context.BlockContentColor.IsByBlock)
			{
				blockColor = context.BlockContentColor;
			}

			XYZ blockScale = isZeroVector(context.BlockContentScale)
				? (hasFlag(flags, MultiLeaderPropertyOverrideFlags.BlockContentScale) ? mleader.BlockContentScale : style.BlockContentScale)
				: context.BlockContentScale;
			if (isZeroVector(blockScale))
			{
				blockScale = new XYZ(1.0, 1.0, 1.0);
			}

			double blockRotation = Math.Abs(context.BlockContentRotation) > 1e-9
				? context.BlockContentRotation
				: (hasFlag(flags, MultiLeaderPropertyOverrideFlags.BlockContentRotation) ? mleader.BlockContentRotation : style.BlockContentRotation);

			XYZ blockLocation = !isZeroVector(context.BlockContentLocation)
				? context.BlockContentLocation
				: context.ContentBasePoint;

			XYZ blockNormal = !isZeroVector(context.BlockContentNormal)
				? context.BlockContentNormal
				: XYZ.AxisZ;

			return new MLeaderResolvedStyle(
				pathType,
				lineColor,
				lineType,
				lineWeight,
				enableDogleg,
				enableLanding,
				landingDistance,
				landingGap,
				arrowhead,
				arrowheadSize,
				contentType,
				textStyle,
				textColor,
				textHeight,
				textAttachmentDirection,
				blockContent,
				blockColor,
				blockScale,
				blockRotation,
				blockLocation,
				blockNormal);
		}

		private static MLeaderLineStyle resolveMLeaderLineStyle(MLeaderResolvedStyle style, MultiLeaderObjectContextData.LeaderLine line)
		{
			var flags = line.OverrideFlags;
			MultiLeaderPathType pathType = hasFlag(flags, LeaderLinePropertOverrideFlags.PathType) ? line.PathType : style.PathType;
			if (pathType == MultiLeaderPathType.Invisible && line.PathType != MultiLeaderPathType.Invisible)
			{
				pathType = line.PathType;
			}

			ACadSharp.Color lineColor = hasFlag(flags, LeaderLinePropertOverrideFlags.LineColor) ? line.LineColor : style.LineColor;
			if (lineColor.IsByBlock && !line.LineColor.IsByBlock)
			{
				lineColor = line.LineColor;
			}

			LineType lineType = hasFlag(flags, LeaderLinePropertOverrideFlags.LineType) ? line.LineType : style.LineType;
			lineType ??= style.LineType ?? LineType.ByLayer;

			LineWeightType lineWeight = hasFlag(flags, LeaderLinePropertOverrideFlags.LineWeight) ? line.LineWeight : style.LineWeight;
			if (lineWeight == LineWeightType.ByBlock && line.LineWeight != LineWeightType.ByBlock)
			{
				lineWeight = line.LineWeight;
			}

			double arrowheadSize = hasFlag(flags, LeaderLinePropertOverrideFlags.ArrowheadSize) ? line.ArrowheadSize : style.ArrowheadSize;
			if (arrowheadSize <= 1e-9 && line.ArrowheadSize > 1e-9)
			{
				arrowheadSize = line.ArrowheadSize;
			}

			BlockRecord arrowhead = hasFlag(flags, LeaderLinePropertOverrideFlags.Arrowhead) ? line.Arrowhead : style.Arrowhead;
			if (arrowhead == null && line.Arrowhead != null)
			{
				arrowhead = line.Arrowhead;
			}

			return new MLeaderLineStyle(pathType, lineColor, lineType, lineWeight, arrowhead, arrowheadSize);
		}

		private StrokeStyle createMLeaderStroke(
			MultiLeader mleader,
			ACadSharp.Color color,
			LineWeightType lineWeight,
			LineType lineType,
			double styleScaleToPaper,
			InsertRenderContext? containingInsert)
		{
			double globalLtScale = mleader?.Document?.Header?.LineTypeScale ?? 1.0;
			var proxy = new Line
			{
				StartPoint = XYZ.Zero,
				EndPoint = XY.AxisX.Convert<XYZ>(),
				Layer = mleader?.Layer,
				Color = color,
				LineWeight = lineWeight,
				LineType = lineType ?? LineType.ByLayer,
				LineTypeScale = globalLtScale,
			};

			return resolveStroke(proxy, styleScaleToPaper, containingInsert);
		}

		private static List<XYZ> buildLeaderVertices(MultiLeaderObjectContextData.LeaderLine line, XYZ endPoint)
		{
			var vertices = new List<XYZ>(line.Points.Count + 1);
			foreach (var point in line.Points)
			{
				vertices.Add(point);
			}

			if (vertices.Count == 0 || vertices[vertices.Count - 1].DistanceFrom(endPoint) > 1e-9)
			{
				vertices.Add(endPoint);
			}

			return vertices;
		}

		private static bool shouldDrawDogleg(
			MLeaderResolvedStyle style,
			MultiLeaderPathType pathType,
			bool horizontalAttachment,
			double landingDistance,
			XYZ doglegDirection)
		{
			return horizontalAttachment
				&& style.EnableLanding
				&& style.EnableDogleg
				&& pathType == MultiLeaderPathType.StraightLineSegments
				&& landingDistance > 1e-9
				&& !isZeroVector(doglegDirection);
		}

		private static bool hasHorizontalAttachment(MultiLeaderObjectContextData.LeaderRoot root, TextAttachmentDirectionType fallback)
		{
			if (root == null)
			{
				return fallback != TextAttachmentDirectionType.Vertical;
			}

			return root.TextAttachmentDirection != TextAttachmentDirectionType.Vertical
				&& fallback != TextAttachmentDirectionType.Vertical;
		}

		private static double resolveLandingDistance(MLeaderResolvedStyle style, MultiLeaderObjectContextData.LeaderRoot root)
		{
			if (root != null && root.LandingDistance > 1e-9)
			{
				return root.LandingDistance;
			}

			return Math.Max(0.0, style.LandingDistance);
		}

		private static XYZ resolveMLeaderContentAnchor(MLeaderResolvedStyle style, MultiLeaderObjectContextData context)
		{
			switch (style.ContentType)
			{
				case LeaderContentType.MText:
					return !isZeroVector(context.TextLocation) ? context.TextLocation : context.ContentBasePoint;
				case LeaderContentType.Block:
					return !isZeroVector(style.BlockLocation) ? style.BlockLocation : context.ContentBasePoint;
				default:
					return context.ContentBasePoint;
			}
		}

		private static XYZ resolveDoglegDirection(MultiLeaderObjectContextData.LeaderRoot root, bool horizontalAttachment, XYZ contentAnchor)
		{
			XYZ raw = root.Direction;
			if (!isZeroVector(raw))
			{
				XYZ normalized = safeNormalize(raw, XYZ.AxisX);
				if (horizontalAttachment)
				{
					double x = Math.Abs(normalized.X) > 1e-9 ? Math.Sign(normalized.X) : 1.0;
					return new XYZ(x, 0.0, 0.0);
				}

				return normalized;
			}

			XYZ toContent = contentAnchor - root.ConnectionPoint;
			if (horizontalAttachment)
			{
				double x = Math.Abs(toContent.X) > 1e-9 ? Math.Sign(toContent.X) : 1.0;
				return new XYZ(x, 0.0, 0.0);
			}

			if (Math.Abs(toContent.Y) > 1e-9)
			{
				return safeNormalize(toContent, XYZ.AxisY);
			}

			return XYZ.AxisX;
		}

		private static IEnumerable<(XYZ Start, XYZ End)> splitSegmentByBreaks(
			XYZ start,
			XYZ end,
			IList<MultiLeaderObjectContextData.StartEndPointPair> breaks)
		{
			XYZ delta = end - start;
			double len2 = delta.Dot(delta);
			if (len2 <= 1e-12)
			{
				yield break;
			}

			if (breaks == null || breaks.Count == 0)
			{
				yield return (start, end);
				yield break;
			}

			var intervals = new List<(double Start, double End)>(breaks.Count);
			foreach (var segmentBreak in breaks)
			{
				double t1 = (segmentBreak.StartPoint - start).Dot(delta) / len2;
				double t2 = (segmentBreak.EndPoint - start).Dot(delta) / len2;
				double from = Math.Max(0.0, Math.Min(1.0, Math.Min(t1, t2)));
				double to = Math.Max(0.0, Math.Min(1.0, Math.Max(t1, t2)));
				if (to - from > 1e-9)
				{
					intervals.Add((from, to));
				}
			}

			if (intervals.Count == 0)
			{
				yield return (start, end);
				yield break;
			}

			intervals.Sort((a, b) => a.Start.CompareTo(b.Start));

			double cursor = 0.0;
			foreach (var interval in intervals)
			{
				if (interval.Start > cursor + 1e-9)
				{
					yield return (start + delta * cursor, start + delta * interval.Start);
				}

				if (interval.End > cursor)
				{
					cursor = interval.End;
					if (cursor >= 1.0 - 1e-9)
					{
						yield break;
					}
				}
			}

			if (cursor < 1.0 - 1e-9)
			{
				yield return (start + delta * cursor, end);
			}
		}

		private List<XYZ> tessellateSpline(IReadOnlyList<XYZ> fitPoints)
		{
			if (fitPoints == null || fitPoints.Count == 0)
			{
				return new List<XYZ>();
			}

			var points = new List<XYZ>(fitPoints.Count);
			XYZ previous = fitPoints[0];
			points.Add(previous);
			for (int i = 1; i < fitPoints.Count; i++)
			{
				XYZ current = fitPoints[i];
				if (current.DistanceFrom(previous) <= 1e-9)
				{
					continue;
				}

				points.Add(current);
				previous = current;
			}

			if (points.Count <= 2)
			{
				return points;
			}

			int segmentsPerSpan = Math.Max(4, Math.Min(32, this._configuration.ArcPrecision / 32));
			var output = new List<XYZ>((points.Count - 1) * segmentsPerSpan + 1);
			for (int i = 0; i < points.Count - 1; i++)
			{
				XYZ p0 = i > 0 ? points[i - 1] : points[i];
				XYZ p1 = points[i];
				XYZ p2 = points[i + 1];
				XYZ p3 = i + 2 < points.Count ? points[i + 2] : points[i + 1];

				if (i == 0)
				{
					output.Add(p1);
				}

				for (int s = 1; s <= segmentsPerSpan; s++)
				{
					double t = (double)s / segmentsPerSpan;
					output.Add(catmullRom(p0, p1, p2, p3, t));
				}
			}

			return output;
		}

		private static XYZ catmullRom(XYZ p0, XYZ p1, XYZ p2, XYZ p3, double t)
		{
			double t2 = t * t;
			double t3 = t2 * t;
			double x = 0.5 * ((2.0 * p1.X) + (-p0.X + p2.X) * t + (2.0 * p0.X - 5.0 * p1.X + 4.0 * p2.X - p3.X) * t2 + (-p0.X + 3.0 * p1.X - 3.0 * p2.X + p3.X) * t3);
			double y = 0.5 * ((2.0 * p1.Y) + (-p0.Y + p2.Y) * t + (2.0 * p0.Y - 5.0 * p1.Y + 4.0 * p2.Y - p3.Y) * t2 + (-p0.Y + 3.0 * p1.Y - 3.0 * p2.Y + p3.Y) * t3);
			double z = 0.5 * ((2.0 * p1.Z) + (-p0.Z + p2.Z) * t + (2.0 * p0.Z - 5.0 * p1.Z + 4.0 * p2.Z - p3.Z) * t2 + (-p0.Z + 3.0 * p1.Z - 3.0 * p2.Z + p3.Z) * t3);
			return new XYZ(x, y, z);
		}

		private static XY transformPointToXY(XYZ point, Matrix4 transform)
		{
			XYZ world = transform * point;
			return new XY(world.X, world.Y);
		}

		private static bool isZeroVector(XYZ value)
		{
			return Math.Abs(value.X) <= 1e-9
				&& Math.Abs(value.Y) <= 1e-9
				&& Math.Abs(value.Z) <= 1e-9;
		}

		private static XYZ safeNormalize(XYZ value, XYZ fallback)
		{
			if (isZeroVector(value))
			{
				return fallback;
			}

			return value.Normalize();
		}

		private static bool hasFlag(MultiLeaderPropertyOverrideFlags flags, MultiLeaderPropertyOverrideFlags expected)
		{
			return (flags & expected) == expected;
		}

		private static bool hasFlag(LeaderLinePropertOverrideFlags flags, LeaderLinePropertOverrideFlags expected)
		{
			return (flags & expected) == expected;
		}

		private static MText createMLeaderText(MultiLeader mleader, MLeaderResolvedStyle style)
		{
			MultiLeaderObjectContextData context = mleader.ContextData;
			string value = context.TextLabel;
			if (string.IsNullOrWhiteSpace(value))
			{
				value = mleader.Style?.DefaultTextContents ?? string.Empty;
			}

			if (string.IsNullOrWhiteSpace(value))
			{
				return null;
			}

			var mtext = new MText(value)
			{
				InsertPoint = context.TextLocation,
				Height = Math.Max(style.TextHeight, 1e-6),
				RectangleWidth = context.BoundaryWidth > 1e-9 ? context.BoundaryWidth : 0.0,
				LineSpacing = context.LineSpacingFactor > 1e-9 ? context.LineSpacingFactor : 1.0,
				LineSpacingStyle = mapMLeaderLineSpacing(context.LineSpacing),
					AttachmentPoint = mapMLeaderAttachmentPoint(context.TextAlignment),
					Style = style.TextStyle ?? TextStyle.Default,
					Color = style.TextColor,
					Layer = mleader.Layer,
					Normal = isZeroVector(context.TextNormal) ? XYZ.AxisZ : context.TextNormal,
				};

			XYZ direction = !isZeroVector(context.Direction)
				? safeNormalize(context.Direction, XYZ.AxisX)
				: new XYZ(Math.Cos(context.TextRotation), Math.Sin(context.TextRotation), 0.0);
			if (isZeroVector(direction))
			{
				direction = XYZ.AxisX;
			}

			mtext.AlignmentPoint = direction;
			return mtext;
		}

		private static AttachmentPointType mapMLeaderAttachmentPoint(TextAlignmentType alignment)
		{
			switch (alignment)
			{
				case TextAlignmentType.Center:
					return AttachmentPointType.MiddleCenter;
				case TextAlignmentType.Right:
					return AttachmentPointType.MiddleRight;
				default:
					return AttachmentPointType.MiddleLeft;
			}
		}

		private static LineSpacingStyleType mapMLeaderLineSpacing(LineSpacingStyle spacing)
		{
			return spacing == LineSpacingStyle.Exactly
				? LineSpacingStyleType.Exact
				: LineSpacingStyleType.AtLeast;
		}

		private Insert createMLeaderBlockInsert(MultiLeader mleader, MLeaderResolvedStyle style)
		{
			BlockRecord block = style.BlockContent;
			if (block == null)
			{
				this._log.Add(mleader.Handle, mleader.SubclassMarker, RenderStatus.Skipped, "MULTILEADER block content is missing.");
				return null;
			}

			if (Math.Abs(style.BlockScale.X) <= 1e-9
				|| Math.Abs(style.BlockScale.Y) <= 1e-9
				|| Math.Abs(style.BlockScale.Z) <= 1e-9)
			{
				this._log.Add(mleader.Handle, mleader.SubclassMarker, RenderStatus.Skipped, "MULTILEADER block content has degenerate scale.");
				return null;
			}

			var insert = new Insert(block)
			{
				InsertPoint = style.BlockLocation,
				XScale = style.BlockScale.X,
				YScale = style.BlockScale.Y,
				ZScale = style.BlockScale.Z,
				Rotation = style.BlockRotation,
				Normal = isZeroVector(style.BlockNormal) ? XYZ.AxisZ : style.BlockNormal,
				Layer = mleader.Layer,
				Color = style.BlockColor,
			};

			applyMLeaderBlockAttributes(insert, mleader.BlockAttributes);
			return insert;
		}

		private static void applyMLeaderBlockAttributes(Insert insert, IList<MultiLeader.BlockAttribute> blockAttributes)
		{
			if (insert == null || blockAttributes == null || blockAttributes.Count == 0 || insert.Attributes == null)
			{
				return;
			}

			var valuesByTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var blockAttribute in blockAttributes)
			{
				string tag = blockAttribute.AttributeDefinition?.Tag;
				if (string.IsNullOrWhiteSpace(tag))
				{
					continue;
				}

				valuesByTag[tag] = blockAttribute.Text ?? string.Empty;
			}

			if (valuesByTag.Count == 0)
			{
				return;
			}

			foreach (var attribute in insert.Attributes)
			{
				if (attribute == null || string.IsNullOrWhiteSpace(attribute.Tag))
				{
					continue;
				}

				if (valuesByTag.TryGetValue(attribute.Tag, out string value))
				{
					attribute.Value = value;
				}
			}
		}

		private readonly struct MLeaderResolvedStyle
		{
			public MultiLeaderPathType PathType { get; }
			public ACadSharp.Color LineColor { get; }
			public LineType LineType { get; }
			public LineWeightType LineWeight { get; }
			public bool EnableDogleg { get; }
			public bool EnableLanding { get; }
			public double LandingDistance { get; }
			public double LandingGap { get; }
			public BlockRecord Arrowhead { get; }
			public double ArrowheadSize { get; }
			public LeaderContentType ContentType { get; }
			public TextStyle TextStyle { get; }
			public ACadSharp.Color TextColor { get; }
			public double TextHeight { get; }
			public TextAttachmentDirectionType TextAttachmentDirection { get; }
			public BlockRecord BlockContent { get; }
			public ACadSharp.Color BlockColor { get; }
			public XYZ BlockScale { get; }
			public double BlockRotation { get; }
			public XYZ BlockLocation { get; }
			public XYZ BlockNormal { get; }

			public MLeaderResolvedStyle(
				MultiLeaderPathType pathType,
				ACadSharp.Color lineColor,
				LineType lineType,
				LineWeightType lineWeight,
				bool enableDogleg,
				bool enableLanding,
				double landingDistance,
				double landingGap,
				BlockRecord arrowhead,
				double arrowheadSize,
				LeaderContentType contentType,
				TextStyle textStyle,
				ACadSharp.Color textColor,
				double textHeight,
				TextAttachmentDirectionType textAttachmentDirection,
				BlockRecord blockContent,
				ACadSharp.Color blockColor,
				XYZ blockScale,
				double blockRotation,
				XYZ blockLocation,
				XYZ blockNormal)
			{
				this.PathType = pathType;
				this.LineColor = lineColor;
				this.LineType = lineType;
				this.LineWeight = lineWeight;
				this.EnableDogleg = enableDogleg;
				this.EnableLanding = enableLanding;
				this.LandingDistance = landingDistance;
				this.LandingGap = landingGap;
				this.Arrowhead = arrowhead;
				this.ArrowheadSize = arrowheadSize;
				this.ContentType = contentType;
				this.TextStyle = textStyle;
				this.TextColor = textColor;
				this.TextHeight = textHeight;
				this.TextAttachmentDirection = textAttachmentDirection;
				this.BlockContent = blockContent;
				this.BlockColor = blockColor;
				this.BlockScale = blockScale;
				this.BlockRotation = blockRotation;
				this.BlockLocation = blockLocation;
				this.BlockNormal = blockNormal;
			}
		}

		private readonly struct MLeaderLineStyle
		{
			public MultiLeaderPathType PathType { get; }
			public ACadSharp.Color LineColor { get; }
			public LineType LineType { get; }
			public LineWeightType LineWeight { get; }
			public BlockRecord Arrowhead { get; }
			public double ArrowheadSize { get; }

			public MLeaderLineStyle(
				MultiLeaderPathType pathType,
				ACadSharp.Color lineColor,
				LineType lineType,
				LineWeightType lineWeight,
				BlockRecord arrowhead,
				double arrowheadSize)
			{
				this.PathType = pathType;
				this.LineColor = lineColor;
				this.LineType = lineType;
				this.LineWeight = lineWeight;
				this.Arrowhead = arrowhead;
				this.ArrowheadSize = arrowheadSize;
			}
		}

		private RenderNode buildDimension(
			Dimension dimension,
			Viewport viewport,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext? containingInsert,
			Matrix4 parentTransform,
			int depth,
			HashSet<string> activeBlocks)
		{
			if (dimension == null)
			{
				return null;
			}

			if (tryBuildDimensionFromAnonymousBlock(dimension, viewport, styleScaleToPaper, textScaleToPaper, containingInsert, parentTransform, depth, activeBlocks, out RenderNode blockNode))
			{
				this._log.Add(dimension.Handle, dimension.SubclassMarker, RenderStatus.Rendered, "Rendered via anonymous dimension block.");
				return blockNode;
			}

			DimensionStyle style = dimension.GetActiveDimensionStyle() ?? dimension.Style ?? DimensionStyle.Default;
			var nodes = new List<RenderNode>();

			switch (dimension)
			{
				case DimensionLinear linear:
					buildLinearOrAlignedDimension(linear, isLinear: true, style, nodes, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);
					break;
				case DimensionAligned aligned:
					buildLinearOrAlignedDimension(aligned, isLinear: false, style, nodes, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);
					break;
				case DimensionAngular3Pt angular3Pt:
					buildAngular3PointDimension(angular3Pt, style, nodes, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);
					break;
				case DimensionAngular2Line angular2Line:
					buildAngular2LineDimension(angular2Line, style, nodes, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);
					break;
				case DimensionRadius radius:
					buildRadiusDimension(radius, style, nodes, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);
					break;
				case DimensionDiameter diameter:
					buildDiameterDimension(diameter, style, nodes, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);
					break;
				case DimensionOrdinate ordinate:
					buildOrdinateDimension(ordinate, style, nodes, styleScaleToPaper, textScaleToPaper, containingInsert);
					break;
				default:
					this._log.Add(dimension.Handle, dimension.SubclassMarker, RenderStatus.NotImplemented, "DIMENSION subtype not supported in Stage 03.");
					this._configuration.Notify($"[{dimension.SubclassMarker}] Dimension subtype not implemented (scene graph pipeline).", NotificationType.NotImplemented);
					return null;
			}

			if (nodes.Count == 0)
			{
				this._log.Add(dimension.Handle, dimension.SubclassMarker, RenderStatus.Skipped, "Dimension produced no visible primitives.");
				return null;
			}

			this._log.Add(dimension.Handle, dimension.SubclassMarker, RenderStatus.Rendered, "Rendered as computed dimension geometry.");
			return new GroupNode(dimension.Handle, Matrix4.Identity, nodes);
		}

		private bool tryBuildDimensionFromAnonymousBlock(
			Dimension dimension,
			Viewport viewport,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext? containingInsert,
			Matrix4 parentTransform,
			int depth,
			HashSet<string> activeBlocks,
			out RenderNode node)
		{
			node = null;

			BlockRecord block = dimension.Block;
			if (block == null || block.Entities == null || block.Entities.Count == 0)
			{
				return false;
			}

			string blockKey = getBlockKey(block);
			if (activeBlocks.Contains(blockKey))
			{
				this._log.Add(dimension.Handle, dimension.SubclassMarker, RenderStatus.Error, $"Circular DIMENSION block detected for '{block.Name}'.");
				return false;
			}

			activeBlocks.Add(blockKey);
			try
			{
				var children = new List<RenderNode>();
				foreach (Entity blockEntity in block.Entities)
				{
					if (blockEntity == null || blockEntity is AttributeDefinition)
					{
						continue;
					}

					RenderNode child = buildEntityNode(
						blockEntity,
						viewport,
						styleScaleToPaper,
						textScaleToPaper,
						containingInsert,
						parentTransform,
						depth + 1,
						activeBlocks);

					if (child != null)
					{
						children.Add(child);
					}
				}

				if (children.Count == 0)
				{
					return false;
				}

				node = new GroupNode(dimension.Handle, Matrix4.Identity, children);
				return true;
			}
			finally
			{
				activeBlocks.Remove(blockKey);
			}
		}

		private void buildLinearOrAlignedDimension(
			DimensionAligned dimension,
			bool isLinear,
			DimensionStyle style,
			List<RenderNode> nodes,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext? containingInsert,
			Viewport viewport,
			int depth,
			HashSet<string> activeBlocks)
		{
			XY p1 = (XY)dimension.FirstPoint;
			XY p2 = (XY)dimension.SecondPoint;
			XY dimLinePoint = (XY)dimension.DefinitionPoint;

			XY dimDirection;
			if (isLinear)
			{
				// Rotation is defined in the dimension's OCS; map it into WCS.
				double angle = ((DimensionLinear)dimension).Rotation;
				XYZ ocs = new XYZ(Math.Cos(angle), Math.Sin(angle), 0.0);
				XYZ wcs = TransformHelper.OcsToWcs(dimension.Normal) * ocs;
				dimDirection = new XY(wcs.X, wcs.Y);
			}
			else
			{
				dimDirection = p2 - p1;
			}

			if (!tryNormalize(dimDirection, out dimDirection))
			{
				dimDirection = XY.AxisX;
			}

			XY perp = perpendicularLeft(dimDirection);
			double projection1 = dot(dimLinePoint - p1, perp);
			double projection2 = dot(dimLinePoint - p2, perp);
			XY d1 = p1 + perp * projection1;
			XY d2 = p2 + perp * projection2;

			double scale = dimensionScale(style);
			double extOffset = style.ExtensionLineOffset * scale;
			double extExtension = style.ExtensionLineExtension * scale;
			double gapRaw = style.DimensionLineGap * scale;
			double gap = Math.Abs(gapRaw);
			bool boxText = gapRaw < 0.0;
			double arrowSize = Math.Max(0.0, style.ArrowSize * scale);

			StrokeStyle extStroke1 = createDimensionStroke(dimension, style.ExtensionLineColor, style.ExtensionLineWeight, style.LineTypeExt1, styleScaleToPaper, containingInsert);
			StrokeStyle extStroke2 = createDimensionStroke(dimension, style.ExtensionLineColor, style.ExtensionLineWeight, style.LineTypeExt2, styleScaleToPaper, containingInsert);
			StrokeStyle dimStroke = createDimensionStroke(dimension, style.DimensionLineColor, style.DimensionLineWeight, style.LineType, styleScaleToPaper, containingInsert);

			double side1 = Math.Abs(projection1) < 1e-9 ? 1.0 : Math.Sign(projection1);
			double side2 = Math.Abs(projection2) < 1e-9 ? 1.0 : Math.Sign(projection2);

			XY extDir = perp;
			if (Math.Abs(dimension.ExtLineRotation) > 1e-12)
			{
				extDir = rotate(perp, dimension.ExtLineRotation);
				if (!tryNormalize(extDir, out extDir))
				{
					extDir = perp;
				}
			}

			if (!style.SuppressFirstExtensionLine)
			{
				XY start = p1 + extDir * (side1 * extOffset);
				XY end = d1 + extDir * (side1 * extExtension);
				PathNode path = createLinePath(dimension.Handle, start, end, extStroke1);
				if (path != null)
				{
					nodes.Add(path);
				}
			}

			if (!style.SuppressSecondExtensionLine)
			{
				XY start = p2 + extDir * (side2 * extOffset);
				XY end = d2 + extDir * (side2 * extExtension);
				PathNode path = createLinePath(dimension.Handle, start, end, extStroke2);
				if (path != null)
				{
					nodes.Add(path);
				}
			}

			if (!tryNormalize(d2 - d1, out XY axis))
			{
				axis = dimDirection;
			}

			XY mid = new XY((d1.X + d2.X) * 0.5, (d1.Y + d2.Y) * 0.5);
			string text = getDimensionText(dimension, style);

			double span = distance(d1, d2);
			double textHeight = Math.Max(0.0, style.TextHeight * scale);
			double textWidth = estimateTextWidth(text, textHeight);
			bool canTextFitInside = text != null && (textWidth + 2.0 * gap) <= span;
			bool canArrowsFitInside = (2.0 * arrowSize + 2.0 * gap) <= span;

			bool textOutside = false;
			bool arrowsOutside = !canArrowsFitInside;
			if (text != null)
			{
				bool forceInside = style.TextInsideExtensions;
				bool textInside = forceInside || canTextFitInside;
				if (textInside && canArrowsFitInside)
				{
					textOutside = false;
					arrowsOutside = false;
				}
				else
				{
					switch (style.DimensionTextArrowFit)
					{
						case TextArrowFitType.Both:
							textOutside = true;
							arrowsOutside = true;
							break;
						case TextArrowFitType.ArrowsFirst:
							arrowsOutside = !canArrowsFitInside;
							textOutside = !textInside;
							break;
						case TextArrowFitType.TextFirst:
							textOutside = !textInside;
							arrowsOutside = !canArrowsFitInside;
							break;
						case TextArrowFitType.BestFit:
						default:
							if (canArrowsFitInside && !textInside)
							{
								textOutside = true;
								arrowsOutside = false;
							}
							else if (!canArrowsFitInside && textInside)
							{
								textOutside = false;
								arrowsOutside = true;
							}
							else
							{
								textOutside = true;
								arrowsOutside = true;
							}
							break;
					}
				}
			}

			double dimLineExt = 0.0;
			if (style.TickSize * scale > 1e-9 && style.DimensionLineExtension > 1e-12)
			{
				dimLineExt = style.DimensionLineExtension * scale;
			}

			XY dimStart = d1 - axis * dimLineExt;
			XY dimEnd = d2 + axis * dimLineExt;

			bool drawDimLineInside = !(textOutside && !style.TextOutsideExtensions);
			if (drawDimLineInside && (!style.SuppressFirstDimensionLine || !style.SuppressSecondDimensionLine))
			{
				bool breakForText = !dimension.IsTextUserDefinedLocation
					&& !textOutside
					&& text != null
					&& style.TextVerticalAlignment == DimensionTextVerticalAlignment.Centered
					&& textWidth > 1e-9;

				if (breakForText)
				{
					double breakHalf = (textWidth * 0.5) + gap;
					XY breakA = mid - axis * breakHalf;
					XY breakB = mid + axis * breakHalf;

					if (!style.SuppressFirstDimensionLine)
					{
						PathNode left = createLinePath(dimension.Handle, dimStart, breakA, dimStroke);
						if (left != null)
						{
							nodes.Add(left);
						}
					}

					if (!style.SuppressSecondDimensionLine)
					{
						PathNode right = createLinePath(dimension.Handle, breakB, dimEnd, dimStroke);
						if (right != null)
						{
							nodes.Add(right);
						}
					}
				}
				else
				{
					if (!style.SuppressFirstDimensionLine && !style.SuppressSecondDimensionLine)
					{
						PathNode full = createLinePath(dimension.Handle, dimStart, dimEnd, dimStroke);
						if (full != null)
						{
							nodes.Add(full);
						}
					}
					else if (style.SuppressFirstDimensionLine && !style.SuppressSecondDimensionLine)
					{
						PathNode half = createLinePath(dimension.Handle, mid, dimEnd, dimStroke);
						if (half != null)
						{
							nodes.Add(half);
						}
					}
					else if (!style.SuppressFirstDimensionLine && style.SuppressSecondDimensionLine)
					{
						PathNode half = createLinePath(dimension.Handle, dimStart, mid, dimStroke);
						if (half != null)
						{
							nodes.Add(half);
						}
					}
				}
			}

			BlockRecord arrow1Block = style.SeparateArrowBlocks ? style.DimArrow1 : style.ArrowBlock;
			BlockRecord arrow2Block = style.SeparateArrowBlocks ? style.DimArrow2 : style.ArrowBlock;

			if (!style.SuppressFirstDimensionLine)
			{
				XY arrow1Dir = arrowsOutside ? -axis : axis;
				if (dimension.FlipArrow1)
				{
					arrow1Dir = -arrow1Dir;
				}
				addDimensionArrow(nodes, dimension, style, arrow1Block, d1, arrow1Dir, arrowSize, dimStroke, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);
			}

			if (!style.SuppressSecondDimensionLine)
			{
				XY arrow2Dir = arrowsOutside ? axis : -axis;
				if (dimension.FlipArrow2)
				{
					arrow2Dir = -arrow2Dir;
				}
				addDimensionArrow(nodes, dimension, style, arrow2Block, d2, arrow2Dir, arrowSize, dimStroke, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);
			}

			if (text == null)
			{
				return;
			}

			XY textPos;
			bool userTextLocation = dimension.IsTextUserDefinedLocation;
			if (userTextLocation)
			{
				textPos = toOcsWorldPoint(dimension.TextMiddlePoint, dimension.Normal);
			}
			else
			{
				textPos = mid;
				double textOffset = gap + (textHeight * 0.5);
				double dimSide = Math.Sign(dot(dimLinePoint - mid, perp));
				if (Math.Abs(dimSide) < 1e-9)
				{
					dimSide = 1.0;
				}

					switch (style.TextVerticalAlignment)
					{
						case DimensionTextVerticalAlignment.Below:
							textPos += perp * (-dimSide * textOffset);
						break;
					case DimensionTextVerticalAlignment.Centered:
						break;
					default:
							textPos += perp * (dimSide * textOffset);
							break;
					}

					// Preserve vertical placement when shifting the text along the dimension axis.
					XY verticalOffset = textPos - mid;

					if (textOutside)
					{
						switch (style.TextHorizontalAlignment)
						{
							case DimensionTextHorizontalAlignment.Left:
							case DimensionTextHorizontalAlignment.OverFirstExtLine:
								textPos = (d1 - axis * (arrowSize + gap + (textWidth * 0.5))) + verticalOffset;
								break;
							case DimensionTextHorizontalAlignment.Right:
							case DimensionTextHorizontalAlignment.OverSecondExtLine:
							default:
								textPos = (d2 + axis * (arrowSize + gap + (textWidth * 0.5))) + verticalOffset;
								break;
						}
					}
					else
					{
						switch (style.TextHorizontalAlignment)
						{
							case DimensionTextHorizontalAlignment.Left:
							case DimensionTextHorizontalAlignment.OverFirstExtLine:
								textPos = (d1 + axis * (gap + arrowSize + (textWidth * 0.5))) + verticalOffset;
								break;
							case DimensionTextHorizontalAlignment.Right:
							case DimensionTextHorizontalAlignment.OverSecondExtLine:
								textPos = (d2 - axis * (gap + arrowSize + (textWidth * 0.5))) + verticalOffset;
								break;
						}
					}

				if (textOutside && style.TextMovement == TextMovement.AddLeaderWhenTextMoved)
				{
					PathNode leader = createLinePath(dimension.Handle, mid, textPos, dimStroke);
					if (leader != null)
					{
						nodes.Add(leader);
					}
				}
			}

			double textRotation = Math.Atan2(axis.Y, axis.X);
			if ((!textOutside && style.TextInsideHorizontal) || (textOutside && style.TextOutsideHorizontal))
			{
				textRotation = 0.0;
			}

			addDimensionTextNode(nodes, dimension, style, text, textPos, textRotation, textScaleToPaper, containingInsert);

			if (boxText && !userTextLocation)
			{
				addDimensionTextBox(nodes, dimension, textPos, textRotation, textWidth, textHeight, gap, dimStroke);
			}
		}

		private void buildAngular3PointDimension(
			DimensionAngular3Pt dimension,
			DimensionStyle style,
			List<RenderNode> nodes,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext? containingInsert,
			Viewport viewport,
			int depth,
			HashSet<string> activeBlocks)
		{
			XY center = (XY)dimension.AngleVertex;
			XY p1 = (XY)dimension.FirstPoint;
			XY p2 = (XY)dimension.SecondPoint;
			XY selector = (XY)dimension.DefinitionPoint;

			double radius = distance(center, selector);
			if (radius < 1e-9)
			{
				radius = Math.Max(distance(center, p1), distance(center, p2));
			}

			if (radius < 1e-9)
			{
				return;
			}

			double a1 = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
			double a2 = Math.Atan2(p2.Y - center.Y, p2.X - center.X);
			double selectorAngle = Math.Atan2(selector.Y - center.Y, selector.X - center.X);
			selectArcThroughAngle(a1, a2, selectorAngle, out double start, out double end, out double sweep);

				StrokeStyle dimStroke = createDimensionStroke(dimension, style.DimensionLineColor, style.DimensionLineWeight, style.LineType, styleScaleToPaper, containingInsert);
				StrokeStyle extStroke = createDimensionStroke(dimension, style.ExtensionLineColor, style.ExtensionLineWeight, style.LineTypeExt1, styleScaleToPaper, containingInsert);

			List<XY> arcPoints = buildArcPoints(center, radius, start, end);
			PathNode arcPath = createPolylinePath(dimension.Handle, arcPoints, dimStroke, closed: false);
			if (arcPath != null)
			{
				nodes.Add(arcPath);
			}

			XY arcStart = polarPoint(center, radius, start);
			XY arcEnd = polarPoint(center, radius, end);
			PathNode ext1 = createLinePath(dimension.Handle, center, arcStart, extStroke);
			PathNode ext2 = createLinePath(dimension.Handle, center, arcEnd, extStroke);
			if (ext1 != null)
			{
				nodes.Add(ext1);
			}
			if (ext2 != null)
			{
				nodes.Add(ext2);
			}

			double scale = dimensionScale(style);
			double arrowSize = Math.Max(0.0, style.ArrowSize * scale);
			XY tangentStart = new XY(-Math.Sin(start), Math.Cos(start));
			XY tangentEnd = new XY(-Math.Sin(end), Math.Cos(end));
			BlockRecord arrow1Block = style.SeparateArrowBlocks ? style.DimArrow1 : style.ArrowBlock;
			BlockRecord arrow2Block = style.SeparateArrowBlocks ? style.DimArrow2 : style.ArrowBlock;

			addDimensionArrow(nodes, dimension, style, arrow1Block, arcStart, tangentStart, arrowSize, dimStroke, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);
			addDimensionArrow(nodes, dimension, style, arrow2Block, arcEnd, -tangentEnd, arrowSize, dimStroke, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);

			string text = getDimensionText(dimension, style);
			if (text == null)
			{
				return;
			}

			XY textPos;
			if (dimension.IsTextUserDefinedLocation)
			{
				textPos = toOcsWorldPoint(dimension.TextMiddlePoint, dimension.Normal);
			}
			else
			{
				double textAngle = start + sweep * 0.5;
				double textRadius = radius + Math.Abs(style.DimensionLineGap * scale) + style.TextHeight * scale * 0.5;
				textPos = polarPoint(center, textRadius, textAngle);
			}

				double rotation = style.TextOutsideHorizontal ? 0.0 : (start + sweep * 0.5 + Math.PI * 0.5);
				addDimensionTextNode(nodes, dimension, style, text, textPos, rotation, textScaleToPaper, containingInsert);
			}

		private void buildAngular2LineDimension(
			DimensionAngular2Line dimension,
			DimensionStyle style,
			List<RenderNode> nodes,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext? containingInsert,
			Viewport viewport,
			int depth,
			HashSet<string> activeBlocks)
		{
			XY a1 = (XY)dimension.FirstPoint;
			XY a2 = (XY)dimension.SecondPoint;
			XY b1 = (XY)dimension.AngleVertex;
			XY b2 = (XY)dimension.DefinitionPoint;

			if (!tryIntersectInfiniteLines(a1, a2, b1, b2, out XY center))
			{
				center = b1;
			}

			XY selectorPoint = (XY)dimension.DimensionArc;
			double radius = distance(center, selectorPoint);
			if (radius < 1e-9)
			{
				radius = Math.Max(distance(center, a1), distance(center, b2));
			}

			if (radius < 1e-9)
			{
				return;
			}

			double angle1 = Math.Atan2(a2.Y - a1.Y, a2.X - a1.X);
			double angle2 = Math.Atan2(b2.Y - b1.Y, b2.X - b1.X);
			double selectorAngle = Math.Atan2(selectorPoint.Y - center.Y, selectorPoint.X - center.X);
			selectArcThroughAngle(angle1, angle2, selectorAngle, out double start, out double end, out double sweep);

				StrokeStyle dimStroke = createDimensionStroke(dimension, style.DimensionLineColor, style.DimensionLineWeight, style.LineType, styleScaleToPaper, containingInsert);
				StrokeStyle extStroke = createDimensionStroke(dimension, style.ExtensionLineColor, style.ExtensionLineWeight, style.LineTypeExt1, styleScaleToPaper, containingInsert);

			List<XY> arcPoints = buildArcPoints(center, radius, start, end);
			PathNode arcPath = createPolylinePath(dimension.Handle, arcPoints, dimStroke, closed: false);
			if (arcPath != null)
			{
				nodes.Add(arcPath);
			}

			XY arcStart = polarPoint(center, radius, start);
			XY arcEnd = polarPoint(center, radius, end);
			PathNode ext1 = createLinePath(dimension.Handle, center, arcStart, extStroke);
			PathNode ext2 = createLinePath(dimension.Handle, center, arcEnd, extStroke);
			if (ext1 != null)
			{
				nodes.Add(ext1);
			}
			if (ext2 != null)
			{
				nodes.Add(ext2);
			}

			double scale = dimensionScale(style);
			double arrowSize = Math.Max(0.0, style.ArrowSize * scale);
			XY tangentStart = new XY(-Math.Sin(start), Math.Cos(start));
			XY tangentEnd = new XY(-Math.Sin(end), Math.Cos(end));
			BlockRecord arrow1Block = style.SeparateArrowBlocks ? style.DimArrow1 : style.ArrowBlock;
			BlockRecord arrow2Block = style.SeparateArrowBlocks ? style.DimArrow2 : style.ArrowBlock;

			addDimensionArrow(nodes, dimension, style, arrow1Block, arcStart, tangentStart, arrowSize, dimStroke, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);
			addDimensionArrow(nodes, dimension, style, arrow2Block, arcEnd, -tangentEnd, arrowSize, dimStroke, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);

			string text = getDimensionText(dimension, style);
			if (text == null)
			{
				return;
			}

			XY textPos;
			if (dimension.IsTextUserDefinedLocation)
			{
				textPos = toOcsWorldPoint(dimension.TextMiddlePoint, dimension.Normal);
			}
			else
			{
				double textAngle = start + sweep * 0.5;
				double textRadius = radius + Math.Abs(style.DimensionLineGap * scale) + style.TextHeight * scale * 0.5;
				textPos = polarPoint(center, textRadius, textAngle);
			}

			double rotation = style.TextOutsideHorizontal ? 0.0 : (start + sweep * 0.5 + Math.PI * 0.5);
			addDimensionTextNode(nodes, dimension, style, text, textPos, rotation, textScaleToPaper, containingInsert);
		}

		private void buildRadiusDimension(
			DimensionRadius dimension,
			DimensionStyle style,
			List<RenderNode> nodes,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext? containingInsert,
			Viewport viewport,
			int depth,
			HashSet<string> activeBlocks)
		{
			XY center = (XY)dimension.DefinitionPoint;
			XY edge = (XY)dimension.AngleVertex;
			if (!tryNormalize(edge - center, out XY dir))
			{
				return;
			}

				StrokeStyle dimStroke = createDimensionStroke(dimension, style.DimensionLineColor, style.DimensionLineWeight, style.LineType, styleScaleToPaper, containingInsert);
			PathNode radial = createLinePath(dimension.Handle, center, edge, dimStroke);
			if (radial != null)
			{
				nodes.Add(radial);
			}

			double scale = dimensionScale(style);
			double arrowSize = Math.Max(0.0, style.ArrowSize * scale);
			BlockRecord arrowBlock = style.SeparateArrowBlocks ? style.DimArrow1 : style.ArrowBlock;
			addDimensionArrow(nodes, dimension, style, arrowBlock, edge, -dir, arrowSize, dimStroke, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);

				addCenterMark(nodes, dimension, style, center, styleScaleToPaper, containingInsert);

			string text = getDimensionText(dimension, style);
			if (text == null)
			{
				return;
			}

			XY textPos;
			if (dimension.IsTextUserDefinedLocation)
			{
				textPos = toOcsWorldPoint(dimension.TextMiddlePoint, dimension.Normal);
			}
			else
			{
					double radius = distance(center, edge);
					double gap = Math.Abs(style.DimensionLineGap * scale);
					double outsideDistance = radius + Math.Max(style.TextHeight * scale, arrowSize);

					// DIMTIX behaves differently for radial/diameter dimensions: when set, it forces text outside.
					double textDistance = style.TextInsideExtensions ? outsideDistance : radius * 0.5;
					textPos = center + dir * (textDistance + gap);
				}

				bool isOutside = distance(center, textPos) > distance(center, edge) + 1e-9;
				bool forceHorizontal = isOutside ? style.TextOutsideHorizontal : style.TextInsideHorizontal;
				double rotation = forceHorizontal ? 0.0 : Math.Atan2(dir.Y, dir.X);
				addDimensionTextNode(nodes, dimension, style, text, textPos, rotation, textScaleToPaper, containingInsert);
			}

		private void buildDiameterDimension(
			DimensionDiameter dimension,
			DimensionStyle style,
			List<RenderNode> nodes,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext? containingInsert,
			Viewport viewport,
			int depth,
			HashSet<string> activeBlocks)
		{
			XY first = (XY)dimension.AngleVertex;
			XY second = (XY)dimension.DefinitionPoint;
			if (!tryNormalize(second - first, out XY dir))
			{
				return;
			}

				StrokeStyle dimStroke = createDimensionStroke(dimension, style.DimensionLineColor, style.DimensionLineWeight, style.LineType, styleScaleToPaper, containingInsert);
			PathNode diameter = createLinePath(dimension.Handle, first, second, dimStroke);
			if (diameter != null)
			{
				nodes.Add(diameter);
			}

			double scale = dimensionScale(style);
			double arrowSize = Math.Max(0.0, style.ArrowSize * scale);
			BlockRecord arrow1Block = style.SeparateArrowBlocks ? style.DimArrow1 : style.ArrowBlock;
			BlockRecord arrow2Block = style.SeparateArrowBlocks ? style.DimArrow2 : style.ArrowBlock;
			addDimensionArrow(nodes, dimension, style, arrow1Block, first, dir, arrowSize, dimStroke, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);
			addDimensionArrow(nodes, dimension, style, arrow2Block, second, -dir, arrowSize, dimStroke, styleScaleToPaper, textScaleToPaper, containingInsert, viewport, depth, activeBlocks);

				addCenterMark(nodes, dimension, style, new XY((first.X + second.X) * 0.5, (first.Y + second.Y) * 0.5), styleScaleToPaper, containingInsert);

			string text = getDimensionText(dimension, style);
			if (text == null)
			{
				return;
			}

			XY textPos;
			if (dimension.IsTextUserDefinedLocation)
			{
				textPos = toOcsWorldPoint(dimension.TextMiddlePoint, dimension.Normal);
			}
			else
			{
				double gap = Math.Abs(style.DimensionLineGap * scale) + style.TextHeight * scale * 0.5;
				XY center = new XY((first.X + second.X) * 0.5, (first.Y + second.Y) * 0.5);
				textPos = center + perpendicularLeft(dir) * gap;
			}

				double rotation = style.TextOutsideHorizontal ? 0.0 : Math.Atan2(dir.Y, dir.X);
				addDimensionTextNode(nodes, dimension, style, text, textPos, rotation, textScaleToPaper, containingInsert);
			}

		private void buildOrdinateDimension(
			DimensionOrdinate dimension,
			DimensionStyle style,
			List<RenderNode> nodes,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext? containingInsert)
		{
			double scale = dimensionScale(style);
			double minOffset = 2.0 * style.ArrowSize * scale;
			XY ref1 = (XY)dimension.FeatureLocation;
			XY ref2 = (XY)dimension.LeaderEndpoint;
			XY refDim = ref2 - ref1;
			double rotation = dimension.HorizontalDirection;
			int side = 1;

			if (dimension.IsOrdinateTypeX)
			{
				rotation += Math.PI * 0.5;
			}

			XY ocsDimRef = rotate(refDim, -rotation);
			XY p1;
			XY p2;

			if (ocsDimRef.X >= 0.0)
			{
				if (ocsDimRef.X >= 2.0 * minOffset)
				{
					p1 = new XY(ocsDimRef.X - minOffset, 0.0);
					p2 = new XY(ocsDimRef.X - minOffset, ocsDimRef.Y);
				}
				else
				{
					p1 = new XY(minOffset, 0.0);
					p2 = new XY(ocsDimRef.X - minOffset, ocsDimRef.Y);
				}
			}
			else
			{
				if (ocsDimRef.X <= -2.0 * minOffset)
				{
					p1 = new XY(ocsDimRef.X + minOffset, 0.0);
					p2 = new XY(ocsDimRef.X + minOffset, ocsDimRef.Y);
				}
				else
				{
					p1 = new XY(-minOffset, 0.0);
					p2 = new XY(ocsDimRef.X + minOffset, ocsDimRef.Y);
				}
				side = -1;
			}

			p1 = ref1 + rotate(p1, rotation);
			p2 = ref1 + rotate(p2, rotation);
			XY start = polarPoint(ref1, style.ExtensionLineOffset * scale, rotation);

				StrokeStyle stroke = createDimensionStroke(dimension, style.DimensionLineColor, style.DimensionLineWeight, style.LineType, styleScaleToPaper, containingInsert);
			PathNode segment1 = createLinePath(dimension.Handle, start, p1, stroke);
			PathNode segment2 = createLinePath(dimension.Handle, p1, p2, stroke);
			PathNode segment3 = createLinePath(dimension.Handle, p2, ref2, stroke);

			if (segment1 != null)
			{
				nodes.Add(segment1);
			}
			if (segment2 != null)
			{
				nodes.Add(segment2);
			}
			if (segment3 != null)
			{
				nodes.Add(segment3);
			}

			string text = getDimensionText(dimension, style);
			if (text == null)
			{
				return;
			}

			XY textPos = dimension.IsTextUserDefinedLocation
				? toOcsWorldPoint(dimension.TextMiddlePoint, dimension.Normal)
				: polarPoint(ref2, side * Math.Abs(style.DimensionLineGap * scale), rotation);

			double textRotation = style.TextInsideHorizontal || style.TextOutsideHorizontal ? 0.0 : rotation;
			addDimensionTextNode(nodes, dimension, style, text, textPos, textRotation, textScaleToPaper, containingInsert);
		}

			private void addCenterMark(List<RenderNode> nodes, Dimension dimension, DimensionStyle style, XY center, double styleScaleToPaper, InsertRenderContext? containingInsert)
			{
				double scale = dimensionScale(style);
				double size = Math.Abs(style.CenterMarkSize * scale);
				if (size < 1e-9)
				{
					return;
				}

				StrokeStyle stroke = createDimensionStroke(dimension, style.ExtensionLineColor, style.ExtensionLineWeight, style.LineTypeExt1, styleScaleToPaper, containingInsert);
				PathNode xAxis = createLinePath(dimension.Handle, new XY(center.X - size, center.Y), new XY(center.X + size, center.Y), stroke);
				PathNode yAxis = createLinePath(dimension.Handle, new XY(center.X, center.Y - size), new XY(center.X, center.Y + size), stroke);
				if (xAxis != null)
				{
					nodes.Add(xAxis);
			}
			if (yAxis != null)
			{
				nodes.Add(yAxis);
			}
		}

		private void addDimensionArrow(
			List<RenderNode> nodes,
			Dimension dimension,
			DimensionStyle style,
			BlockRecord arrowBlock,
			XY tip,
			XY direction,
			double arrowSize,
			StrokeStyle dimStroke,
			double styleScaleToPaper,
			double textScaleToPaper,
			InsertRenderContext? containingInsert,
			Viewport viewport,
			int depth,
			HashSet<string> activeBlocks)
		{
			if (arrowSize <= 1e-9 || !tryNormalize(direction, out XY dir))
			{
				return;
			}

			double tickSize = style.TickSize * dimensionScale(style);
			if (tickSize > 1e-9)
			{
				XY perp = perpendicularLeft(dir);
				XY diag = dir + perp;
				if (!tryNormalize(diag, out diag))
				{
					diag = perp;
				}

				XY a = tip - diag * (0.5 * tickSize);
				XY b = tip + diag * (0.5 * tickSize);
				PathNode tick = createLinePath(dimension.Handle, a, b, dimStroke);
				if (tick != null)
				{
					nodes.Add(tick);
				}
				return;
			}

				if (arrowBlock != null)
				{
					var insert = new Insert(arrowBlock)
					{
						InsertPoint = new XYZ(tip.X, tip.Y, 0.0),
						XScale = arrowSize,
						YScale = arrowSize,
						ZScale = 1.0,
						Rotation = Math.Atan2(dir.Y, dir.X),
						Layer = dimension.Layer,
						// Force explicit, already-resolved styling so arrow blocks don't accidentally inherit ByBlock from the
						// surrounding INSERT context (the arrow insert is an implementation detail, not a nested block).
						Color = dimStroke.Color,
						LineWeight = LineWeightType.ByLayer,
						LineType = LineType.ByLayer,
						Normal = dimension.Normal,
					};

					RenderNode arrowNode = buildEntityNode(
						insert,
						viewport,
						styleScaleToPaper,
						textScaleToPaper,
						containingInsert: null,
						parentTransform: Matrix4.Identity,
						depth: depth + 1,
						activeBlocks: activeBlocks);

				if (arrowNode != null)
				{
					nodes.Add(arrowNode);
					return;
				}
			}

			XY perpDir = perpendicularLeft(dir);
			XY back = tip - dir * arrowSize;
			XY p1 = back + perpDir * (arrowSize * 0.3);
			XY p2 = back - perpDir * (arrowSize * 0.3);
			var segs = new PathSegment[]
			{
				new MoveTo(tip),
				new LineTo(p1),
				new LineTo(p2),
				new ClosePath(),
			};

			nodes.Add(new PathNode(
				dimension.Handle,
				segs,
				stroke: null,
				fill: new FillStyle(dimStroke.Color)));
		}

			private void addDimensionTextNode(
				List<RenderNode> nodes,
				Dimension dimension,
				DimensionStyle style,
				string text,
			XY position,
			double rotation,
			double textScaleToPaper,
			InsertRenderContext? containingInsert)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			double scale = dimensionScale(style);
			double height = Math.Max(1e-6, style.TextHeight * scale);
			double finalRotation = normalizeAngleSigned(rotation + dimension.TextRotation);
			var textEntity = new TextEntity
			{
				Value = text,
				Height = height,
				InsertPoint = new XYZ(position.X, position.Y, 0.0),
				AlignmentPoint = new XYZ(position.X, position.Y, 0.0),
				HorizontalAlignment = TextHorizontalAlignment.Center,
				VerticalAlignment = TextVerticalAlignmentType.Middle,
				Rotation = finalRotation,
				Style = style.Style ?? TextStyle.Default,
				Color = style.TextColor,
				Layer = dimension.Layer,
				Normal = dimension.Normal,
			};

			TextRunNode node = buildText(textEntity, textScaleToPaper, containingInsert);
			if (node != null)
			{
				nodes.Add(node);
				}
			}

			private static double estimateTextWidth(string text, double height)
			{
				if (string.IsNullOrEmpty(text) || height <= 1e-12)
				{
					return 0.0;
				}

				// Keep this consistent with TextLayoutEngine's internal ApproximateTextMetrics.
				const double averageGlyphWidth = 0.55;
				return text.Length * averageGlyphWidth * height;
			}

			private static void addDimensionTextBox(
				List<RenderNode> nodes,
				Dimension dimension,
				XY center,
				double rotation,
				double textWidth,
				double textHeight,
				double gap,
				StrokeStyle stroke)
			{
				if (nodes == null || dimension == null || stroke == null)
				{
					return;
				}

				double halfW = (textWidth * 0.5) + gap;
				double halfH = (textHeight * 0.5) + gap;
				if (halfW <= 1e-9 || halfH <= 1e-9)
				{
					return;
				}

				XY xAxis = new XY(Math.Cos(rotation), Math.Sin(rotation));
				XY yAxis = perpendicularLeft(xAxis);

				XY p1 = center - xAxis * halfW - yAxis * halfH;
				XY p2 = center + xAxis * halfW - yAxis * halfH;
				XY p3 = center + xAxis * halfW + yAxis * halfH;
				XY p4 = center - xAxis * halfW + yAxis * halfH;

				var segs = new PathSegment[]
				{
					new MoveTo(p1),
					new LineTo(p2),
					new LineTo(p3),
					new LineTo(p4),
					new ClosePath(),
				};

				nodes.Add(new PathNode(dimension.Handle, segs, stroke, fill: null));
			}

			private static string getDimensionText(Dimension dimension, DimensionStyle style)
			{
				if (dimension == null)
				{
				return null;
			}

			if (string.Equals(dimension.Text, " ", StringComparison.Ordinal))
			{
				return null;
			}

			string value = dimension.GetMeasurementText(style);
			if (string.IsNullOrWhiteSpace(value))
			{
				return null;
			}

			return value;
		}

		private StrokeStyle createDimensionStroke(
			Dimension dimension,
			ACadSharp.Color color,
			LineWeightType lineWeight,
			LineType lineType,
			double styleScaleToPaper,
			InsertRenderContext? containingInsert)
		{
			// Use the same style resolver as normal entities so ByLayer/ByBlock and dash patterns behave consistently.
			// The synthetic entity isn't part of the document graph, so bake $LTSCALE into LineTypeScale.
			double globalLtScale = dimension?.Document?.Header?.LineTypeScale ?? 1.0;
			var proxy = new Line
			{
				StartPoint = XYZ.Zero,
				EndPoint = XY.AxisX.Convert<XYZ>(),
				Layer = dimension?.Layer,
				Color = color,
				LineWeight = lineWeight,
				LineType = lineType ?? LineType.ByLayer,
				LineTypeScale = globalLtScale,
			};

			return resolveStroke(proxy, styleScaleToPaper, containingInsert);
		}

		private static PathNode createLinePath(ulong handle, XY start, XY end, StrokeStyle stroke)
		{
			if (distance(start, end) < 1e-9 || stroke == null)
			{
				return null;
			}

			var segs = new PathSegment[]
			{
				new MoveTo(start),
				new LineTo(end),
			};

			return new PathNode(handle, segs, stroke, fill: null);
		}

		private static PathNode createPolylinePath(ulong handle, IReadOnlyList<XY> points, StrokeStyle stroke, bool closed)
		{
			if (points == null || points.Count < 2 || stroke == null)
			{
				return null;
			}

			var segs = new List<PathSegment>(points.Count + 1)
			{
				new MoveTo(points[0])
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

		private List<XY> buildArcPoints(XY center, double radius, double startAngle, double endAngle)
		{
			if (radius < 1e-9)
			{
				return new List<XY>();
			}

			double sweep = endAngle - startAngle;
			if (sweep <= 0.0)
			{
				sweep += Math.PI * 2.0;
			}

			int segments = Math.Max(8, (int)Math.Ceiling(this._configuration.ArcPrecision * (sweep / (Math.PI * 2.0))));
			segments = Math.Min(2048, segments);

			var points = new List<XY>(segments + 1);
			for (int i = 0; i <= segments; i++)
			{
				double t = (double)i / segments;
				double angle = startAngle + sweep * t;
				points.Add(polarPoint(center, radius, angle));
			}

			return points;
		}

		private static void selectArcThroughAngle(double angleA, double angleB, double selector, out double start, out double end, out double sweep)
		{
			double a = normalizeAnglePositive(angleA);
			double b = normalizeAnglePositive(angleB);
			double s = normalizeAnglePositive(selector);

			if (isAngleOnArcCounterClockwise(a, b, s))
			{
				start = a;
				end = a + deltaCounterClockwise(a, b);
			}
			else
			{
				start = b;
				end = b + deltaCounterClockwise(b, a);
			}

			sweep = end - start;
		}

		private static bool isAngleOnArcCounterClockwise(double start, double end, double angle)
		{
			double total = deltaCounterClockwise(start, end);
			double part = deltaCounterClockwise(start, angle);
			return part <= total + 1e-9;
		}

		private static double deltaCounterClockwise(double start, double end)
		{
			double delta = normalizeAnglePositive(end) - normalizeAnglePositive(start);
			if (delta < 0.0)
			{
				delta += Math.PI * 2.0;
			}

			return delta;
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
			if (Math.Abs(denominator) < 1e-12)
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

		private static XY toOcsWorldPoint(XYZ point, XYZ normal)
		{
			XYZ world = TransformHelper.OcsToWcs(normal) * point;
			return new XY(world.X, world.Y);
		}

		private static double dimensionScale(DimensionStyle style)
		{
			if (style == null || style.ScaleFactor <= 1e-9)
			{
				return 1.0;
			}

			return style.ScaleFactor;
		}

		private static XY polarPoint(XY origin, double radius, double angle)
		{
			return new XY(
				origin.X + radius * Math.Cos(angle),
				origin.Y + radius * Math.Sin(angle));
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

		private static string getBlockKey(BlockRecord block)
		{
			if (block == null)
			{
				return string.Empty;
			}

			if (!string.IsNullOrWhiteSpace(block.Name))
			{
				return block.Name;
			}

			return "#" + block.Handle.ToString();
		}

		private static double normalizeAnglePositive(double angle)
		{
			double twoPi = Math.PI * 2.0;
			double result = angle % twoPi;
			if (result < 0.0)
			{
				result += twoPi;
			}

			return result;
		}

		private static double normalizeAngleSigned(double angle)
		{
			double value = normalizeAnglePositive(angle);
			if (value > Math.PI)
			{
				value -= Math.PI * 2.0;
			}

			return value;
		}

		private static PathNode rectanglePath(ulong handle, XY min, XY max)
		{
			var segs = new PathSegment[]
			{
				new MoveTo(new XY(min.X, min.Y)),
				new LineTo(new XY(max.X, min.Y)),
				new LineTo(new XY(max.X, max.Y)),
				new LineTo(new XY(min.X, max.Y)),
				new ClosePath(),
			};
			return new PathNode(handle, segs, stroke: null, fill: null);
		}
	}
}
