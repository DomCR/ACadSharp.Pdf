using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf;
using ACadSharp.Pdf.Core.Render;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class DimensionRenderingTests
	{
		[Fact]
		public void LinearDimension_TextSuppressed_DoesNotEmitText()
		{
			var dim = new DimensionLinear
			{
				FirstPoint = new XYZ(0, 0, 0),
				SecondPoint = new XYZ(100, 0, 0),
				DefinitionPoint = new XYZ(100, 20, 0),
				Text = " ",
			};

			string content = renderEntity(dim, out _);

			Assert.DoesNotContain(" Tj", content);
		}

		[Fact]
		public void LinearDimension_FallbackRendersGeometryAndText()
		{
			var doc = new CadDocument();
			var dim = new DimensionLinear
			{
				FirstPoint = new XYZ(0, 0, 0),
				SecondPoint = new XYZ(100, 0, 0),
				DefinitionPoint = new XYZ(100, 20, 0),
				Text = "DIM-LIN",
			};
			doc.Entities.Add(dim);

			string content = renderEntity(dim, out RenderLog log);

			Assert.Contains("(DIM-LIN) Tj", content);
			Assert.Contains("0 20 m", content);
			Assert.Contains("100 20 l", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void LinearDimension_NegativeGap_DrawsTextBox()
		{
			var dim = new DimensionLinear
			{
				FirstPoint = new XYZ(0, 0, 0),
				SecondPoint = new XYZ(100, 0, 0),
				DefinitionPoint = new XYZ(100, 20, 0),
				Text = "BOX",
			};
			dim.Style.DimensionLineGap = -2.0;
			dim.Style.TextVerticalAlignment = DimensionTextVerticalAlignment.Centered;
			dim.Style.TextHeight = 10.0;

			string content = renderEntity(dim, out _);

			// The text box is a stroked closed path => "h" then "S".
			Assert.Contains("h\nS\n", content);
		}

		[Fact]
		public void LinearDimension_TextOutside_AddsLeaderWhenConfigured()
		{
			var dim = new DimensionLinear
			{
				FirstPoint = new XYZ(0, 0, 0),
				SecondPoint = new XYZ(5, 0, 0),
				DefinitionPoint = new XYZ(5, 2, 0),
				Text = "LEADERTEST",
			};
			dim.Style.TextHeight = 10.0;
			dim.Style.DimensionLineGap = 1.0;
			dim.Style.ArrowSize = 2.0;
			dim.Style.TextMovement = TextMovement.AddLeaderWhenTextMoved;

			string content = renderEntity(dim, out _);

			// Leader line from mid (2.5,2) to text (35.5,8) with the chosen parameters.
			Assert.Contains("2.5 2 m", content);
			Assert.Contains("35.5 8 l", content);
		}

		[Fact]
		public void LinearDimension_TickSize_RendersTicksInsteadOfFilledArrows()
		{
			var dim = new DimensionLinear
			{
				FirstPoint = new XYZ(0, 0, 0),
				SecondPoint = new XYZ(100, 0, 0),
				DefinitionPoint = new XYZ(100, 20, 0),
				Text = "TICK",
			};
			dim.Style.TickSize = 5.0;

			string content = renderEntity(dim, out _);

			// Filled arrowheads are emitted as fill-only paths ("F"). Tick marks are stroke-only.
			Assert.DoesNotContain("\nF\n", content);
		}

		[Fact]
		public void LinearDimension_ZeroLength_DoesNotThrow()
		{
			var dim = new DimensionLinear
			{
				FirstPoint = new XYZ(10, 10, 0),
				SecondPoint = new XYZ(10, 10, 0),
				DefinitionPoint = new XYZ(10, 20, 0),
				Text = "ZERO",
			};

			string content = renderEntity(dim, out RenderLog log);

			Assert.Contains("(ZERO) Tj", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error);
		}

		[Fact]
		public void Angular2Line_ParallelLines_DoesNotThrow()
		{
			var dim = new DimensionAngular2Line
			{
				FirstPoint = new XYZ(0, 0, 0),
				SecondPoint = new XYZ(10, 0, 0),
				AngleVertex = new XYZ(0, 5, 0),
				DefinitionPoint = new XYZ(10, 5, 0),
				DimensionArc = new XYZ(5, 10, 0),
				Text = "PARALLEL",
			};

			string content = renderEntity(dim, out RenderLog log);

			Assert.Contains("(PARALLEL) Tj", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.Error);
		}

		[Fact]
		public void Angular3PointDimension_FallbackRendersText()
		{
			var doc = new CadDocument();
			var dim = new DimensionAngular3Pt
			{
				AngleVertex = XYZ.Zero,
				FirstPoint = new XYZ(20, 0, 0),
				SecondPoint = new XYZ(0, 20, 0),
				DefinitionPoint = new XYZ(14, 14, 0),
				Text = "DIM-ANG3",
			};
			doc.Entities.Add(dim);

			string content = renderEntity(dim, out RenderLog log);

			Assert.Contains("(DIM-ANG3) Tj", content);
			Assert.Contains("14", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void RadiusDimension_FallbackRendersRadialLineAndText()
		{
			var doc = new CadDocument();
			var dim = new DimensionRadius
			{
				DefinitionPoint = new XYZ(10, 10, 0),
				AngleVertex = new XYZ(30, 10, 0),
				Text = "DIM-RAD",
			};
			doc.Entities.Add(dim);

			string content = renderEntity(dim, out RenderLog log);

			Assert.Contains("(DIM-RAD) Tj", content);
			Assert.Contains("10 10 m", content);
			Assert.Contains("30 10 l", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void DiameterDimension_FallbackRendersDiameterLineAndText()
		{
			var doc = new CadDocument();
			var dim = new DimensionDiameter
			{
				AngleVertex = new XYZ(0, 0, 0),
				DefinitionPoint = new XYZ(40, 0, 0),
				Text = "DIM-DIA",
			};
			doc.Entities.Add(dim);

			string content = renderEntity(dim, out RenderLog log);

			Assert.Contains("(DIM-DIA) Tj", content);
			Assert.Contains("0 0 m", content);
			Assert.Contains("40 0 l", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void OrdinateDimension_FallbackRendersLeaderAndText()
		{
			var doc = new CadDocument();
			var dim = new DimensionOrdinate
			{
				FeatureLocation = new XYZ(10, 10, 0),
				LeaderEndpoint = new XYZ(40, 35, 0),
				DefinitionPoint = new XYZ(40, 35, 0),
				Text = "DIM-ORD",
			};
			doc.Entities.Add(dim);

			string content = renderEntity(dim, out RenderLog log);

			Assert.Contains("(DIM-ORD) Tj", content);
			Assert.Contains("40 35 l", content);
			Assert.DoesNotContain(log.Entries, e => e.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void Dimension_UsesAnonymousBlockBeforeFallback()
		{
			var doc = new CadDocument();
			var block = new BlockRecord("*D1");
			block.Entities.Add(new Line
			{
				StartPoint = new XYZ(7, 8, 0),
				EndPoint = new XYZ(9, 8, 0),
			});
			doc.BlockRecords.Add(block);

			var dim = new DimensionLinear
			{
				FirstPoint = new XYZ(0, 0, 0),
				SecondPoint = new XYZ(100, 0, 0),
				DefinitionPoint = new XYZ(100, 20, 0),
				Text = "SHOULD-NOT-RENDER",
				Block = block,
			};
			doc.Entities.Add(dim);

			string content = renderEntity(dim, out RenderLog _);

			Assert.Contains("7 8 m", content);
			Assert.Contains("9 8 l", content);
			Assert.DoesNotContain("(SHOULD-NOT-RENDER) Tj", content);
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
				DecimalFormat = "0.####",
			};

			string content = page.Contents.GetPdfForm(cfg);
			log = cfg.LastRenderLog;
			return content;
		}
	}
}
