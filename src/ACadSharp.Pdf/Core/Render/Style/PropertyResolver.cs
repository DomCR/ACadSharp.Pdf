using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using ACadSharp.Pdf.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACadSharp.Pdf.Core.Render.Style
{
	public enum VisibilityDecision
	{
		Visible = 0,
		InvisibleFlag = 1,
		LayerOff = 2,
		LayerFrozen = 3,
		LayerNotPlottable = 4,
		ViewportFrozenLayer = 5,
	}

	public sealed class ResolvedStyle
	{
		public StrokeStyle Stroke { get; }
		public FillStyle Fill { get; }

		public ResolvedStyle(StrokeStyle stroke, FillStyle fill)
		{
			this.Stroke = stroke;
			this.Fill = fill;
		}
	}

	public sealed class PropertyResolver
	{
		private readonly PdfConfiguration _configuration;
		private readonly RenderLog _log;

		public PropertyResolver(PdfConfiguration configuration, RenderLog log)
		{
			this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
			this._log = log ?? throw new ArgumentNullException(nameof(log));
		}

		public VisibilityDecision GetVisibility(Entity entity, Viewport viewport)
		{
			if (entity == null) throw new ArgumentNullException(nameof(entity));

			if (entity.IsInvisible)
			{
				return VisibilityDecision.InvisibleFlag;
			}

			Layer layer = entity.Layer;
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

		public StrokeStyle ResolveStroke(Entity entity, Layout layout, double geometricScaleToPaper = 1.0, ACadSharp.Color? byBlockColor = null, LineWeightType? byBlockLineWeight = null, LineType byBlockLineType = null)
		{
			if (entity == null) throw new ArgumentNullException(nameof(entity));
			if (layout == null) throw new ArgumentNullException(nameof(layout));

			ACadSharp.Color color = resolveColor(entity, byBlockColor);
			double widthPt = resolveLineWeightPt(entity, byBlockLineWeight);
			IReadOnlyList<double> dashPt = resolveDashArrayPt(entity, layout, widthPt, geometricScaleToPaper, byBlockLineType);

			return new StrokeStyle(color, widthPt, dashPt, 0);
		}

		private ACadSharp.Color resolveColor(Entity entity, ACadSharp.Color? byBlockColor)
		{
			ACadSharp.Color color = entity.Color;
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
				return mapAci7(entity.Layer?.Color ?? new ACadSharp.Color((short)7));
			}

			// ByBlock
			if (byBlockColor.HasValue)
			{
				return mapAci7(byBlockColor.Value);
			}

			// Deterministic fallback until Stage 01 supplies ByBlock inheritance context.
			this._log.Add(entity.Handle, entity.SubclassMarker, RenderStatus.Rendered, "Color ByBlock without context; falling back to layer color.");
			return mapAci7(entity.Layer?.Color ?? new ACadSharp.Color((short)7));
		}

		private static ACadSharp.Color mapAci7(ACadSharp.Color color)
		{
			if (!color.IsTrueColor && color.Index == 7)
			{
				return new ACadSharp.Color(0, 0, 0);
			}

			return color;
		}

		private double resolveLineWeightPt(Entity entity, LineWeightType? byBlockLineWeight)
		{
			LineWeightType lw = entity.LineWeight;
			switch (lw)
			{
				case LineWeightType.ByLayer:
					lw = entity.Layer?.LineWeight ?? LineWeightType.Default;
					break;
				case LineWeightType.ByBlock:
					lw = byBlockLineWeight ?? (entity.Owner is BlockRecord record ? record.BlockEntity.LineWeight : LineWeightType.Default);
					if (lw == LineWeightType.ByBlock)
					{
						lw = LineWeightType.Default;
					}
					break;
				case LineWeightType.ByDIPs:
				case LineWeightType.Default:
					// Keep deterministic mapping; no access to $LWDEFAULT in the current model.
					lw = LineWeightType.Default;
					break;
			}

			double mm = lineWeightToMm(lw);
			return mm.ToPdfUnit(PdfUnitType.Millimeter);
		}

		private static double lineWeightToMm(LineWeightType lw)
		{
			switch (lw)
			{
				case LineWeightType.W0:
					return 0.001;
				case LineWeightType.Default:
				case LineWeightType.ByDIPs:
				case LineWeightType.ByBlock:
				case LineWeightType.ByLayer:
					return 0.01;
				default:
					if ((short)lw < 0)
					{
						return 0.01;
					}
					double mm = ((double)lw) / 100.0;
					if (mm < 0.0) mm = 0.0;
					if (mm > 10.0) mm = 10.0;
					return mm;
			}
		}

		private IReadOnlyList<double> resolveDashArrayPt(Entity entity, Layout layout, double widthPt, double geometricScaleToPaper, LineType byBlockLineType)
		{
			LineType lt = entity.LineType;
			if (lt == null)
			{
				return Array.Empty<double>();
			}

			if (string.Equals(lt.Name, LineType.ByLayerName, StringComparison.InvariantCultureIgnoreCase))
			{
				lt = entity.Layer?.LineType ?? LineType.Continuous;
			}
			else if (string.Equals(lt.Name, LineType.ByBlockName, StringComparison.InvariantCultureIgnoreCase))
			{
				lt = byBlockLineType ?? LineType.Continuous;
				if (byBlockLineType == null)
				{
					this._log.Add(entity.Handle, entity.SubclassMarker, RenderStatus.Rendered, "Linetype ByBlock without context; falling back to Continuous.");
				}
			}

			if (!lt.IsComplex)
			{
				return Array.Empty<double>();
			}

			if (lt.HasShapes)
			{
				this._log.Add(entity.Handle, entity.SubclassMarker, RenderStatus.Rendered, $"Complex linetype '{lt.Name}' has shapes/text; falling back to Continuous.");
				return Array.Empty<double>();
			}

			double globalLtScale = entity.Document?.Header?.LineTypeScale ?? 1.0;
			double entityLtScale = entity.LineTypeScale;
			double scale = globalLtScale * entityLtScale * geometricScaleToPaper;
			if (scale <= 0)
			{
				return Array.Empty<double>();
			}

			var segments = lt.Segments?.ToArray();
			if (segments == null || segments.Length == 0)
			{
				return Array.Empty<double>();
			}

			// Dash arrays in PDF are always non-negative. Convert the model-space lengths -> paper-space -> PDF points.
			// The input lengths are in "drawing units" for the current space, so multiply by geometric scale to paper.
			var arr = new List<double>(segments.Length);
			foreach (var seg in segments)
			{
				if (seg.IsPoint)
				{
					// Approximate a dot as a very short dash ~= line width.
					arr.Add(Math.Max(widthPt, 0.1));
					continue;
				}

				double lenPaperUnits = Math.Abs(seg.Length) * scale;
				double lenPt = (lenPaperUnits / layout.DenominatorScale).ToPdfUnit(layout.PaperUnits);
				if (lenPt <= 0)
				{
					lenPt = Math.Max(widthPt, 0.1);
				}

				arr.Add(lenPt);
			}

			// Avoid invalid patterns (all zeros / too small)
			if (arr.All(d => d <= 0))
			{
				return Array.Empty<double>();
			}

			return arr;
		}
	}
}
