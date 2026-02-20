using ACadSharp.Objects;
using ACadSharp.Pdf.Core.Render.Transforms;
using CSMath;
using System;
using System.Collections.Generic;

namespace ACadSharp.Pdf.Core.Render.Flattening
{
	internal sealed class SceneFlattener
	{
		private readonly Layout _layout;

		public SceneFlattener(Layout layout)
		{
			this._layout = layout ?? throw new ArgumentNullException(nameof(layout));
		}

		public IReadOnlyList<FlatDrawCommand> Flatten(IReadOnlyList<RenderNode> nodes)
		{
			var commands = new List<FlatDrawCommand>();
			if (nodes == null || nodes.Count == 0)
			{
				return commands;
			}

			Matrix4 identity = Matrix4.Identity;
			foreach (var node in nodes)
			{
				flattenNode(node, identity, commands);
			}

			return commands;
		}

		private void flattenNode(RenderNode node, Matrix4 current, List<FlatDrawCommand> commands)
		{
			switch (node)
			{
				case GroupNode group:
					{
						Matrix4 next = current * group.Transform;
						foreach (var child in group.Children)
						{
							flattenNode(child, next, commands);
						}
						break;
					}
				case ClipNode clip:
					{
						var clipSegs = transformAndConvertSegments(clip.ClipPath.Segments, current);
						commands.Add(new FlatBeginClipCommand(clipSegs));
						foreach (var child in clip.Children)
						{
							flattenNode(child, current, commands);
						}
						commands.Add(new FlatEndClipCommand());
						break;
					}
				case PathNode path:
					{
						var segs = transformAndConvertSegments(path.Segments, current);
						commands.Add(new FlatPathCommand(segs, path.Stroke, path.Fill));
						break;
					}
				case TextRunNode text:
					{
						XY anchorPaper = transformPoint(text.AnchorPt, current);
						XY anchorPdf = TransformHelper.PaperToPdfPoints(anchorPaper, this._layout);
						commands.Add(new FlatTextCommand(
							text: text.Text,
							fontSizePt: text.FontSizePt,
							anchorPdfPt: anchorPdf,
							a: text.A,
							b: text.B,
							c: text.C,
							d: text.D,
							color: text.Color));
						break;
					}
				case ImageNode image:
					{
						FlatImageCommand cmd = createFlatImageCommand(image, current);
						if (cmd != null)
						{
							commands.Add(cmd);
						}
						break;
					}
				default:
					break;
			}
		}

		private FlatImageCommand createFlatImageCommand(ImageNode image, Matrix4 current)
		{
			if (image == null || image.Rgb24Data == null || image.Rgb24Data.Length == 0)
			{
				return null;
			}

			XY originPaper = transformPoint(XY.Zero, current);
			XY axisXPaper = transformPoint(new XY(1.0, 0.0), current);
			XY axisYPaper = transformPoint(new XY(0.0, 1.0), current);

			XY originPdf = TransformHelper.PaperToPdfPoints(originPaper, this._layout);
			XY axisXPdf = TransformHelper.PaperToPdfPoints(axisXPaper, this._layout);
			XY axisYPdf = TransformHelper.PaperToPdfPoints(axisYPaper, this._layout);

			double a = axisXPdf.X - originPdf.X;
			double b = axisXPdf.Y - originPdf.Y;
			double c = axisYPdf.X - originPdf.X;
			double d = axisYPdf.Y - originPdf.Y;

			return new FlatImageCommand(
				rgb24Data: image.Rgb24Data,
				sourceWidthPixels: image.SourceWidthPixels,
				sourceHeightPixels: image.SourceHeightPixels,
				displayWidth: image.DisplayWidth,
				displayHeight: image.DisplayHeight,
				a: a,
				b: b,
				c: c,
				d: d,
				e: originPdf.X,
				f: originPdf.Y);
		}

		private IReadOnlyList<PathSegment> transformAndConvertSegments(IReadOnlyList<PathSegment> segments, Matrix4 matrix)
		{
			if (segments == null || segments.Count == 0)
			{
				return Array.Empty<PathSegment>();
			}

			var result = new List<PathSegment>(segments.Count);
			foreach (var seg in segments)
			{
				switch (seg)
				{
					case MoveTo m:
						result.Add(new MoveTo(TransformHelper.PaperToPdfPoints(transformPoint(m.Point, matrix), this._layout)));
						break;
					case LineTo l:
						result.Add(new LineTo(TransformHelper.PaperToPdfPoints(transformPoint(l.Point, matrix), this._layout)));
						break;
					case CubicTo c:
						result.Add(new CubicTo(
							TransformHelper.PaperToPdfPoints(transformPoint(c.C1, matrix), this._layout),
							TransformHelper.PaperToPdfPoints(transformPoint(c.C2, matrix), this._layout),
							TransformHelper.PaperToPdfPoints(transformPoint(c.End, matrix), this._layout)));
						break;
					case ClosePath:
						result.Add(new ClosePath());
						break;
				}
			}

			return result;
		}

		private static XY transformPoint(XY p, Matrix4 m)
		{
			XYZ v = m * new XYZ(p.X, p.Y, 0.0);
			return new XY(v.X, v.Y);
		}
	}
}
