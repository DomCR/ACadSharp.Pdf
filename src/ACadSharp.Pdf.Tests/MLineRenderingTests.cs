using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf;
using ACadSharp.Pdf.Core.Render;
using ACadSharp.Tables;
using CSMath;
using System;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class MLineRenderingTests
	{
		[Fact]
		public void MLine_JustificationTop_ShiftsOffsetsFromStyle()
		{
			var mline = createBaseMLine(MLineJustification.Top, closed: false);

			string content = renderEntity(mline, out RenderLog log);

			// Top justification with default (+0.5, -0.5) style => effective offsets (0, -1).
			Assert.Contains("0 0 m", content);
			Assert.Contains("10 0 l", content);
			Assert.Contains("10 10 l", content);
			Assert.Contains("0 -1 m", content);
			Assert.Contains("11 -1 l", content);
			Assert.Contains("11 10 l", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error || e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void MLine_JustificationBottom_ShiftsOffsetsFromStyle()
		{
			var mline = createBaseMLine(MLineJustification.Bottom, closed: false);

			string content = renderEntity(mline, out RenderLog log);

			// Bottom justification with default (+0.5, -0.5) style => effective offsets (+1, 0).
			Assert.Contains("0 1 m", content);
			Assert.Contains("9 1 l", content);
			Assert.Contains("9 10 l", content);
			Assert.Contains("0 0 m", content);
			Assert.Contains("10 0 l", content);
			Assert.Contains("10 10 l", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error || e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void MLine_Closed_RendersClosedElementPaths()
		{
			var mline = createBaseMLine(MLineJustification.Top, closed: true);

			string content = renderEntity(mline, out RenderLog log);
			int closedStrokeCount = countOccurrences(content, "h\nS\n");

			Assert.True(closedStrokeCount >= 2, $"Expected at least 2 closed stroked paths, got {closedStrokeCount}.");
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error || e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void MLine_FillAndCaps_Rendered_WhenEnabledInStyle()
		{
			var style = new MLineStyle("CAPS_FILL");
			style.Flags =
				MLineStyleFlags.FillOn |
				MLineStyleFlags.StartSquareCap |
				MLineStyleFlags.EndSquareCap |
				MLineStyleFlags.StartRoundCap |
				MLineStyleFlags.EndRoundCap;
			style.FillColor = Color.Red;
			style.AddElement(new MLineStyle.Element { Offset = 1.0, Color = Color.Green, LineType = LineType.ByLayer });
			style.AddElement(new MLineStyle.Element { Offset = -1.0, Color = Color.Blue, LineType = LineType.ByLayer });

			var mline = new MLine
			{
				Layer = new Layer("0"),
				Color = Color.ByLayer,
				Style = style,
				Justification = MLineJustification.Zero,
				Flags = MLineFlags.Has,
			};
			mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(0, 0, 0) });
			mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(20, 0, 0) });

			string content = renderEntity(mline, out RenderLog log);
			int strokeCount = countOccurrences(content, "\nS\n");

			Assert.Contains("\nF\n", content);
			Assert.Contains("0 -1 m", content);
			Assert.Contains("0 1 l", content);
			Assert.Contains("20 -1 m", content);
			Assert.Contains("20 1 l", content);
			Assert.True(strokeCount >= 4, $"Expected multiple stroked paths (elements + caps), got {strokeCount}.");
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error || e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void MLine_VertexParams_AreUsed_ForGeometry()
		{
			var style = MLineStyle.Default;

			// Equivalent to the stage07_mline.dxf first entity: Top justification => offsets become (0, -1).
			var mline = new MLine
			{
				Layer = new Layer("0"),
				Color = Color.ByLayer,
				Style = style,
				Justification = MLineJustification.Top,
				Flags = MLineFlags.Has,
			};

			mline.Vertices.Add(makeVertex(new XYZ(0, 0, 0), new XYZ(0, 1, 0), 0.0, -1.0));
			mline.Vertices.Add(makeVertex(new XYZ(50, 0, 0), new XYZ(-0.7071067811865475, 0.7071067811865475, 0), 0.0, -1.4142135623730951));
			mline.Vertices.Add(makeVertex(new XYZ(50, 50, 0), new XYZ(-1, 0, 0), 0.0, -1.0));

			string content = renderEntity(mline, out RenderLog log);

			Assert.Contains("51 -1 l", content);
			Assert.Contains("51 50 l", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error || e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void MLine_VertexParams_DisplayJoints_AddsMiterLinesAtInteriorVertex()
		{
			var style = MLineStyle.Default;
			style.Flags = MLineStyleFlags.DisplayJoints;

			var mline = new MLine
			{
				Layer = new Layer("0"),
				Color = Color.ByLayer,
				Style = style,
				Justification = MLineJustification.Top,
				Flags = MLineFlags.Has,
			};

			mline.Vertices.Add(makeVertex(new XYZ(0, 0, 0), new XYZ(0, 1, 0), 0.0, -1.0));
			mline.Vertices.Add(makeVertex(new XYZ(50, 0, 0), new XYZ(-0.7071067811865475, 0.7071067811865475, 0), 0.0, -1.4142135623730951));
			mline.Vertices.Add(makeVertex(new XYZ(50, 50, 0), new XYZ(-1, 0, 0), 0.0, -1.0));

			string content = renderEntity(mline, out RenderLog log);

			// Joint midpoint between (50,0) and (51,-1) is (50.5,-0.5).
			Assert.Contains("50.5 -0.5 l", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error || e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void MLine_VertexParams_Mismatch_FallsBackToOffsetAlgorithm()
		{
			var mline = createBaseMLine(MLineJustification.Top, closed: false);

			// Provide invalid vertex parametrization (segment count mismatch) to force fallback.
			foreach (var v in mline.Vertices)
			{
				v.Miter = XYZ.AxisY;
				v.Segments.Add(new MLine.Vertex.Segment { Parameters = new System.Collections.Generic.List<double> { 0.0, 0.0 } });
			}

			string content = renderEntity(mline, out RenderLog log);

			Assert.Contains("11 -1 l", content);
			Assert.Contains(log.Entries, e => e.Status == RenderStatus.Rendered && e.Reason.Contains("offset approximation", StringComparison.OrdinalIgnoreCase));
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error || e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void MLine_NoStartCaps_SuppressesRoundedFillAtStart()
		{
			var style = new MLineStyle("CAPS_FILL");
			style.Flags = MLineStyleFlags.FillOn | MLineStyleFlags.StartRoundCap | MLineStyleFlags.EndRoundCap;
			style.FillColor = Color.Red;
			style.AddElement(new MLineStyle.Element { Offset = 1.0, Color = Color.ByLayer, LineType = LineType.ByLayer });
			style.AddElement(new MLineStyle.Element { Offset = -1.0, Color = Color.ByLayer, LineType = LineType.ByLayer });

			var mline = new MLine
			{
				Layer = new Layer("0"),
				Color = Color.ByLayer,
				Style = style,
				Justification = MLineJustification.Zero,
				Flags = MLineFlags.Has | MLineFlags.NoStartCaps,
			};
			mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(0, 0, 0) });
			mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(20, 0, 0) });

			string content = renderEntity(mline, out RenderLog log);

			Assert.Contains("\nF\n", content);
			// Without a rounded start cap, the fill boundary shouldn't extend to x<0 for this simple segment.
			// The semicircle from (0,1) to (0,-1) would include (-1,0); ensure it's absent.
			Assert.DoesNotContain("\n-1 0 l", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error || e.Status == RenderStatus.NotImplemented);
		}

		private static MLine.Vertex makeVertex(XYZ position, XYZ miterDirection, double topLength, double bottomLength)
		{
			var v = new MLine.Vertex
			{
				Position = position,
				Miter = miterDirection,
			};

			v.Segments.Add(new MLine.Vertex.Segment { Parameters = new System.Collections.Generic.List<double> { topLength, 0.0 } });
			v.Segments.Add(new MLine.Vertex.Segment { Parameters = new System.Collections.Generic.List<double> { bottomLength, 0.0 } });
			return v;
		}

		private static MLine createBaseMLine(MLineJustification justification, bool closed)
		{
			var mline = new MLine
			{
				Color = Color.ByLayer,
				Justification = justification,
				Flags = closed ? MLineFlags.Has | MLineFlags.Closed : MLineFlags.Has,
				Style = MLineStyle.Default,
			};

			mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(0, 0, 0) });
			mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(10, 0, 0) });
			mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(10, 10, 0) });
			if (closed)
			{
				mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(0, 10, 0) });
			}

			return mline;
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
				DecimalFormat = "0.###",
			};

			string content = page.Contents.GetPdfForm(cfg);
			log = cfg.LastRenderLog;
			return content;
		}

		private static int countOccurrences(string input, string value)
		{
			if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(value))
			{
				return 0;
			}

			int count = 0;
			int index = 0;
			while ((index = input.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
			{
				count++;
				index += value.Length;
			}

			return count;
		}
	}
}
