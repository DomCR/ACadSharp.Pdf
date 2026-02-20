using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Pdf;
using ACadSharp.Tables;
using CSMath;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace ACadSharp.Pdf.Tests
{
	public class MultiLeaderRenderingTests
	{
		[Fact]
		public void MultiLeader_MTextStraight_RendersLeaderDoglegAndText()
		{
			MultiLeader mleader = createTextLeader(pathType: MultiLeaderPathType.StraightLineSegments);

			string content = renderEntity(mleader, out _);

			Assert.Contains("(ML-TEXT) Tj", content);
			Assert.Contains("0 0 m", content);
			Assert.Contains("20 0 l", content);
			Assert.Contains("40 0 m", content);
			Assert.Contains("50 0 l", content);
		}

		[Fact]
		public void MultiLeader_InvisiblePath_RendersContentOnly()
		{
			MultiLeader mleader = createTextLeader(pathType: MultiLeaderPathType.Invisible);

			string content = renderEntity(mleader, out _);

			Assert.Contains("(ML-TEXT) Tj", content);
			Assert.DoesNotContain(" m\n", content);
			Assert.DoesNotContain(" l\n", content);
		}

		[Fact]
		public void MultiLeader_SplinePath_TessellatesCurve()
		{
			MultiLeader mleader = createTextLeader(pathType: MultiLeaderPathType.Spline);
			var root = mleader.ContextData.LeaderRoots[0];
			var line = root.Lines[0];
			line.Points.Clear();
			line.Points.Add(new XYZ(0, 0, 0));
			line.Points.Add(new XYZ(10, 10, 0));
			line.Points.Add(new XYZ(20, 0, 0));
			root.ConnectionPoint = new XYZ(30, 0, 0);
			root.Direction = new XYZ(1, 0, 0);
			root.LandingDistance = 10.0;

			string content = renderEntity(mleader, out _);
			int lineCommands = Regex.Matches(content, @"\sl\s").Count;

			Assert.True(lineCommands >= 6);
		}

		[Fact]
		public void MultiLeader_LineBreak_SplitsLeaderSegment()
		{
			var mleader = new MultiLeader
			{
				Layer = new Layer("0") { Color = Color.Red },
				PathType = MultiLeaderPathType.StraightLineSegments,
				ContentType = LeaderContentType.None,
				EnableDogleg = false,
				LandingDistance = 0.0,
				PropertyOverrideFlags =
					MultiLeaderPropertyOverrideFlags.PathType
					| MultiLeaderPropertyOverrideFlags.ContentType
					| MultiLeaderPropertyOverrideFlags.EnableDogleg
					| MultiLeaderPropertyOverrideFlags.LandingDistance,
			};

			var root = new MultiLeaderObjectContextData.LeaderRoot
			{
				ConnectionPoint = new XYZ(30, 0, 0),
				Direction = new XYZ(1, 0, 0),
				LandingDistance = 0.0,
			};

			var line = new MultiLeaderObjectContextData.LeaderLine
			{
				SegmentIndex = 0,
			};
			line.Points.Add(new XYZ(0, 0, 0));
			line.Points.Add(new XYZ(20, 0, 0));
			line.StartEndPoints.Add(new MultiLeaderObjectContextData.StartEndPointPair(
				new XYZ(5, 0, 0),
				new XYZ(10, 0, 0)));
			root.Lines.Add(line);
			mleader.ContextData.LeaderRoots.Add(root);

			string content = renderEntity(mleader, out _);

			Assert.Contains("0 0 m", content);
			Assert.Contains("5 0 l", content);
			Assert.Contains("10 0 m", content);
			Assert.Contains("30 0 l", content);
		}

		[Fact]
		public void MultiLeader_BlockContent_AppliesAttributeOverrides()
		{
			var block = new BlockRecord("ML-BLOCK");
			var attDef = new AttributeDefinition
			{
				Tag = "LABEL",
				Value = "DEFAULT",
				InsertPoint = XYZ.Zero,
				Height = 2.0,
			};
			block.Entities.Add(attDef);

			var mleader = new MultiLeader
			{
				Layer = new Layer("0") { Color = Color.Blue },
				PathType = MultiLeaderPathType.Invisible,
				ContentType = LeaderContentType.Block,
				PropertyOverrideFlags =
					MultiLeaderPropertyOverrideFlags.PathType
					| MultiLeaderPropertyOverrideFlags.ContentType
					| MultiLeaderPropertyOverrideFlags.BlockContent,
			};

			mleader.ContextData.HasContentsBlock = true;
			mleader.ContextData.BlockContent = block;
			mleader.ContextData.BlockContentLocation = new XYZ(30, 30, 0);
			mleader.ContextData.BlockContentScale = new XYZ(1, 1, 1);
			mleader.BlockAttributes.Add(new MultiLeader.BlockAttribute
			{
				AttributeDefinition = attDef,
				Text = "42",
			});

			string content = renderEntity(mleader, out _);

			Assert.Contains("(42) Tj", content);
			Assert.DoesNotContain("(DEFAULT) Tj", content);
		}

		[Fact]
		public void MultiLeader_InInsert_AppliesParentTransform()
		{
			MultiLeader nested = createTextLeader(pathType: MultiLeaderPathType.StraightLineSegments);
			nested.ContextData.TextLabel = "IN-BLOCK";
			nested.ContextData.TextLocation = new XYZ(25, 0, 0);
			nested.EnableDogleg = false;
			nested.LandingDistance = 0.0;
			nested.ContextData.LeaderRoots[0].LandingDistance = 0.0;

			var block = new BlockRecord("ML-INSERT");
			block.Entities.Add(nested);

			var insert = new Insert(block)
			{
				InsertPoint = new XYZ(100, 50, 0),
				XScale = 2.0,
				YScale = 2.0,
				ZScale = 1.0,
			};

			string content = renderEntity(insert, out _);

			Assert.Contains("100 50 m", content);
			Assert.Contains("140 50 l", content);
			Assert.Contains("(IN-BLOCK) Tj", content);
		}

		[Fact]
		public void MultiLeader_DoglegDisabled_ExtendsLeaderToDoglegEndpoint()
		{
			var mleader = new MultiLeader
			{
				Layer = new Layer("0") { Color = Color.Red },
				PathType = MultiLeaderPathType.StraightLineSegments,
				ContentType = LeaderContentType.None,
				EnableDogleg = false,
				EnableLanding = true,
				LandingDistance = 10.0,
				PropertyOverrideFlags =
					MultiLeaderPropertyOverrideFlags.PathType
					| MultiLeaderPropertyOverrideFlags.ContentType
					| MultiLeaderPropertyOverrideFlags.EnableDogleg
					| MultiLeaderPropertyOverrideFlags.EnableLanding
					| MultiLeaderPropertyOverrideFlags.LandingDistance,
			};

			mleader.ContextData.ContentBasePoint = new XYZ(60, 0, 0);

			var root = new MultiLeaderObjectContextData.LeaderRoot
			{
				ConnectionPoint = new XYZ(40, 0, 0),
				Direction = new XYZ(1, 0, 0),
				LandingDistance = 10.0,
				TextAttachmentDirection = TextAttachmentDirectionType.Horizontal,
			};

			var line = new MultiLeaderObjectContextData.LeaderLine();
			line.Points.Add(new XYZ(0, 0, 0));
			line.Points.Add(new XYZ(20, 0, 0));
			line.Points.Add(new XYZ(40, 0, 0));
			root.Lines.Add(line);
			mleader.ContextData.LeaderRoots.Add(root);

			string content = renderEntity(mleader, out _);

			Assert.Contains("50 0 l", content);
			Assert.DoesNotContain("40 0 m\n50 0 l", content);
		}

		[Fact]
		public void MultiLeader_InvisibleLeaderLine_DoesNotDrawDogleg()
		{
			var mleader = new MultiLeader
			{
				Layer = new Layer("0") { Color = Color.Red },
				PathType = MultiLeaderPathType.StraightLineSegments,
				ContentType = LeaderContentType.None,
				EnableDogleg = true,
				EnableLanding = true,
				LandingDistance = 10.0,
				PropertyOverrideFlags =
					MultiLeaderPropertyOverrideFlags.PathType
					| MultiLeaderPropertyOverrideFlags.ContentType
					| MultiLeaderPropertyOverrideFlags.EnableDogleg
					| MultiLeaderPropertyOverrideFlags.EnableLanding
					| MultiLeaderPropertyOverrideFlags.LandingDistance,
			};

			var root = new MultiLeaderObjectContextData.LeaderRoot
			{
				ConnectionPoint = new XYZ(40, 0, 0),
				Direction = new XYZ(1, 0, 0),
				LandingDistance = 10.0,
				TextAttachmentDirection = TextAttachmentDirectionType.Horizontal,
			};

			var line = new MultiLeaderObjectContextData.LeaderLine
			{
				PathType = MultiLeaderPathType.Invisible,
				OverrideFlags = LeaderLinePropertOverrideFlags.PathType,
			};
			line.Points.Add(new XYZ(0, 0, 0));
			line.Points.Add(new XYZ(20, 0, 0));
			root.Lines.Add(line);
			mleader.ContextData.LeaderRoots.Add(root);

			string content = renderEntity(mleader, out _);

			Assert.DoesNotContain(" m\n", content);
			Assert.DoesNotContain(" l\n", content);
		}

		[Fact]
		public void MultiLeader_DoglegBreak_SplitsDoglegSegment()
		{
			var mleader = new MultiLeader
			{
				Layer = new Layer("0") { Color = Color.Red },
				PathType = MultiLeaderPathType.StraightLineSegments,
				ContentType = LeaderContentType.None,
				EnableDogleg = true,
				EnableLanding = true,
				LandingDistance = 10.0,
				PropertyOverrideFlags =
					MultiLeaderPropertyOverrideFlags.PathType
					| MultiLeaderPropertyOverrideFlags.ContentType
					| MultiLeaderPropertyOverrideFlags.EnableDogleg
					| MultiLeaderPropertyOverrideFlags.EnableLanding
					| MultiLeaderPropertyOverrideFlags.LandingDistance,
			};

			var root = new MultiLeaderObjectContextData.LeaderRoot
			{
				ConnectionPoint = new XYZ(40, 0, 0),
				Direction = new XYZ(1, 0, 0),
				LandingDistance = 10.0,
				TextAttachmentDirection = TextAttachmentDirectionType.Horizontal,
			};
			root.BreakStartEndPointsPairs.Add(new MultiLeaderObjectContextData.StartEndPointPair(
				new XYZ(43, 0, 0),
				new XYZ(47, 0, 0)));

			var line = new MultiLeaderObjectContextData.LeaderLine();
			line.Points.Add(new XYZ(0, 0, 0));
			line.Points.Add(new XYZ(20, 0, 0));
			root.Lines.Add(line);
			mleader.ContextData.LeaderRoots.Add(root);

			string content = renderEntity(mleader, out _);

			Assert.Contains("40 0 m", content);
			Assert.Contains("43 0 l", content);
			Assert.Contains("47 0 m", content);
			Assert.Contains("50 0 l", content);
		}

		[Fact]
		public void MultiLeader_MultipleLeaderRoots_RendersContentOnce()
		{
			MultiLeader mleader = createTextLeader(pathType: MultiLeaderPathType.StraightLineSegments);
			mleader.ContextData.TextLabel = "SHARED";

			var root2 = new MultiLeaderObjectContextData.LeaderRoot
			{
				ConnectionPoint = new XYZ(40, 10, 0),
				Direction = new XYZ(1, 0, 0),
				LandingDistance = 10.0,
				TextAttachmentDirection = TextAttachmentDirectionType.Horizontal,
			};

			var line2 = new MultiLeaderObjectContextData.LeaderLine();
			line2.Points.Add(new XYZ(0, 10, 0));
			line2.Points.Add(new XYZ(20, 10, 0));
			root2.Lines.Add(line2);
			mleader.ContextData.LeaderRoots.Add(root2);

			string content = renderEntity(mleader, out _);

			Assert.Contains("0 0 m", content);
			Assert.Contains("0 10 m", content);
			Assert.True(Regex.Matches(content, @"\(SHARED\) Tj").Count == 1);
		}

		[Fact]
		public void MultiLeader_DirectionFallback_PointsDoglegTowardContent()
		{
			var mleader = new MultiLeader
			{
				Layer = new Layer("0") { Color = Color.Red },
				PathType = MultiLeaderPathType.StraightLineSegments,
				ContentType = LeaderContentType.None,
				EnableDogleg = true,
				EnableLanding = true,
				LandingDistance = 10.0,
				PropertyOverrideFlags =
					MultiLeaderPropertyOverrideFlags.PathType
					| MultiLeaderPropertyOverrideFlags.ContentType
					| MultiLeaderPropertyOverrideFlags.EnableDogleg
					| MultiLeaderPropertyOverrideFlags.EnableLanding
					| MultiLeaderPropertyOverrideFlags.LandingDistance,
			};

			// Content is left of the landing point, so the inferred dogleg direction is -X.
			mleader.ContextData.ContentBasePoint = new XYZ(20, 0, 0);

			var root = new MultiLeaderObjectContextData.LeaderRoot
			{
				ConnectionPoint = new XYZ(40, 0, 0),
				Direction = XYZ.Zero,
				LandingDistance = 10.0,
				TextAttachmentDirection = TextAttachmentDirectionType.Horizontal,
			};

			var line = new MultiLeaderObjectContextData.LeaderLine();
			line.Points.Add(new XYZ(0, 0, 0));
			line.Points.Add(new XYZ(20, 0, 0));
			root.Lines.Add(line);
			mleader.ContextData.LeaderRoots.Add(root);

			string content = renderEntity(mleader, out _);

			Assert.Contains("40 0 m", content);
			Assert.Contains("30 0 l", content);
		}

		[Fact]
		public void MultiLeader_CustomArrowBlock_RendersArrowBlockGeometry()
		{
			var arrowBlock = new BlockRecord("ML-ARROW");
			arrowBlock.Entities.Add(new Line
			{
				StartPoint = XYZ.Zero,
				EndPoint = new XYZ(0, 1, 0),
			});

			var mleader = new MultiLeader
			{
				Layer = new Layer("0") { Color = Color.Red },
				PathType = MultiLeaderPathType.StraightLineSegments,
				ContentType = LeaderContentType.None,
				EnableDogleg = false,
				EnableLanding = true,
				LandingDistance = 0.0,
				Arrowhead = arrowBlock,
				ArrowheadSize = 2.0,
				PropertyOverrideFlags =
					MultiLeaderPropertyOverrideFlags.PathType
					| MultiLeaderPropertyOverrideFlags.ContentType
					| MultiLeaderPropertyOverrideFlags.EnableDogleg
					| MultiLeaderPropertyOverrideFlags.EnableLanding
					| MultiLeaderPropertyOverrideFlags.LandingDistance
					| MultiLeaderPropertyOverrideFlags.Arrowhead
					| MultiLeaderPropertyOverrideFlags.ArrowheadSize,
			};

			var root = new MultiLeaderObjectContextData.LeaderRoot
			{
				ConnectionPoint = new XYZ(20, 0, 0),
				Direction = new XYZ(1, 0, 0),
				LandingDistance = 0.0,
				TextAttachmentDirection = TextAttachmentDirectionType.Horizontal,
			};

			var line = new MultiLeaderObjectContextData.LeaderLine();
			line.Points.Add(new XYZ(0, 0, 0));
			line.Points.Add(new XYZ(20, 0, 0));
			root.Lines.Add(line);
			mleader.ContextData.LeaderRoots.Add(root);

			string content = renderEntity(mleader, out _);

			// The arrow block line points up in block space; with a horizontal leader it rotates by π at the tip,
			// resulting in a downward line in WCS.
			Assert.Contains("0 -2 l", content);
		}

			[Fact]
			public void MultiLeader_DxfParsing_ReadsLeaderAndDoglegBreaks()
			{
				// net48 doesn't have string.Join(char, string[]) overload.
				string dxf = string.Join("\n", new[]
				{
					"0", "SECTION",
					"2", "HEADER",
					"9", "$ACADVER",
					"1", "AC1027",
				"0", "ENDSEC",
				"0", "SECTION",
				"2", "ENTITIES",
				"0", "MULTILEADER",
				"100", "AcDbEntity",
				"8", "0",
				"100", "AcDbMLeader",
				"170", "1", // straight leaders
				"300", "CONTEXT_DATA{",
				"40", "1.0", // content scale
				"10", "0.0",
				"20", "0.0",
				"30", "0.0",
				"302", "LEADER{",
				"10", "30.0", // connection point (landing point)
				"20", "0.0",
				"30", "0.0",
				"11", "1.0", // dogleg vector
				"21", "0.0",
				"31", "0.0",
				"40", "10.0", // landing distance
				// Dogleg break 33..37 (in the dogleg segment 30..40)
				"12", "33.0",
				"22", "0.0",
				"32", "0.0",
				"13", "37.0",
				"23", "0.0",
				"33", "0.0",
				"304", "LEADER_LINE{",
				// vertices 0 -> 20 -> (landing point appended by renderer)
				"10", "0.0",
				"20", "0.0",
				"30", "0.0",
				// Leader line break 5..10 on segment 0..20
				"11", "5.0",
				"21", "0.0",
				"31", "0.0",
				"12", "10.0",
				"22", "0.0",
				"32", "0.0",
				"10", "20.0",
				"20", "0.0",
				"30", "0.0",
				"305", "}",
				"303", "}",
				"301", "}",
				"0", "ENDSEC",
				"0", "EOF",
				string.Empty,
			});

			using var stream = new MemoryStream(Encoding.ASCII.GetBytes(dxf));
			CadDocument doc = DxfReader.Read(stream);
			MultiLeader mleader = doc.Entities.OfType<MultiLeader>().Single();

			string content = renderEntity(mleader, out _);

			Assert.Contains("5 0 l", content);
			Assert.Contains("10 0 m", content);
			Assert.Contains("33 0 l", content);
			Assert.Contains("37 0 m", content);
		}

		private static MultiLeader createTextLeader(MultiLeaderPathType pathType)
		{
			var mleader = new MultiLeader
			{
				Layer = new Layer("0") { Color = Color.Red },
				PathType = pathType,
				ContentType = LeaderContentType.MText,
				EnableDogleg = true,
				EnableLanding = true,
				LandingDistance = 10.0,
				ArrowheadSize = 2.5,
				PropertyOverrideFlags =
					MultiLeaderPropertyOverrideFlags.PathType
					| MultiLeaderPropertyOverrideFlags.ContentType
					| MultiLeaderPropertyOverrideFlags.EnableDogleg
					| MultiLeaderPropertyOverrideFlags.EnableLanding
					| MultiLeaderPropertyOverrideFlags.LandingDistance
					| MultiLeaderPropertyOverrideFlags.ArrowheadSize
					| MultiLeaderPropertyOverrideFlags.TextStyle
					| MultiLeaderPropertyOverrideFlags.TextHeight
					| MultiLeaderPropertyOverrideFlags.TextColor,
			};

			mleader.ContextData.HasTextContents = true;
			mleader.ContextData.TextLabel = "ML-TEXT";
			mleader.ContextData.TextLocation = new XYZ(60, 0, 0);
			mleader.ContextData.TextHeight = 5.0;
			mleader.ContextData.TextStyle = TextStyle.Default;
			mleader.ContextData.TextColor = Color.ByLayer;
			mleader.ContextData.TextAlignment = TextAlignmentType.Left;
			mleader.ContextData.TextNormal = XYZ.AxisZ;
			mleader.ContextData.Direction = XYZ.AxisX;

			var root = new MultiLeaderObjectContextData.LeaderRoot
			{
				ConnectionPoint = new XYZ(40, 0, 0),
				Direction = new XYZ(1, 0, 0),
				LandingDistance = 10.0,
				TextAttachmentDirection = TextAttachmentDirectionType.Horizontal,
			};

			var line = new MultiLeaderObjectContextData.LeaderLine();
			line.Points.Add(new XYZ(0, 0, 0));
			line.Points.Add(new XYZ(20, 0, 0));

			root.Lines.Add(line);
			mleader.ContextData.LeaderRoots.Add(root);
			return mleader;
		}

		private static string renderEntity(Entity entity, out ACadSharp.Pdf.Core.Render.RenderLog log)
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
