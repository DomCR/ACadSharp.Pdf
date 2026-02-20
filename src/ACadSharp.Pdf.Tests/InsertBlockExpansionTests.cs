using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf;
using ACadSharp.Pdf.Core;
using ACadSharp.Pdf.Core.Render;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class InsertBlockExpansionTests
	{
		[Fact]
		public void Insert_AppliesBasePointAndScale()
		{
			var doc = new CadDocument();
			var block = new BlockRecord("B1");
			block.BlockEntity.BasePoint = new XYZ(5, 5, 0);
			block.Entities.Add(new Line
			{
				StartPoint = new XYZ(5, 5, 0),
				EndPoint = new XYZ(6, 5, 0),
			});

			var insert = new Insert(block)
			{
				InsertPoint = new XYZ(100, 50, 0),
				XScale = 2.0,
				YScale = 2.0,
				ZScale = 1.0,
			};

			doc.Entities.Add(insert);

			string content = renderEntity(insert, out _);

			Assert.Contains("100 50 m", content);
			Assert.Contains("102 50 l", content);
		}

		[Fact]
		public void MInsert_ExpandsGridCells()
		{
			var doc = new CadDocument();
			var block = new BlockRecord("BGRID");
			block.Entities.Add(new Line
			{
				StartPoint = new XYZ(0, 0, 0),
				EndPoint = new XYZ(1, 0, 0),
			});

			var insert = new Insert(block)
			{
				InsertPoint = XYZ.Zero,
				ColumnCount = 3,
				RowCount = 2,
				ColumnSpacing = 10.0,
				RowSpacing = 20.0,
			};

			doc.Entities.Add(insert);

			string content = renderEntity(insert, out _);

			Assert.Contains("0 0 m", content);
			Assert.Contains("10 0 m", content);
			Assert.Contains("20 0 m", content);
			Assert.Contains("0 20 m", content);
			Assert.Contains("10 20 m", content);
			Assert.Contains("20 20 m", content);
		}

		[Fact]
		public void MInsert_ScalesGridSpacingWithInsertScale()
		{
			var doc = new CadDocument();
			var block = new BlockRecord("BGRID_SCALE");
			block.Entities.Add(new Line
			{
				StartPoint = new XYZ(0, 0, 0),
				EndPoint = new XYZ(1, 0, 0),
			});

			var insert = new Insert(block)
			{
				InsertPoint = XYZ.Zero,
				ColumnCount = 2,
				RowCount = 1,
				ColumnSpacing = 10.0,
				RowSpacing = 0.0,
				XScale = 2.0,
				YScale = 2.0,
				ZScale = 1.0,
			};

			doc.Entities.Add(insert);

			string content = renderEntity(insert, out _);

			Assert.Contains("0 0 m", content);
			Assert.Contains("20 0 m", content);
			Assert.Contains("22 0 l", content);
		}

		[Fact]
		public void Insert_RendersAttribAndSkipsAttDefTemplate()
		{
			var doc = new CadDocument();
			var block = new BlockRecord("BATT");
			block.Entities.Add(new AttributeDefinition
			{
				Tag = "LABEL",
				Value = "DEFAULT",
				InsertPoint = new XYZ(0, 0, 0),
				Height = 2.0,
			});

			var insert = new Insert(block)
			{
				InsertPoint = new XYZ(10, 10, 0),
			};
			insert.Attributes[0].Value = "VISIBLE";

			doc.Entities.Add(insert);

			string content = renderEntity(insert, out _);

			Assert.Contains("(VISIBLE) Tj", content);
			Assert.DoesNotContain("(DEFAULT) Tj", content);
		}

		[Fact]
		public void Insert_ByBlockColorInheritsFromInserter()
		{
			var doc = new CadDocument();
			var block = new BlockRecord("BCOLOR");
			block.Entities.Add(new Line
			{
				StartPoint = new XYZ(0, 0, 0),
				EndPoint = new XYZ(1, 0, 0),
				Color = Color.ByBlock,
			});

			var insert = new Insert(block)
			{
				InsertPoint = XYZ.Zero,
				Color = Color.Red,
			};

			doc.Entities.Add(insert);

			string content = renderEntity(insert, out _);

			Assert.Contains("1 0 0 RG", content);
		}

		[Fact]
		public void Insert_LayerZeroInheritsInserterLayer()
		{
			var doc = new CadDocument();
			var dimLayer = new Layer("DIM")
			{
				Color = Color.Blue,
			};
			doc.Layers.Add(dimLayer);

			var block = new BlockRecord("BLAYER0");
			block.Entities.Add(new Line
			{
				StartPoint = new XYZ(0, 0, 0),
				EndPoint = new XYZ(1, 0, 0),
				Color = Color.ByLayer,
				Layer = doc.Layers["0"],
			});

			var insert = new Insert(block)
			{
				InsertPoint = XYZ.Zero,
				Layer = dimLayer,
			};

			doc.Entities.Add(insert);

			string content = renderEntity(insert, out _);

			Assert.Contains("0 0 1 RG", content);
		}

		[Fact]
		public void Insert_CycleDetectionStopsRecursiveExpansion()
		{
			var doc = new CadDocument();
			var block = new BlockRecord("BCYCLE");
			var nested = new Insert(block);
			block.Entities.Add(nested);

			var top = new Insert(block);
			doc.Entities.Add(top);

			_ = renderEntity(top, out RenderLog log);

			Assert.Contains(log.Entries, e => e.Status == RenderStatus.Error && e.Reason.Contains("Circular INSERT"));
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
