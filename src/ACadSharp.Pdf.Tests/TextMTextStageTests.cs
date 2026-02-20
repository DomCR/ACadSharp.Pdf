using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class TextMTextStageTests
	{
		[Fact]
		public void Text_DefaultAlignment_UsesInsertPoint()
		{
			var text = createText("ABCD", 10.0);
			text.InsertPoint = new XYZ(10, 20, 0);
			text.AlignmentPoint = new XYZ(100, 200, 0);
			text.HorizontalAlignment = TextHorizontalAlignment.Left;
			text.VerticalAlignment = TextVerticalAlignmentType.Baseline;

			string content = renderEntity(text);
			var tm = getTextMatrices(content);

			Assert.Single(tm);
			AssertClose(10.0, tm[0].E);
			AssertClose(20.0, tm[0].F);
		}

		[Fact]
		public void Text_NonDefaultAlignment_UsesAlignmentPointWithOffset()
		{
			var text = createText("ABCD", 10.0);
			text.InsertPoint = new XYZ(10, 20, 0);
			text.AlignmentPoint = new XYZ(100, 50, 0);
			text.HorizontalAlignment = TextHorizontalAlignment.Center;
			text.VerticalAlignment = TextVerticalAlignmentType.Top;

			string content = renderEntity(text);
			var tm = getTextMatrices(content);

			Assert.Single(tm);
			AssertClose(89.0, tm[0].E);
			AssertClose(40.0, tm[0].F);
		}

		[Fact]
		public void Text_VerticalAlignmentOnly_UsesAlignmentPoint()
		{
			var text = createText("ABCD", 10.0);
			text.InsertPoint = new XYZ(10, 20, 0);
			text.AlignmentPoint = new XYZ(100, 50, 0);
			text.HorizontalAlignment = TextHorizontalAlignment.Left;
			text.VerticalAlignment = TextVerticalAlignmentType.Top;

			string content = renderEntity(text);
			var tm = getTextMatrices(content);

			Assert.Single(tm);
			// Top shifts baseline down by ascent (10).
			AssertClose(100.0, tm[0].E);
			AssertClose(40.0, tm[0].F);
		}

		[Fact]
		public void Text_Fit_StretchesWidthBetweenAlignmentPoints()
		{
			var text = createText("ABCD", 10.0);
			text.InsertPoint = XYZ.Zero;
			text.AlignmentPoint = new XYZ(100, 0, 0);
			text.HorizontalAlignment = TextHorizontalAlignment.Fit;
			text.VerticalAlignment = TextVerticalAlignmentType.Baseline;

			string content = renderEntity(text);
			var tm = getTextMatrices(content);

			Assert.Single(tm);
			AssertClose(100.0 / 22.0, tm[0].A, 1e-3);
			AssertClose(1.0, tm[0].D, 1e-6);
		}

		[Fact]
		public void Text_Aligned_ScalesFontHeightToMatchSpan()
		{
			var text = createText("ABCD", 10.0);
			text.InsertPoint = XYZ.Zero;
			text.AlignmentPoint = new XYZ(100, 0, 0);
			text.HorizontalAlignment = TextHorizontalAlignment.Aligned;
			text.VerticalAlignment = TextVerticalAlignmentType.Baseline;

			string content = renderEntity(text);
			double size = getFontSize(content);
			var tm = getTextMatrices(content);

			AssertClose(100.0 / 22.0 * 10.0, size, 1e-3);
			AssertClose(1.0, tm[0].A, 1e-6);
			AssertClose(1.0, tm[0].D, 1e-6);
		}

		[Fact]
		public void Text_MirrorFlags_AppearInTextMatrix()
		{
			var text = createText("AB", 10.0);
			text.InsertPoint = new XYZ(10, 10, 0);
			text.Mirror = TextMirrorFlag.Backward | TextMirrorFlag.UpsideDown;

			string content = renderEntity(text);
			var tm = getTextMatrices(content);

			Assert.Single(tm);
			AssertClose(-1.0, tm[0].A, 1e-6);
			AssertClose(-1.0, tm[0].D, 1e-6);
			AssertClose(10.0, tm[0].E, 1e-6);
			AssertClose(10.0, tm[0].F, 1e-6);
		}

		[Fact]
		public void MText_ParagraphBreaks_EmitMultipleTextRuns()
		{
			var mtext = new MText("L1\\PL2\\PL3")
			{
				InsertPoint = new XYZ(50, 50, 0),
				Height = 10,
				AttachmentPoint = AttachmentPointType.TopLeft,
				RectangleWidth = 0,
			};

			string content = renderEntity(mtext);
			var tm = getTextMatrices(content);

			Assert.Equal(3, tm.Count);
			Assert.Contains("(L1) Tj", content);
			Assert.Contains("(L2) Tj", content);
			Assert.Contains("(L3) Tj", content);
			Assert.True(tm[1].F < tm[0].F);
			Assert.True(tm[2].F < tm[1].F);
		}

		[Fact]
		public void MText_Stacking_IsRenderedInline()
		{
			var mtext = new MText("\\S1/2;")
			{
				InsertPoint = new XYZ(0, 0, 0),
				Height = 10,
				AttachmentPoint = AttachmentPointType.TopLeft,
			};

			string content = renderEntity(mtext);
			Assert.Contains("(1/2) Tj", content);
		}

		[Fact]
		public void MText_LongWord_WrapsByCharacters()
		{
			var mtext = new MText("AAAAAAAAAAAAAA")
			{
				InsertPoint = new XYZ(0, 100, 0),
				Height = 10,
				AttachmentPoint = AttachmentPointType.TopLeft,
				RectangleWidth = 10,
			};

			string content = renderEntity(mtext);
			var tm = getTextMatrices(content);

			Assert.True(tm.Count > 1);
		}

		[Fact]
		public void MText_ConsecutiveParagraphBreaks_CreateBlankLineSpacing()
		{
			var mtext = new MText("L1\\P\\PL3")
			{
				InsertPoint = new XYZ(0, 100, 0),
				Height = 10,
				AttachmentPoint = AttachmentPointType.TopLeft,
				RectangleWidth = 0,
			};

			string content = renderEntity(mtext);
			var tm = getTextMatrices(content);

			Assert.Equal(2, tm.Count);
			// Two advances: (1.666 * 10) * 2
			double dy = tm[0].F - tm[1].F;
			Assert.True(dy > 30.0);
		}

		[Fact]
		public void MText_UnicodeEscape_IsRendered()
		{
			var mtext = new MText("A\\U+00B1B")
			{
				InsertPoint = new XYZ(0, 0, 0),
				Height = 10,
				AttachmentPoint = AttachmentPointType.TopLeft,
			};

			string content = renderEntity(mtext);
			Assert.Contains("(A\u00B1B) Tj", content);
		}

		[Fact]
		public void MText_RectangleWidth_WrapsWords()
		{
			var mtext = new MText("AAAA BBBB CCCC")
			{
				InsertPoint = new XYZ(0, 100, 0),
				Height = 10,
				AttachmentPoint = AttachmentPointType.TopLeft,
				RectangleWidth = 30,
			};

			string content = renderEntity(mtext);
			var tm = getTextMatrices(content);

			Assert.True(tm.Count >= 3);
			Assert.Contains("AAAA", content);
			Assert.Contains("BBBB", content);
			Assert.Contains("CCCC", content);
		}

		[Fact]
		public void Insert_MTextAttribute_RendersMultilineContent()
		{
			var block = new BlockRecord("B-MTEXT-ATT");
			block.Entities.Add(new AttributeDefinition
			{
				Tag = "NOTE",
				Value = string.Empty,
				InsertPoint = XYZ.Zero,
				Height = 5.0,
			});

			var insert = new Insert(block)
			{
				InsertPoint = new XYZ(10, 10, 0),
			};

			var att = insert.Attributes[0];
			att.Value = string.Empty;
			att.MText = new MText("ROW1\\PROW2")
			{
				Height = 5.0,
				InsertPoint = XYZ.Zero,
			};

			string content = renderEntity(insert);

			Assert.Contains("(ROW1) Tj", content);
			Assert.Contains("(ROW2) Tj", content);
		}

		private static TextEntity createText(string value, double height)
		{
			return new TextEntity
			{
				Value = value,
				Height = height,
				Layer = new ACadSharp.Tables.Layer("0") { Color = Color.Red },
				Color = Color.ByLayer,
			};
		}

		private static string renderEntity(Entity entity)
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

			return page.Contents.GetPdfForm(cfg);
		}

		private static List<TextMatrix> getTextMatrices(string content)
		{
			var result = new List<TextMatrix>();
			var matches = Regex.Matches(content, @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Tm");
			foreach (Match match in matches)
			{
				result.Add(new TextMatrix
				{
					A = parse(match.Groups[1].Value),
					B = parse(match.Groups[2].Value),
					C = parse(match.Groups[3].Value),
					D = parse(match.Groups[4].Value),
					E = parse(match.Groups[5].Value),
					F = parse(match.Groups[6].Value),
				});
			}

			return result;
		}

		private static double getFontSize(string content)
		{
			Match match = Regex.Match(content, @"/F1 (-?\d+(?:\.\d+)?) Tf");
			Assert.True(match.Success, "No font-size command found in PDF content.");
			return parse(match.Groups[1].Value);
		}

		private static double parse(string value)
		{
			return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
		}

		private static void AssertClose(double expected, double actual, double epsilon = 1e-4)
		{
			Assert.True(Math.Abs(expected - actual) <= epsilon, $"Expected {expected} but got {actual}");
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
