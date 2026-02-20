using ACadSharp.Entities;
using ACadSharp.Extensions;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Pdf.Extensions;
using ACadSharp.Pdf.Core.Render.SceneGraph;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#if NETFRAMEWORK
using CSUtilities.Extensions;
#endif

namespace ACadSharp.Pdf.Core.IO
{
	internal class PdfPen
	{
		public double DenominatorScale { get { return this._layout.DenominatorScale; } }

		public PlotPaperUnits PaperUnits { get { return this._layout.PaperUnits; } }

		// The κ (kappa) for drawing a circle or an ellipse with four Bézier splines, specifying the
		// distance of the influence point from the starting or end point of a spline.
		// Petzold: 4/3 * tan(α / 4)
		// κ := 4/3 * (1 - cos(-π/4)) / sin(π/4)) <=> 4/3 * (sqrt(2) - 1) <=> 4/3 * tan(π/8)
		// ReSharper disable once InconsistentNaming
		public const double κ = 0.5522847498307933984022516322796;

		private readonly PdfConfiguration _configuration;

		private readonly Layout _layout;

		private readonly StringBuilder _sb = new();

		private readonly UnderlayRasterCache _underlayRasterCache;
		private BoundingBox? _clipRectPaperOverride;

		public PdfPen(Layout layout, PdfConfiguration configuration)
		{
			this._layout = layout;
			this._configuration = configuration;
			this._underlayRasterCache = new UnderlayRasterCache(configuration);
		}

		public void DrawEntity(Entity entity)
		{
			this.DrawEntity(entity, new Transform());
		}

		public void DrawEntity(Entity entity, Transform transform)
		{
			if (!this._clipRectPaperOverride.HasValue)
			{
				this._clipRectPaperOverride = getPageClipRectPaper(this._layout);
			}

			this.writeEntityHeader(entity);

			this.applyStyle(entity);

			switch (entity)
			{
				case Arc arc:
					this.drawArc(arc, transform);
					break;
				case Circle circle:
					this.drawCircle(circle, transform);
					break;
				case Ellipse ellipse:
					this.drawEllpise(ellipse, transform);
					break;
				case Line line:
					this.drawLine(line, transform);
					break;
				case Ray ray:
					this.drawRay(ray, transform);
					break;
				case XLine xline:
					this.drawXLine(xline, transform);
					break;
				case Point point:
					this.drawPoint(point, transform);
					break;
				case IPolyline polyline:
					this.drawPolyline(polyline, transform);
					break;
				case IText text:
					this.drawText(text, transform);
					break;
				case Viewport viewport:
					this.drawViewport(viewport);
					break;
				case RasterImage image:
					this.drawRasterImage(image, transform);
					break;
				case PdfUnderlay underlay:
					this.drawPdfUnderlay(underlay, transform);
					break;
				default:
					this._configuration.Notify($"[{entity.SubclassMarker}] Drawing not implemented.", NotificationType.NotImplemented);
					break;
			}

			this.writeEntityEnd(entity);
		}

		public override string ToString()
		{
			return _sb.ToString();
		}

		private void appendArray(string key, params double[] arr)
		{
			this._sb.AppendJoin(" ", arr.Select(d => this.toPdfDouble(d)));
			this._sb.AppendLine($" {key}");
		}

		private void appendPath(params XY[] vertices)
		{
			this.appendXY(vertices[0], PdfKey.BeginPath);

			for (int i = 1; vertices.Length > i; i++)
			{
				this.appendXY(vertices[i], PdfKey.Line);
			}

			this.appendXY(vertices[vertices.Length - 1], PdfKey.Stroke);
		}

		private void appendXY(double x, double y, string key)
		{
			this._sb.AppendLine($"{this.toPdfDouble(x)} {this.toPdfDouble(y)} {key}");
		}

		private void appendXY(IVector value, string key)
		{
			this.appendXY(value[0], value[1], key);
		}

		private void applyStyle(Entity entity)
		{
			LineWeightType lw = entity.GetActiveLineWeightType();
			double lwValue = lw.GetLineWeightValue();
			this._sb.AppendLine($"{lwValue.ToPdfUnit(PdfUnitType.Millimeter)} {PdfKey.LineWidth}");

			Color color = entity.GetActiveColor();

			if (color.Index == 7)
			{
				color = new Color(0, 0, 0);
			}

			this._sb.AppendLine(color.ToPdfString());

			LineType lt = entity.GetActiveLineType();
			if (this.drawableLineType(lt))
			{
				this.writeDashes(entity.GetActiveLineType(), lwValue.ToPdfUnit(PdfUnitType.Millimeter));
			}
			else
			{
				this._sb.AppendLine("[] 0 d");
			}
		}

		private void writeDashes(LineType lineType, double pointSize)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("[");
			foreach (LineType.Segment segment in lineType.Segments)
			{
				if (segment.IsPoint)
				{
					sb.Append(toPdfDouble(pointSize));
				}
				else
				{
					sb.Append(toPdfDouble(Math.Abs(segment.Length)));
				}

				sb.Append(' ');
			}

			this._sb.AppendLine($"{sb.ToString().Trim()}] 0 d");
		}

		private bool drawableLineType(LineType lineType)
		{
			return lineType.IsComplex && !lineType.HasShapes;
		}

		private void drawArc(Arc arc, Transform transform)
		{
			XY[] vertices = arc.PolygonalVertexes(this._configuration.ArcPrecision)
				.Select(v => transform.ApplyTransform(v))
				.Select(v => v.Convert<XY>())
				.ToArray();

			this.appendPath(vertices);
		}

		private void drawCircle(Circle circle, Transform transform)
		{
			BoundingBox rect = circle.GetBoundingBox();

			var min = transform.ApplyTransform(rect.Min);

			double δx = transform.Scale.X * circle.Radius;
			double δy = transform.Scale.Y * circle.Radius;

			double fx = δx * κ;
			double fy = δy * κ;
			double x0 = min.X + δx;
			double y0 = min.Y + δy;

			this.appendXY(x0 + δx, y0, PdfKey.BeginPath);
			this.appendArray(PdfKey.Arc, x0 + δx, y0 + fy, x0 + fx, y0 + δy, x0, y0 + δy);
			this.appendArray(PdfKey.Arc, x0 - fx, y0 + δy, x0 - δx, y0 + fy, x0 - δx, y0);
			this.appendArray(PdfKey.Arc, x0 - δx, y0 - fy, x0 - fx, y0 - δy, x0, y0 - δy);
			this.appendArray(PdfKey.Arc, x0 + fx, y0 - δy, x0 + δx, y0 - fy, x0 + δx, y0);
			this._sb.AppendLine($"h {PdfKey.Stroke}");
		}

		private void drawEllpise(Ellipse ellipse, Transform transform)
		{
			XY[] vertices = ellipse.PolygonalVertexes(this._configuration.ArcPrecision)
				.Select(v => transform.ApplyTransform(v))
				.Select(v => v.Convert<XY>())
				.ToArray();

			this.appendPath(vertices);
		}

		private void drawLine(Line line, Transform transform)
		{
			this.appendXY(transform.ApplyTransform(line.StartPoint), PdfKey.BeginPath);
			this.appendXY(transform.ApplyTransform(line.EndPoint), PdfKey.Line);

			this._sb.AppendLine(PdfKey.Stroke);
		}

		private void drawPoint(Point point, Transform transform)
		{
			double diff = this._configuration.DotSize / 2;
			XYZ p = transform.ApplyTransform(point.Location) - new XYZ(diff);

			this._sb.AppendLine($"{this.toPdfDouble(p.X)} {this.toPdfDouble(p.Y)} {this.toPdfDouble(this._configuration.DotSize)} {this.toPdfDouble(this._configuration.DotSize)} re");
			this._sb.AppendLine($"F");
		}

		private void drawPolyline(IPolyline polyline, Transform transform)
		{
			IEnumerable<XYZ> vertices = polyline.GetPoints<XYZ>(this._configuration.ArcPrecision)
				.Select(v => v = transform.ApplyTransform(v));

			this.appendXY(vertices.First(), PdfKey.BeginPath);

			for (int i = 1; vertices.Count() > i; i++)
			{
				this.appendXY(vertices.ElementAt(i), PdfKey.Line);
			}

			if (polyline.IsClosed)
			{
				this.appendXY(vertices.Last(), PdfKey.Line);
				this.appendXY(vertices.First(), PdfKey.Line);
			}
			else
			{
				this.appendXY(vertices.Last(), PdfKey.Line);
			}

			this._sb.AppendLine(PdfKey.Stroke);
		}

		private void drawText(IText text, Transform transform)
		{
			this._sb.AppendLine(PdfKey.BasicTextStart);

			this._sb.Append("/F");
			this._sb.Append("1");   //Font id in the pdf, the font definition should be embedded
			this._sb.Append(' ');
			this._sb.Append(this.toPdfDouble(text.Height));
			this._sb.Append(' ');
			this._sb.Append(PdfKey.TypeFont);
			this._sb.AppendLine();

			this.appendXY(text.InsertPoint, "Td");

			switch (text)
			{
				case MText mtext:
					this._sb.AppendLine($"{this.toPdfDouble(text.Height)} TL");
					foreach (var l in mtext.GetTextLines())
					{
						this._sb.AppendLine($"T* ({l}) {PdfKey.TextString}");
					}
					break;
				default:
					this._sb.AppendLine($"({text.Value}) {PdfKey.TextString}");
					break;
			}

			this._sb.AppendLine(PdfKey.BasicTextEnd);
		}

		private void drawViewport(Viewport viewport)
		{
			BoundingBox box = viewport.GetBoundingBox();

			this.appendXY(box.Min, PdfKey.BeginPath);
			this.appendXY(box.Max, PdfKey.Line);
			this._sb.AppendLine(PdfKey.Stroke);

			//Draw rectangle
			this.appendArray(PdfKey.Rectangle, box.Min.X, box.Min.Y, box.Width, box.Height);
			this._sb.AppendLine(PdfKey.Stroke);

			//Limit viewport view
			this._sb.AppendLine(PdfKey.StackStart);

			this.appendArray(PdfKey.Rectangle, box.Min.X, box.Min.Y, box.Width, box.Height);
			this._sb.AppendLine("W n");

			var modelBox = viewport.GetModelBoundingBox();

			var df = modelBox.Min * viewport.ScaleFactor;

			Transform transform = new Transform();
			transform.Translation = box.Min - df;
			transform.Scale = new XYZ(viewport.ScaleFactor);

			BoundingBox previousClip = this._clipRectPaperOverride ?? BoundingBox.Null;
			try
			{
				this._clipRectPaperOverride = getViewportClipRectPaper(viewport);
				foreach (Entity e in selectViewportEntities(viewport))
				{
					this.DrawEntity(e, transform);
				}
			}
			finally
			{
				this._clipRectPaperOverride = previousClip.Extent == BoundingBoxExtent.Null ? null : previousClip;
			}

			this._sb.AppendLine(PdfKey.StackEnd);
		}

		private IEnumerable<Entity> selectViewportEntities(Viewport viewport)
		{
			if (viewport == null || viewport.Document == null)
			{
				yield break;
			}

			BoundingBox viewBox = viewport.GetModelBoundingBox();
			BoundingBox clipRect = viewBox;
			if (tryCreateExpandedRect((XY)viewBox.Min, (XY)viewBox.Max, out BoundingBox expanded))
			{
				clipRect = expanded;
			}

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
					if (InfiniteLineClipper.ClipRay(origin, direction, clipRect).HasValue)
					{
						yield return entity;
					}
					continue;
				}

				if (entity is XLine xline)
				{
					XY origin = new XY(xline.FirstPoint.X, xline.FirstPoint.Y);
					XY direction = new XY(xline.Direction.X, xline.Direction.Y);
					if (InfiniteLineClipper.ClipXLine(origin, direction, clipRect).HasValue)
					{
						yield return entity;
					}
					continue;
				}

				BoundingBox box = entity.GetBoundingBox();
				if (box.Extent == BoundingBoxExtent.Infinite)
				{
					continue;
				}

				if (viewBox.IsIn(box, out bool partialIn) || partialIn)
				{
					yield return entity;
				}
			}
		}

		private void drawRay(Ray ray, Transform transform)
		{
			if (ray == null)
			{
				return;
			}

			BoundingBox clipRect = this._clipRectPaperOverride ?? getPageClipRectPaper(this._layout);
			XYZ origin3 = transform.ApplyTransform(ray.StartPoint);
			XYZ dir3 = transformDirection(transform, ray.Direction);
			XY origin = new XY(origin3.X, origin3.Y);
			XY direction = new XY(dir3.X, dir3.Y);

			var clipped = InfiniteLineClipper.ClipRay(origin, direction, clipRect);
			if (!clipped.HasValue)
			{
				return;
			}

			this.appendXY(clipped.Value.Start, PdfKey.BeginPath);
			this.appendXY(clipped.Value.End, PdfKey.Line);
			this._sb.AppendLine(PdfKey.Stroke);
		}

		private void drawXLine(XLine xline, Transform transform)
		{
			if (xline == null)
			{
				return;
			}

			BoundingBox clipRect = this._clipRectPaperOverride ?? getPageClipRectPaper(this._layout);
			XYZ origin3 = transform.ApplyTransform(xline.FirstPoint);
			XYZ dir3 = transformDirection(transform, xline.Direction);
			XY origin = new XY(origin3.X, origin3.Y);
			XY direction = new XY(dir3.X, dir3.Y);

			var clipped = InfiniteLineClipper.ClipXLine(origin, direction, clipRect);
			if (!clipped.HasValue)
			{
				return;
			}

			this.appendXY(clipped.Value.Start, PdfKey.BeginPath);
			this.appendXY(clipped.Value.End, PdfKey.Line);
			this._sb.AppendLine(PdfKey.Stroke);
		}

		private static BoundingBox getViewportClipRectPaper(Viewport viewport)
		{
			BoundingBox box = viewport.GetBoundingBox();
			if (tryCreateExpandedRect((XY)box.Min, (XY)box.Max, out BoundingBox expanded))
			{
				return expanded;
			}
			return box;
		}

		private static BoundingBox getPageClipRectPaper(Layout layout)
		{
			if (layout == null)
			{
				return new BoundingBox(new XYZ(-10000.0, -10000.0, 0.0), new XYZ(10000.0, 10000.0, 0.0));
			}

			double width = layout.PaperWidth;
			double height = layout.PaperHeight;
			if (width <= 1e-9 || height <= 1e-9)
			{
				return new BoundingBox(new XYZ(-10000.0, -10000.0, 0.0), new XYZ(10000.0, 10000.0, 0.0));
			}

			if (tryCreateExpandedRect(new XY(0.0, 0.0), new XY(width, height), out BoundingBox expanded))
			{
				return expanded;
			}

			return new BoundingBox(new XYZ(0.0, 0.0, 0.0), new XYZ(width, height, 0.0));
		}

		private static bool tryCreateExpandedRect(XY min, XY max, out BoundingBox expanded)
		{
			expanded = BoundingBox.Null;

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
			if (double.IsNaN(margin) || double.IsInfinity(margin) || margin <= 0.0)
			{
				margin = 1.0;
			}

			expanded = new BoundingBox(
				new XYZ(minX - margin, minY - margin, 0.0),
				new XYZ(maxX + margin, maxY + margin, 0.0));
			return true;
		}

		private void drawRasterImage(RasterImage image, Transform transform)
		{
			if (image == null)
			{
				return;
			}

			if (!image.ShowImage)
			{
				return;
			}

			if (image.Definition == null || string.IsNullOrWhiteSpace(image.Definition.FileName))
			{
				this._configuration.Notify("[IMAGE] Missing IMAGEDEF file reference.", NotificationType.Warning);
				return;
			}

			if (!this._underlayRasterCache.TryLoadRasterImage(image.Definition.FileName, out var raster, out _, out string reason))
			{
				if (!this._configuration.SkipMissingImages)
				{
					this._configuration.Notify($"[IMAGE] Load failed: {reason}", NotificationType.Warning);
				}
				return;
			}

			double displayWidth = image.Size.X > 0 ? image.Size.X : raster.Width;
			double displayHeight = image.Size.Y > 0 ? image.Size.Y : raster.Height;
			if (displayWidth <= 0 || displayHeight <= 0)
			{
				return;
			}

			XYZ insert = transform.ApplyTransform(image.InsertPoint);
			XYZ u = transformDirection(transform, image.UVector);
			XYZ v = transformDirection(transform, image.VVector);

			byte[] rgb24 = UnderlayRasterCache.ApplyRasterImageAdjustments(raster.Rgb24Data, image.Brightness, image.Contrast, image.Fade);

			this._sb.AppendLine(PdfKey.StackStart);

			if (image.ClippingState && image.Flags.HasFlag(ImageDisplayFlags.UseClippingBoundary) && image.ClipMode == ClipMode.Inside)
			{
				appendImageClipPath(image, insert, u, v, displayWidth, displayHeight);
			}

			appendConcatMatrix(
				a: u.X * displayWidth, b: u.Y * displayWidth,
				c: v.X * displayHeight, d: v.Y * displayHeight,
				e: insert.X, f: insert.Y);

			appendInlineRgbImage(raster.Width, raster.Height, rgb24);
			this._sb.AppendLine(PdfKey.StackEnd);
		}

		private void drawPdfUnderlay(PdfUnderlay underlay, Transform transform)
		{
			if (underlay == null)
			{
				return;
			}

			if (!underlay.Flags.HasFlag(UnderlayDisplayFlags.ShowUnderlay))
			{
				return;
			}

			if (underlay.Definition == null || string.IsNullOrWhiteSpace(underlay.Definition.File))
			{
				this._configuration.Notify("[PDFUNDERLAY] Missing underlay file reference.", NotificationType.Warning);
				return;
			}

			int pageIndex = 0;
			if (!string.IsNullOrWhiteSpace(underlay.Definition.Page) && int.TryParse(underlay.Definition.Page, out int parsed) && parsed > 0)
			{
				pageIndex = parsed - 1;
			}

			int dpi = this._configuration.PdfUnderlayDpi <= 0 ? 150 : this._configuration.PdfUnderlayDpi;
			if (!this._underlayRasterCache.TryRasterizePdf(underlay.Definition.File, pageIndex, dpi, out var raster, out _, out string reason))
			{
				if (!this._configuration.SkipMissingImages)
				{
					this._configuration.Notify($"[PDFUNDERLAY] Rasterization failed: {reason}", NotificationType.Warning);
				}
				return;
			}

			var u = (PdfUnderlay)underlay.CloneTyped();
			u.ApplyTransform(transform);

			bool monochrome = u.Flags.HasFlag(UnderlayDisplayFlags.Monochrome);
			byte[] rgb24 = UnderlayRasterCache.ApplyUnderlayAdjustments(raster.Rgb24Data, u.Contrast, u.Fade, monochrome);

			Matrix4 ocsToWcs = CSMath.Matrix4.GetArbitraryAxis(u.Normal == XYZ.Zero ? XYZ.AxisZ : u.Normal);

			double cos = Math.Cos(u.Rotation);
			double sin = Math.Sin(u.Rotation);

			XYZ axisXOcs = new XYZ(cos * u.XScale, sin * u.XScale, 0.0);
			XYZ axisYOcs = new XYZ(-sin * u.YScale, cos * u.YScale, 0.0);

			XYZ axisX = ocsToWcs * axisXOcs;
			XYZ axisY = ocsToWcs * axisYOcs;

			this._sb.AppendLine(PdfKey.StackStart);

			if (u.Flags.HasFlag(UnderlayDisplayFlags.ClippingOn) && u.Flags.HasFlag(UnderlayDisplayFlags.ClipInsideMode))
			{
				appendUnderlayClipPath(u, ocsToWcs);
			}

			appendConcatMatrix(
				a: axisX.X, b: axisX.Y,
				c: axisY.X, d: axisY.Y,
				e: u.InsertPoint.X, f: u.InsertPoint.Y);

			appendInlineRgbImage(raster.Width, raster.Height, rgb24);
			this._sb.AppendLine(PdfKey.StackEnd);
		}

		private static XYZ transformDirection(Transform transform, XYZ vector)
		{
			Matrix4 m = transform.Matrix;
			XYZM r = m * new XYZM(vector.X, vector.Y, vector.Z, 0.0);
			return new XYZ(r.X, r.Y, r.Z);
		}

		private void appendUnderlayClipPath(PdfUnderlay underlay, Matrix4 ocsToWcs)
		{
			if (underlay.ClipBoundaryVertices == null || underlay.ClipBoundaryVertices.Count < 2)
			{
				return;
			}

			var vertices = underlay.ClipBoundaryVertices;
			List<XY> pts = new List<XY>();

			double cos = Math.Cos(underlay.Rotation);
			double sin = Math.Sin(underlay.Rotation);

			foreach (var v in vertices)
			{
				double xr = cos * v.X - sin * v.Y;
				double yr = sin * v.X + cos * v.Y;
				XYZ w = ocsToWcs * new XYZ(xr, yr, 0.0);
				pts.Add(new XY(underlay.InsertPoint.X + w.X, underlay.InsertPoint.Y + w.Y));
			}

			if (pts.Count == 2)
			{
				appendRectanglePath(pts[0], pts[1]);
			}
			else if (pts.Count >= 3)
			{
				appendPolygonPath(pts);
			}
		}

		private void appendImageClipPath(RasterImage image, XYZ insert, XYZ u, XYZ v, double displayWidth, double displayHeight)
		{
			List<XY> clipVertices = image.ClipBoundaryVertices;
			if (clipVertices == null || clipVertices.Count == 0)
			{
				clipVertices = new List<XY>
				{
					new XY(-0.5, -0.5),
					new XY(displayWidth - 0.5, displayHeight - 0.5),
				};
			}

			List<XY> world = new List<XY>();

			if (image.ClipType == ClipType.Rectangular && clipVertices.Count >= 2)
			{
				XY a = clipVertices[0];
				XY b = clipVertices[1];

				double minX = Math.Min(a.X, b.X);
				double minY = Math.Min(a.Y, b.Y);
				double maxX = Math.Max(a.X, b.X);
				double maxY = Math.Max(a.Y, b.Y);

				world.Add(toWorld(insert, u, v, minX, minY));
				world.Add(toWorld(insert, u, v, maxX, minY));
				world.Add(toWorld(insert, u, v, maxX, maxY));
				world.Add(toWorld(insert, u, v, minX, maxY));
			}
			else if (clipVertices.Count >= 3)
			{
				foreach (var cv in clipVertices)
				{
					world.Add(toWorld(insert, u, v, cv.X, cv.Y));
				}
			}

			if (world.Count == 2)
			{
				appendRectanglePath(world[0], world[1]);
				return;
			}

			if (world.Count >= 3)
			{
				appendPolygonPath(world);
				return;
			}
		}

		private static XY toWorld(XYZ insert, XYZ u, XYZ v, double px, double py)
		{
			return new XY(
				insert.X + (px * u.X) + (py * v.X),
				insert.Y + (px * u.Y) + (py * v.Y));
		}

		private void appendRectanglePath(XY p1, XY p2)
		{
			double minX = Math.Min(p1.X, p2.X);
			double minY = Math.Min(p1.Y, p2.Y);
			double maxX = Math.Max(p1.X, p2.X);
			double maxY = Math.Max(p1.Y, p2.Y);

			this.appendXY(minX, minY, PdfKey.BeginPath);
			this.appendXY(maxX, minY, PdfKey.Line);
			this.appendXY(maxX, maxY, PdfKey.Line);
			this.appendXY(minX, maxY, PdfKey.Line);
			this._sb.AppendLine($"h {PdfKey.ClippingPath} n");
		}

		private void appendPolygonPath(IReadOnlyList<XY> pts)
		{
			if (pts == null || pts.Count < 3)
			{
				return;
			}

			this.appendXY(pts[0], PdfKey.BeginPath);
			for (int i = 1; i < pts.Count; i++)
			{
				this.appendXY(pts[i], PdfKey.Line);
			}
			this._sb.AppendLine($"h {PdfKey.ClippingPath} n");
		}

		private void appendConcatMatrix(double a, double b, double c, double d, double e, double f)
		{
			this._sb.AppendLine($"{this.toPdfDouble(a)} {this.toPdfDouble(b)} {this.toPdfDouble(c)} {this.toPdfDouble(d)} {this.toPdfDouble(e)} {this.toPdfDouble(f)} {PdfKey.CurrentMatrix}");
		}

		private void appendInlineRgbImage(int width, int height, byte[] rgb24)
		{
			if (width <= 0 || height <= 0 || rgb24 == null || rgb24.Length == 0)
			{
				return;
			}

			this._sb.AppendLine("BI");
			this._sb.AppendLine($"/W {width}");
			this._sb.AppendLine($"/H {height}");
			this._sb.AppendLine("/BPC 8");
			this._sb.AppendLine("/CS /RGB");
			this._sb.AppendLine("/F /ASCIIHexDecode");
			this._sb.AppendLine("ID");
			appendAsciiHexData(this._sb, rgb24);
			this._sb.AppendLine(">");
			this._sb.AppendLine("EI");
		}

		private static void appendAsciiHexData(StringBuilder sb, byte[] data)
		{
			if (data == null || data.Length == 0)
			{
				return;
			}

			const int lineChars = 128;
			const string hex = "0123456789ABCDEF";

			int chars = 0;
			for (int i = 0; i < data.Length; i++)
			{
				byte value = data[i];
				sb.Append(hex[value >> 4]);
				sb.Append(hex[value & 0x0F]);
				chars += 2;

				if (chars >= lineChars)
				{
					sb.AppendLine();
					chars = 0;
				}
			}

			if (chars != 0)
			{
				sb.AppendLine();
			}
		}

		private string toPdfDouble(double value)
		{
			return (value / this.DenominatorScale).ToPdfUnit(this.PaperUnits).ToString(this._configuration.DecimalFormat);
		}

		private void writeEntityEnd(Entity entity)
		{
			_sb.AppendLine(PdfKey.CommentSeparator);
		}

		private void writeEntityHeader(Entity entity)
		{
			_sb.AppendLine($"% {entity.ObjectName} | {entity.Handle}");
		}
	}
}
