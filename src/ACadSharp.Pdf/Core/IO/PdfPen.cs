﻿using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Pdf.Extensions;
using CSMath;
using System.Linq;
using System.Text;

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
			this.applyStyle(entity);

			switch (entity)
			{
				case Arc arc:
					this.drawArc(arc);
					break;
				case Circle circle:
					this.drawCircle(circle);
					break;
				case Line line:
					this.drawLine(line);
					break;
				case Point point:
					this.drawPoint(point);
					break;
				case IPolyline polyline:
					this.drawPolyline(polyline);
					break;
				//case Viewport viewport:
				//	this.drawViewport(viewport);
				//	break;
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

		private void drawArc(Arc arc)
		{
			var vertices = arc.PolygonalVertexes(this._configuration.ArcPrecision);
			this.appendXY(vertices.First(), PdfKey.BeginPath);

			for (int i = 1; vertices.Count() > i; i++)
			{
				this.appendXY(vertices[i], PdfKey.Line);
			}

			this.appendXY(vertices.Last(), PdfKey.Stroke);
		}

		private void drawCircle(Circle circle)
		{
			BoundingBox rect = circle.GetBoundingBox();

			//double δx = rect.Max.X / 2;
			//double δy = rect.Max.Y / 2;

			double δx = circle.Radius;
			double δy = circle.Radius;

			double fx = δx * κ;
			double fy = δy * κ;
			double x0 = rect.Min.X + δx;
			double y0 = rect.Min.Y + δy;

			this.appendXY(x0 + δx, y0, PdfKey.BeginPath);
			this.appendArray(PdfKey.Arc, x0 + δx, y0 + fy, x0 + fx, y0 + δy, x0, y0 + δy);
			this.appendArray(PdfKey.Arc, x0 - fx, y0 + δy, x0 - δx, y0 + fy, x0 - δx, y0);
			this.appendArray(PdfKey.Arc, x0 - δx, y0 - fy, x0 - fx, y0 - δy, x0, y0 - δy);
			this.appendArray(PdfKey.Arc, x0 + fx, y0 - δy, x0 + δx, y0 - fy, x0 + δx, y0);
			this._sb.AppendLine($"h {PdfKey.Stroke}");
		}

		private void drawLine(Line line)
		{
			this.appendXY(line.StartPoint, PdfKey.BeginPath);
			this.appendXY(line.EndPoint, PdfKey.Line);
			this._sb.AppendLine(PdfKey.Stroke);
		}

		private void drawPoint(Point point)
		{
			double diff = this._configuration.DotSize / 2;
			XYZ p = point.Location - new XYZ(diff);

			this._sb.AppendLine($"{this.toPdfDouble(p.X)} {this.toPdfDouble(p.Y)} {this.toPdfDouble(this._configuration.DotSize)} {this.toPdfDouble(this._configuration.DotSize)} re");
			this._sb.AppendLine($"F");
		}

		private void drawPolyline(IPolyline polyline)
		{
			this.appendXY(polyline.Vertices.First().Location, PdfKey.BeginPath);

			for (int i = 1; polyline.Vertices.Count() > i; i++)
			{
				this.appendXY(polyline.Vertices.ElementAt(i).Location, PdfKey.Line);
			}


			if (polyline.IsClosed)
			{
				this.appendXY(polyline.Vertices.Last().Location, PdfKey.Line);
				this.appendXY(polyline.Vertices.First().Location, PdfKey.Line);
			}
			else
			{
				this.appendXY(polyline.Vertices.Last().Location, PdfKey.Stroke);
			}
		}

		private void drawViewport(Viewport viewport)
		{

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
