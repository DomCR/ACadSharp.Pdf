using ACadSharp.Entities;
using CSMath;
using System;
using System.Collections.Generic;

namespace ACadSharp.Pdf.Core.Render
{
	public abstract class RenderNode
	{
		public ulong SourceHandle { get; }

		protected RenderNode(ulong sourceHandle)
		{
			this.SourceHandle = sourceHandle;
		}
	}

	public sealed class PathNode : RenderNode
	{
		public IReadOnlyList<PathSegment> Segments { get; }

		public StrokeStyle Stroke { get; }

		public FillStyle Fill { get; }

		public PathNode(ulong sourceHandle, IReadOnlyList<PathSegment> segments, StrokeStyle stroke, FillStyle fill)
			: base(sourceHandle)
		{
			this.Segments = segments ?? throw new ArgumentNullException(nameof(segments));
			this.Stroke = stroke;
			this.Fill = fill;
		}
	}

	public sealed class TextRunNode : RenderNode
	{
		public string Text { get; }

		public string FontName { get; }

		public double FontSizePt { get; }

		public XY AnchorPt { get; }

		public double A { get; }

		public double B { get; }

		public double C { get; }

		public double D { get; }

		public ACadSharp.Color Color { get; }

		public TextAlignment HAlign { get; }

		public TextVAlignment VAlign { get; }

		public TextRunNode(
			ulong sourceHandle,
			string text,
			string fontName,
			double fontSizePt,
			XY anchorPt,
			double a,
			double b,
			double c,
			double d,
			ACadSharp.Color color,
			TextAlignment hAlign,
			TextVerticalAlignmentType vAlign)
			: base(sourceHandle)
		{
			this.Text = text ?? string.Empty;
			this.FontName = fontName ?? string.Empty;
			this.FontSizePt = fontSizePt;
			this.AnchorPt = anchorPt;
			this.A = a;
			this.B = b;
			this.C = c;
			this.D = d;
			this.Color = color;
			this.HAlign = hAlign;
			this.VAlign = vAlign switch
			{
				TextVerticalAlignmentType.Bottom => TextVAlignment.Bottom,
				TextVerticalAlignmentType.Middle => TextVAlignment.Middle,
				TextVerticalAlignmentType.Top => TextVAlignment.Top,
				_ => TextVAlignment.Baseline,
			};
		}
	}

	public sealed class ImageNode : RenderNode
	{
		public byte[] Rgb24Data { get; }

		public int SourceWidthPixels { get; }

		public int SourceHeightPixels { get; }

		public double DisplayWidth { get; }

		public double DisplayHeight { get; }

		public ImageNode(
			ulong sourceHandle,
			byte[] rgb24Data,
			int sourceWidthPixels,
			int sourceHeightPixels,
			double displayWidth,
			double displayHeight)
			: base(sourceHandle)
		{
			this.Rgb24Data = rgb24Data ?? throw new ArgumentNullException(nameof(rgb24Data));
			this.SourceWidthPixels = sourceWidthPixels;
			this.SourceHeightPixels = sourceHeightPixels;
			this.DisplayWidth = displayWidth;
			this.DisplayHeight = displayHeight;
		}
	}

	public sealed class GroupNode : RenderNode
	{
		public Matrix4 Transform { get; }

		public IReadOnlyList<RenderNode> Children { get; }

		public GroupNode(ulong sourceHandle, Matrix4 transform, IReadOnlyList<RenderNode> children)
			: base(sourceHandle)
		{
			this.Transform = transform;
			this.Children = children ?? throw new ArgumentNullException(nameof(children));
		}
	}

	public sealed class ClipNode : RenderNode
	{
		public PathNode ClipPath { get; }

		public IReadOnlyList<RenderNode> Children { get; }

		public ClipNode(ulong sourceHandle, PathNode clipPath, IReadOnlyList<RenderNode> children)
			: base(sourceHandle)
		{
			this.ClipPath = clipPath ?? throw new ArgumentNullException(nameof(clipPath));
			this.Children = children ?? throw new ArgumentNullException(nameof(children));
		}
	}
}
