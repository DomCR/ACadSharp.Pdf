using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Pdf.Extensions;
using CSMath;
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
		// The κ (kappa) for drawing a circle or an ellipse with four Bézier splines, specifying the
		// distance of the influence point from the starting or end point of a spline.
		// Petzold: 4/3 * tan(α / 4)
		// κ := 4/3 * (1 - cos(-π/4)) / sin(π/4)) <=> 4/3 * (sqrt(2) - 1) <=> 4/3 * tan(π/8)
		// ReSharper disable once InconsistentNaming
		public const double κ = 0.5522847498307933984022516322796;

		public PlotPaperUnits PaperUnits { get; set; }

		private readonly StringBuilder _sb = new();
		private readonly PdfConfiguration _configuration;

		public PdfPen(PdfConfiguration configuration)
		{
			this._configuration = configuration;
		}

		public void DrawEntity(Entity entity)
		{
			this.DrawEntity(entity, new Transform());
		}

		public void DrawEntity(Entity entity, Transform transform)
		{
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
				case Point point:
					this.drawPoint(point, transform);
					break;
				case IPolyline polyline:
					this.drawPolyline(polyline, transform);
					break;
				case TextEntity text:
					this.drawText(text, transform);
					break;
				case Viewport viewport:
					this.drawViewport(viewport);
					break;
				default:
					this._configuration.Notify($"[{entity.SubclassMarker}] Drawing not implemented.", NotificationType.NotImplemented);
					break;
			}
		}

		public override string ToString()
		{
			return _sb.ToString();
		}

		private void applyStyle(Entity entity)
		{
			LineweightType lw = LineweightType.Default;
			switch (entity.LineWeight)
			{
				case LineweightType.ByDIPs:
					break;
				case LineweightType.ByBlock:
					break;
				case LineweightType.ByLayer:
					lw = entity.Layer.LineWeight;
					break;
			}

			double lwValue = this._configuration.GetLineWeightValue(lw);
			this._sb.AppendLine($"{lwValue.ToPdfUnit(PdfUnitType.Millimeter)} {PdfKey.LineWidth}");

			Color color;
			if (entity.Color.IsByLayer)
			{
				color = entity.Layer.Color;
			}
			else
			{
				color = entity.Color;
			}

			if (color.Index == 7)
			{
				color = new Color(0, 0, 0);
			}

			this._sb.AppendLine(color.ToPdfString());
		}

		private void drawArc(Arc arc, Transform transform)
		{
			XY[] vertices = arc.PolygonalVertexes(this._configuration.ArcPrecision)
				.Select(v => transform.ApplyTransform((XYZ)v))
				.Select(v => (XY)v)
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
				.Select(v => v + (XY)ellipse.Center)
				.Select(v => transform.ApplyTransform((XYZ)v))
				.Select(v => (XY)v)
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
			IEnumerable<XYZ> vertices = polyline.Vertices.Select(v => transform.ApplyTransform(v.Location.Convert<XYZ>()));

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

		private void drawText(TextEntity text, Transform transform)
		{

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

			foreach (Entity e in viewport.SelectEntities())
			{
				this.DrawEntity(e, transform);
			}

			this._sb.AppendLine(PdfKey.StackEnd);
		}

		private void appendPath(params XY[] vertices)
		{
			this.appendXY(vertices.First(), PdfKey.BeginPath);

			for (int i = 1; vertices.Count() > i; i++)
			{
				this.appendXY(vertices[i], PdfKey.Line);
			}

			this.appendXY(vertices.Last(), PdfKey.Stroke);
		}

		private void appendArray(string key, params double[] arr)
		{
			this._sb.AppendJoin(" ", arr.Select(d => this.toPdfDouble(d)));
			this._sb.AppendLine($" {key}");
		}

		private void appendXY(double x, double y, string key)
		{
			this._sb.AppendLine($"{this.toPdfDouble(x)} {this.toPdfDouble(y)} {key}");
		}

		private void appendXY(IVector value, string key)
		{
			this.appendXY(value[0], value[1], key);
		}

		private void appendXY(XY value, string key)
		{
			this.appendXY(value.X, value.Y, key);
		}

		private void appendXY(XYZ value, string key)
		{
			this.appendXY((XY)value, key);
		}

		private string toPdfDouble(double value)
		{
			return value.ToPdfUnit(this.PaperUnits).ToString(this._configuration.DecimalFormat);
		}
	}
}
