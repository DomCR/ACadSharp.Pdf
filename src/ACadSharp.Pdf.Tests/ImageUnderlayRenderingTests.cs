using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf;
using ACadSharp.Pdf.Core;
using ACadSharp.Pdf.Core.Render;
using ACadSharp.Tables;
using CSMath;
using System;
using System.IO;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class ImageUnderlayRenderingTests
	{
		[Fact]
		public void RasterImage_EmbedsInlineImage()
		{
			string imagePath = getFixtureImagePath();

			var imageDef = new ImageDefinition { FileName = imagePath };
			var image = new RasterImage(imageDef)
			{
				InsertPoint = XYZ.Zero,
				UVector = new XYZ(20, 0, 0),
				VVector = new XYZ(0, 20, 0),
				Size = new XY(1, 1),
				Flags = ImageDisplayFlags.ShowImage,
				Layer = new Layer("0"),
			};

			string content = renderEntity(image, out RenderLog log);

			Assert.Contains("BI", content);
			Assert.Contains("/W 1", content);
			Assert.Contains("/H 1", content);
			Assert.Contains("/ASCIIHexDecode", content);
			Assert.Contains(log.Entries, e => e.Status == RenderStatus.Rendered && e.EntityType == image.SubclassMarker);
		}

		[Fact]
		public void RasterImage_WithClipBoundary_EmitsClipOperator()
		{
			string imagePath = getFixtureImagePath();

			var imageDef = new ImageDefinition { FileName = imagePath };
			var image = new RasterImage(imageDef)
			{
				InsertPoint = XYZ.Zero,
				UVector = new XYZ(20, 0, 0),
				VVector = new XYZ(0, 20, 0),
				Size = new XY(1, 1),
				Flags = ImageDisplayFlags.ShowImage | ImageDisplayFlags.UseClippingBoundary,
				ClippingState = true,
				ClipMode = ClipMode.Inside,
				ClipType = ClipType.Rectangular,
				Layer = new Layer("0"),
			};
			image.ClipBoundaryVertices.Add(new XY(-0.5, -0.5));
			image.ClipBoundaryVertices.Add(new XY(0.5, 0.5));

			string content = renderEntity(image, out _);

			Assert.Contains("W n", content);
		}

		[Fact]
		public void RasterImage_RelativePath_UsesConfiguredBasePath()
		{
			string imagePath = getFixtureImagePath();
			string basePath = Path.GetDirectoryName(imagePath);

			var imageDef = new ImageDefinition { FileName = Path.GetFileName(imagePath) };
			var image = new RasterImage(imageDef)
			{
				InsertPoint = XYZ.Zero,
				UVector = new XYZ(20, 0, 0),
				VVector = new XYZ(0, 20, 0),
				Size = new XY(1, 1),
				Flags = ImageDisplayFlags.ShowImage,
				Layer = new Layer("0"),
			};

			var cfg = createConfig();
			cfg.BasePath = basePath;
			string content = renderEntity(image, cfg, out RenderLog log);

			Assert.Contains("BI", content);
			Assert.Contains(log.Entries, e => e.Status == RenderStatus.Rendered && e.EntityType == image.SubclassMarker);
		}

		[Fact]
		public void RasterImage_InInsert_DoesNotTranslateUVectors()
		{
			string imagePath = getFixtureImagePath();

			var imageDef = new ImageDefinition { FileName = imagePath };
			var block = new BlockRecord("BIMG");
			block.Entities.Add(new RasterImage(imageDef)
			{
				InsertPoint = XYZ.Zero,
				UVector = new XYZ(1, 0, 0),
				VVector = new XYZ(0, 1, 0),
				Size = new XY(1, 1),
				Flags = ImageDisplayFlags.ShowImage,
				Layer = new Layer("0"),
			});

			var insert = new Insert(block)
			{
				InsertPoint = new XYZ(10, 20, 0),
			};

			string content = renderEntity(insert, out _);

			// The inline image placement matrix should be a pure translation for this case.
			Assert.Contains("1 0 0 1 10 20 cm", content);
		}

		[Fact]
		public void PdfUnderlay_RasterizesAndEmbedsInlineImage()
		{
			string pdfPath = createTempUnderlayPdf();
			try
			{
				var definition = new PdfUnderlayDefinition { File = pdfPath, Page = "1" };
				var underlay = new PdfUnderlay(definition)
				{
					InsertPoint = new XYZ(100, 20, 0),
					XScale = 120,
					YScale = 60,
					Rotation = 0,
					Normal = XYZ.AxisZ,
					Flags = UnderlayDisplayFlags.ShowUnderlay,
					Layer = new Layer("0"),
				};

				string content = renderEntity(underlay, out RenderLog log);

				Assert.Contains("BI", content);
				Assert.Contains("/ASCIIHexDecode", content);
				Assert.Contains(log.Entries, e => e.Status == RenderStatus.Rendered && e.EntityType == underlay.SubclassMarker);
			}
			finally
			{
				File.Delete(pdfPath);
			}
		}

		[Fact]
		public void MissingExternalReference_ProducesSkippedOrErrorBasedOnConfiguration()
		{
			var imageDef = new ImageDefinition { FileName = "does_not_exist.png" };
			var image = new RasterImage(imageDef)
			{
				InsertPoint = XYZ.Zero,
				UVector = new XYZ(20, 0, 0),
				VVector = new XYZ(0, 20, 0),
				Size = new XY(1, 1),
				Flags = ImageDisplayFlags.ShowImage,
				Layer = new Layer("0"),
			};

			var skipCfg = createConfig();
			skipCfg.SkipMissingImages = true;
			renderEntity(image, skipCfg, out RenderLog skipLog);
			Assert.Contains(skipLog.Entries, e => e.Status == RenderStatus.Skipped && e.Reason.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0);

			var failCfg = createConfig();
			failCfg.SkipMissingImages = false;
			renderEntity(image, failCfg, out RenderLog failLog);
			Assert.Contains(failLog.Entries, e => e.Status == RenderStatus.Error && e.Reason.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		private static string renderEntity(Entity entity, out RenderLog log)
		{
			return renderEntity(entity, createConfig(), out log);
		}

		private static string renderEntity(Entity entity, PdfConfiguration configuration, out RenderLog log)
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

			string content = page.Contents.GetPdfForm(configuration);
			log = configuration.LastRenderLog;
			return content;
		}

		private static PdfConfiguration createConfig()
		{
			return new PdfConfiguration
			{
				UseSceneGraph = true,
				DecimalFormat = "0.####",
			};
		}

		private static string getFixtureImagePath()
		{
			string imagePath = Path.GetFullPath(Path.Combine(TestVariables.SamplesFolder, "fixtures", "test_image.png"));
			Assert.True(File.Exists(imagePath), $"Fixture image not found: {imagePath}");
			return imagePath;
		}

		private static string createTempUnderlayPdf()
		{
			string tempPath = Path.Combine(Path.GetTempPath(), $"acadsharp_underlay_{Guid.NewGuid():N}.pdf");
			File.WriteAllText(tempPath,
@"%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Contents 4 0 R /Resources << >> >>
endobj
4 0 obj
<< /Length 35 >>
stream
0 0 0 rg
10 10 180 80 re
f
endstream
endobj
xref
0 5
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
0000000219 00000 n 
trailer
<< /Size 5 /Root 1 0 R >>
startxref
303
%%EOF");

			return tempPath;
		}
	}
}
