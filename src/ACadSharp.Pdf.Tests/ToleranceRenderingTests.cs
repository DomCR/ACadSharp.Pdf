using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf;
using ACadSharp.Pdf.Core.Render;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class ToleranceRenderingTests
	{
		[Fact]
		public void Tolerance_BasicFrame_RendersCompartmentsAndText()
		{
			var tolerance = createTolerance(@"{\Fgdt;j}%%v0.05%%vA%%vB");

			string content = renderEntity(tolerance, out RenderLog log);

			Assert.Contains("(0.05) Tj", content);
			Assert.Contains("(A) Tj", content);
			Assert.Contains("(B) Tj", content);
			Assert.DoesNotContain("(⌖) Tj", content);
			Assert.Contains(" c\n", content);
			Assert.DoesNotContain(log.Entries, entry => entry.Status == RenderStatus.Error || entry.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void Tolerance_DecodesDiameterAndMaterialConditionEscapes()
		{
			var tolerance = createTolerance(@"{\Fgdt;j}%%v%%c0.10%%cm%%vA%%cm");

			string content = renderEntity(tolerance, out RenderLog log);

			Assert.Contains("(0.10) Tj", content);
			Assert.Contains("(A) Tj", content);
			Assert.Contains("(M) Tj", content);
			Assert.Contains(" c\n", content);
			Assert.DoesNotContain(log.Entries, entry => entry.Status == RenderStatus.Error || entry.Status == RenderStatus.NotImplemented);
		}

		[Fact]
		public void Tolerance_EmptyTrailingCompartments_AreTrimmed()
		{
			var tolerance = createTolerance(@"{\Fgdt;e}%%v0.20%%v%%v");

			string content = renderEntity(tolerance, out _);

			Assert.Equal(1, countOccurrences(content, " Tj\n"));
		}

		[Fact]
		public void Tolerance_DirectionVector_RotatesTextBasis()
		{
			var tolerance = createTolerance(@"{\Fgdt;j}%%v0.05");
			tolerance.Direction = new XYZ(0.0, 1.0, 0.0);

			string content = renderEntity(tolerance, out _);
			List<TextMatrix> matrices = getTextMatrices(content);

			Assert.NotEmpty(matrices);
			TextMatrix first = matrices[0];
			AssertClose(0.0, first.A, 1e-6);
			AssertClose(1.0, first.B, 1e-6);
			AssertClose(-1.0, first.C, 1e-6);
			AssertClose(0.0, first.D, 1e-6);
		}

		[Fact]
		public void Tolerance_ZeroDirectionVector_DefaultsToXAxis()
		{
			var tolerance = createTolerance(@"{\Fgdt;j}%%v0.05");
			tolerance.Direction = XYZ.Zero;

			string content = renderEntity(tolerance, out _);
			List<TextMatrix> matrices = getTextMatrices(content);

			Assert.NotEmpty(matrices);
			TextMatrix first = matrices[0];
			AssertClose(1.0, first.A, 1e-6);
			AssertClose(0.0, first.B, 1e-6);
			AssertClose(0.0, first.C, 1e-6);
			AssertClose(1.0, first.D, 1e-6);
		}

		[Fact]
		public void Tolerance_TwoRowFrame_RendersBothRows()
		{
			var tolerance = createTolerance(@"{\Fgdt;j}%%v0.05%%vA\X{\Fgdt;j}%%v0.10%%vB");

			string content = renderEntity(tolerance, out _);

			Assert.Contains("(0.05) Tj", content);
			Assert.Contains("(0.10) Tj", content);
			Assert.Contains("(A) Tj", content);
			Assert.Contains("(B) Tj", content);
			Assert.Equal(4, countOccurrences(content, " Tj\n"));
		}

		private static Tolerance createTolerance(string text)
		{
			return new Tolerance
			{
				Text = text,
				InsertionPoint = XYZ.Zero,
				Direction = XYZ.AxisX,
				Normal = XYZ.AxisZ,
				Layer = new Layer("0"),
				Style = new DimensionStyle("TOL")
				{
					TextHeight = 2.5,
					DimensionLineGap = 0.625,
					DimensionLineColor = Color.ByLayer,
					DimensionLineWeight = LineWeightType.ByLayer,
					TextColor = Color.ByLayer,
					Style = TextStyle.Default,
				},
			};
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

		private static List<TextMatrix> getTextMatrices(string content)
		{
			var results = new List<TextMatrix>();
			var rx = new Regex(@"([\-0-9\.]+)\s+([\-0-9\.]+)\s+([\-0-9\.]+)\s+([\-0-9\.]+)\s+([\-0-9\.]+)\s+([\-0-9\.]+)\s+Tm");
			MatchCollection matches = rx.Matches(content ?? string.Empty);
			foreach (Match match in matches)
			{
				results.Add(new TextMatrix
				{
					A = parseDouble(match.Groups[1].Value),
					B = parseDouble(match.Groups[2].Value),
					C = parseDouble(match.Groups[3].Value),
					D = parseDouble(match.Groups[4].Value),
					E = parseDouble(match.Groups[5].Value),
					F = parseDouble(match.Groups[6].Value),
				});
			}

			return results;
		}

		private static double parseDouble(string value)
		{
			return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
		}

		private static void AssertClose(double expected, double actual, double tolerance)
		{
			Assert.True(
				Math.Abs(expected - actual) <= tolerance,
				$"Expected {expected.ToString("G17", CultureInfo.InvariantCulture)}, got {actual.ToString("G17", CultureInfo.InvariantCulture)}");
		}

		private sealed class TextMatrix
		{
			public double A { get; set; }
			public double B { get; set; }
			public double C { get; set; }
			public double D { get; set; }
			public double E { get; set; }
			public double F { get; set; }
		}
	}
}
