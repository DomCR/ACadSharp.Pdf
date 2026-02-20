using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf;
using ACadSharp.Pdf.Core;
using ACadSharp.Pdf.Core.Render;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Globalization;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class RayXLineRenderingTests
	{
		[Fact]
		public void XLine_ClipsAcrossLayoutBounds()
		{
			var xline = new XLine
			{
				FirstPoint = new XYZ(50.0, 50.0, 0.0),
				Direction = new XYZ(1.0, 0.0, 0.0),
			};

			string content = renderEntity(xline, out RenderLog log);

			Assert.True(tryGetFirstSegment(content, out XY start, out XY end));
			assertClose(start, -2.0, 50.0);
			assertClose(end, 102.0, 50.0);
			Assert.Contains(log.Entries, e => e.Status == RenderStatus.Rendered);
		}

		[Fact]
		public void Ray_StartsAtBasePointInsideClip()
		{
			var ray = new Ray
			{
				StartPoint = new XYZ(50.0, 50.0, 0.0),
				Direction = new XYZ(1.0, 0.0, 0.0),
			};

			string content = renderEntity(ray, out RenderLog log);

			Assert.True(tryGetFirstSegment(content, out XY start, out XY end));
			assertClose(start, 50.0, 50.0);
			assertClose(end, 102.0, 50.0);
			Assert.Contains(log.Entries, e => e.Status == RenderStatus.Rendered);
		}

		[Fact]
		public void Ray_OutsideAndPointingAway_IsSkipped()
		{
			var ray = new Ray
			{
				StartPoint = new XYZ(-50.0, 50.0, 0.0),
				Direction = new XYZ(-1.0, 0.0, 0.0),
			};

			string content = renderEntity(ray, out RenderLog log);

			Assert.False(tryGetFirstSegment(content, out _, out _));
			Assert.Contains(log.Entries, e => e.Status == RenderStatus.Skipped && e.Reason.Contains("outside clip rectangle", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public void XLine_WithZeroDirection_IsSkipped()
		{
			var xline = new XLine
			{
				FirstPoint = new XYZ(50.0, 50.0, 0.0),
				Direction = XYZ.Zero,
			};

			string content = renderEntity(xline, out RenderLog log);

			Assert.False(tryGetFirstSegment(content, out _, out _));
			Assert.Contains(log.Entries, e => e.Status == RenderStatus.Skipped && e.Reason.Contains("zero direction", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public void Ray_InInsert_UsesScaledDirection()
		{
			var block = new BlockRecord("RAY_BLOCK");
			block.Entities.Add(new Ray
			{
				StartPoint = XYZ.Zero,
				Direction = new XYZ(1.0, 1.0, 0.0),
			});

			var insert = new Insert(block)
			{
				XScale = 2.0,
				YScale = 1.0,
				ZScale = 1.0,
			};

			string content = renderEntity(insert, out RenderLog log);

			Assert.True(tryGetFirstSegment(content, out XY start, out XY end));
			assertClose(start, 0.0, 0.0);
			assertClose(end, 102.0, 51.0);
			Assert.Contains(log.Entries, e => e.Status == RenderStatus.Rendered);
		}

		private static string renderEntity(Entity entity, out RenderLog log)
		{
			var pdf = new PdfDocument();
			PdfPage page = pdf.Pages.AddPage();
			page.Layout = new Layout("L")
			{
				PaperUnits = PlotPaperUnits.Pixels,
				DenominatorScale = 1.0,
				PaperWidth = 100.0,
				PaperHeight = 100.0,
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

		private static bool tryGetFirstSegment(string content, out XY start, out XY end)
		{
			start = XY.Zero;
			end = XY.Zero;
			bool hasStart = false;

			string[] lines = content.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (line.EndsWith(" m", StringComparison.Ordinal))
				{
					if (!hasStart && tryParsePathPoint(line, out XY point))
					{
						start = point;
						hasStart = true;
					}
					continue;
				}

				if (hasStart && line.EndsWith(" l", StringComparison.Ordinal) && tryParsePathPoint(line, out XY pointEnd))
				{
					end = pointEnd;
					return true;
				}
			}

			return false;
		}

		private static bool tryParsePathPoint(string line, out XY point)
		{
			point = XY.Zero;
			string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 3)
			{
				return false;
			}

			if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
				|| !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
			{
				return false;
			}

			point = new XY(x, y);
			return true;
		}

		private static void assertClose(XY point, double x, double y)
		{
			Assert.True(Math.Abs(point.X - x) <= 1e-6, $"Expected X={x}, got {point.X}");
			Assert.True(Math.Abs(point.Y - y) <= 1e-6, $"Expected Y={y}, got {point.Y}");
		}
	}
}
