using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf;
using ACadSharp.Pdf.Core;
using ACadSharp.Pdf.Core.Render;
using ACadSharp.Tables;
using CSMath;
using System;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class RayXLineViewportSceneGraphTests
	{
		[Fact]
		public void Viewport_RendersRayAndXLineDespiteInfiniteBoundingBoxes()
		{
			var doc = new CadDocument();
			var ray = new Ray
			{
				StartPoint = new XYZ(50, 50, 0),
				Direction = new XYZ(1, 0, 0),
			};
			var xline = new XLine
			{
				FirstPoint = new XYZ(50, 50, 0),
				Direction = new XYZ(0, 1, 0),
			};
			doc.Entities.Add(ray);
			doc.Entities.Add(xline);

			var viewport = new Viewport
			{
				Center = new XYZ(50, 50, 0),
				Width = 100,
				Height = 100,
				ViewCenter = new XY(50, 50),
				ViewHeight = 100,
				ViewDirection = XYZ.AxisZ,
			};
			doc.PaperSpace.Entities.Add(viewport);

			var pdf = new PdfDocument();
			PdfPage page = pdf.Pages.AddPage();
			page.Layout = new Layout("L")
			{
				PaperUnits = PlotPaperUnits.Pixels,
				DenominatorScale = 1.0,
				PaperWidth = 200,
				PaperHeight = 200,
			};
			page.Viewports.Add(viewport);

			var cfg = new PdfConfiguration { UseSceneGraph = true };
			_ = page.Contents.GetPdfForm(cfg);
			RenderLog log = cfg.LastRenderLog;

			Assert.Contains(log.Entries, e => e.Handle == ray.Handle && e.Status == RenderStatus.Rendered);
			Assert.Contains(log.Entries, e => e.Handle == xline.Handle && e.Status == RenderStatus.Rendered);
		}
	}
}

