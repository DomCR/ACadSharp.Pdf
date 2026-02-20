using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf;
using ACadSharp.Pdf.Core.Render.Style;
using ACadSharp.Pdf.Core.Render.Transforms;
using ACadSharp.Tables;
using CSMath;
using System;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class RenderInfrastructureTests
	{
		[Fact]
		public void OcsToWcs_MapsZAxisToNormal()
		{
			XYZ n = new XYZ(0.2, 0.4, 0.9).Normalize();
			var m = TransformHelper.OcsToWcs(n);

			XYZ mapped = (m * XYZ.AxisZ).Normalize();

			Assert.True(mapped.DistanceFrom(n) < 1e-9);
		}

		[Fact]
		public void OcsToWcs_IsIdentityForWorldZ()
		{
			var m = TransformHelper.OcsToWcs(XYZ.AxisZ);
			Assert.True((m * XYZ.AxisX).DistanceFrom(XYZ.AxisX) < 1e-9);
			Assert.True((m * XYZ.AxisY).DistanceFrom(XYZ.AxisY) < 1e-9);
			Assert.True((m * XYZ.AxisZ).DistanceFrom(XYZ.AxisZ) < 1e-9);
		}

		[Fact]
		public void OcsToWcs_ForNegativeZ_ProducesOrthonormalBasis()
		{
			var m = TransformHelper.OcsToWcs(-XYZ.AxisZ);
			XYZ x = (m * XYZ.AxisX).Normalize();
			XYZ y = (m * XYZ.AxisY).Normalize();
			XYZ z = (m * XYZ.AxisZ).Normalize();

			Assert.True(Math.Abs(x.Dot(y)) < 1e-9);
			Assert.True(Math.Abs(x.Dot(z)) < 1e-9);
			Assert.True(Math.Abs(y.Dot(z)) < 1e-9);
			Assert.True(z.DistanceFrom(-XYZ.AxisZ) < 1e-9);
		}

		[Fact]
		public void ResolveStroke_MapsAci7ToBlack()
		{
			var config = new PdfConfiguration();
			var log = new ACadSharp.Pdf.Core.Render.RenderLog();
			var resolver = new PropertyResolver(config, log);

			var layout = new Layout("L")
			{
				PaperUnits = PlotPaperUnits.Millimeters,
				DenominatorScale = 1.0,
			};

			var layer = new Layer("0") { Color = new Color((short)7) };
			var line = new Line
			{
				StartPoint = XYZ.Zero,
				EndPoint = XY.AxisX.Convert<XYZ>(),
				Layer = layer,
				Color = new Color((short)7),
			};

			var stroke = resolver.ResolveStroke(line, layout);
			Assert.Equal(0, stroke.Color.R);
			Assert.Equal(0, stroke.Color.G);
			Assert.Equal(0, stroke.Color.B);
		}

		[Fact]
		public void ResolveStroke_LineWeightMmToPoints()
		{
			var config = new PdfConfiguration();
			var log = new ACadSharp.Pdf.Core.Render.RenderLog();
			var resolver = new PropertyResolver(config, log);

			var layout = new Layout("L")
			{
				PaperUnits = PlotPaperUnits.Millimeters,
				DenominatorScale = 1.0,
			};

			var layer = new Layer("0") { Color = new Color((short)1) };
			var line = new Line
			{
				StartPoint = XYZ.Zero,
				EndPoint = XY.AxisX.Convert<XYZ>(),
				Layer = layer,
				LineWeight = LineWeightType.W25,
			};

			var stroke = resolver.ResolveStroke(line, layout);
			double expected = 0.25 * (72.0 / 25.4);
			Assert.True(Math.Abs(stroke.WidthPt - expected) < 1e-9);
		}

		[Fact]
		public void ResolveStroke_LineWeightNotScaledByGeometry()
		{
			var config = new PdfConfiguration();
			var log = new ACadSharp.Pdf.Core.Render.RenderLog();
			var resolver = new PropertyResolver(config, log);

			var layout = new Layout("L")
			{
				PaperUnits = PlotPaperUnits.Millimeters,
				DenominatorScale = 1.0,
			};

			var layer = new Layer("0") { Color = new Color((short)1) };
			var line = new Line
			{
				StartPoint = XYZ.Zero,
				EndPoint = XY.AxisX.Convert<XYZ>(),
				Layer = layer,
				LineWeight = LineWeightType.W25,
			};

			var s1 = resolver.ResolveStroke(line, layout, geometricScaleToPaper: 1.0);
			var s2 = resolver.ResolveStroke(line, layout, geometricScaleToPaper: 100.0);

			Assert.True(Math.Abs(s1.WidthPt - s2.WidthPt) < 1e-12);
		}

		[Fact]
		public void ResolveStroke_DashArrayScalesWithGeometryScale()
		{
			var config = new PdfConfiguration();
			var log = new ACadSharp.Pdf.Core.Render.RenderLog();
			var resolver = new PropertyResolver(config, log);

			var layout = new Layout("L")
			{
				PaperUnits = PlotPaperUnits.Millimeters,
				DenominatorScale = 1.0,
			};

			var lt = new LineType("TEST");
			lt.AddSegment(new LineType.Segment { Length = 1.0 });
			lt.AddSegment(new LineType.Segment { Length = -1.0 });

			var layer = new Layer("0") { Color = new Color((short)1) };
			var line = new Line
			{
				StartPoint = XYZ.Zero,
				EndPoint = new XYZ(10, 0, 0),
				Layer = layer,
				LineType = lt,
				LineTypeScale = 1.0,
			};

			var s1 = resolver.ResolveStroke(line, layout, geometricScaleToPaper: 1.0);
			var s2 = resolver.ResolveStroke(line, layout, geometricScaleToPaper: 2.0);

			Assert.NotEmpty(s1.DashArrayPt);
			Assert.NotEmpty(s2.DashArrayPt);
			Assert.True(Math.Abs((s2.DashArrayPt[0] / s1.DashArrayPt[0]) - 2.0) < 1e-9);
		}

		[Fact]
		public void SceneGraphPdf_EscapesTextString()
		{
			var pdf = new PdfDocument();
			var page = pdf.Pages.AddPage();

			page.Layout = new Layout("L")
			{
				PaperUnits = PlotPaperUnits.Millimeters,
				DenominatorScale = 1.0,
				PaperWidth = 100,
				PaperHeight = 100,
			};

			var layer = new Layer("0") { Color = new Color((short)1) };
			var text = new TextEntity
			{
				InsertPoint = new XYZ(10, 10, 0),
				Height = 2,
				Value = "a\\b(c)d\nx",
				Layer = layer,
				Color = Color.ByLayer,
			};

			page.Entities.Add(text);

			var config = new PdfConfiguration { UseSceneGraph = true };
			string content = page.Contents.GetPdfForm(config);

			Assert.Contains("(a\\\\b\\(c\\)d\\nx)", content);
			Assert.Contains(" rg", content);
		}
	}
}
