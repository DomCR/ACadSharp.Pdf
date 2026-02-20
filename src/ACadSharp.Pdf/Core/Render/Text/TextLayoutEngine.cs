using ACadSharp.Entities;
using ACadSharp.Extensions;
using ACadSharp.Objects;
using ACadSharp.Pdf.Core.Render.Transforms;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ACadSharp.Pdf.Core.Render.Text
{
	internal sealed class TextLayoutEngine
	{
		private const double DefaultLineSpacingFactor = 1.666;
		private const double Epsilon = 1e-9;

		private readonly Layout _layout;
		private readonly PdfConfiguration _configuration;
		private readonly RenderLog _log;
		private readonly ApproximateTextMetrics _metrics = new ApproximateTextMetrics();
		private readonly FontResolver _fontResolver;

		public TextLayoutEngine(Layout layout, PdfConfiguration configuration, RenderLog log)
		{
			this._layout = layout ?? throw new ArgumentNullException(nameof(layout));
			this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
			this._log = log ?? throw new ArgumentNullException(nameof(log));
			this._fontResolver = new FontResolver(configuration);
		}

			public TextRunNode LayoutText(TextEntity text, double textScaleToPaper, ACadSharp.Color color)
			{
			if (text == null)
			{
				return null;
			}

			string value = text.Value ?? string.Empty;
			if (string.IsNullOrEmpty(value))
			{
				this._log.Add(text.Handle, text.SubclassMarker, RenderStatus.Skipped, "Empty text value.");
				return null;
			}

			if (text.Height <= Epsilon)
			{
				this._log.Add(text.Handle, text.SubclassMarker, RenderStatus.Skipped, "Text height is zero.");
				return null;
			}

				string fontName = this._fontResolver.Resolve(text.Style);
				double styleWidth = text.Style != null && text.Style.Width > Epsilon ? text.Style.Width : 1.0;
				double xScale = (text.WidthFactor > Epsilon ? text.WidthFactor : 1.0) * styleWidth;
				double oblique = text.ObliqueAngle + (text.Style?.ObliqueAngle ?? 0.0);
				double yScale = text.Mirror.HasFlag(TextMirrorFlag.UpsideDown) ? -1.0 : 1.0;
				if (text.Mirror.HasFlag(TextMirrorFlag.Backward))
				{
					xScale = -xScale;
				}

				double height = text.Height;
				double rotation = text.Rotation;
				XYZ anchorOcs = selectTextAnchor(text);
				double xScaleAbs = Math.Abs(xScale);
				bool usesTwoPointStretch = text.HorizontalAlignment == TextHorizontalAlignment.Fit || text.HorizontalAlignment == TextHorizontalAlignment.Aligned;
				if (usesTwoPointStretch)
				{
					XY span = new XY(text.AlignmentPoint.X - text.InsertPoint.X, text.AlignmentPoint.Y - text.InsertPoint.Y);
					double dist = span.GetLength();
					if (dist > Epsilon)
					{
						rotation = span.GetAngle();
						double naturalWidth = this._metrics.MeasureStringWidth(value, fontName, height, xScaleAbs);
						if (naturalWidth > Epsilon)
						{
							double stretch = dist / naturalWidth;
							if (text.HorizontalAlignment == TextHorizontalAlignment.Fit)
							{
								xScale *= stretch;
								xScaleAbs = Math.Abs(xScale);
							}
							else
							{
								height *= stretch;
							}
						}
					}
					anchorOcs = text.InsertPoint;
				}

				if (Math.Abs(xScale) <= Epsilon)
				{
					this._log.Add(text.Handle, text.SubclassMarker, RenderStatus.Skipped, "Text width factor is zero.");
					return null;
				}

			if (height <= Epsilon)
			{
				this._log.Add(text.Handle, text.SubclassMarker, RenderStatus.Skipped, "Text height collapsed after alignment.");
				return null;
			}

				double measuredWidth = this._metrics.MeasureStringWidth(value, fontName, height, xScaleAbs);
				double ascent = this._metrics.GetAscent(height);
				double descent = this._metrics.GetDescent(height);
				XY offset = resolveTextOffset(text, measuredWidth, ascent, descent);

				var basis = createWorldBasis(text.Normal, rotation, oblique, xScale, yScale);
				XYZ anchorWcs = TransformHelper.OcsToWcs(text.Normal) * anchorOcs;
				XY anchor = new XY(
					anchorWcs.X + (basis.A * offset.X) + (basis.C * offset.Y),
					anchorWcs.Y + (basis.B * offset.X) + (basis.D * offset.Y));

			double fontSizePaperUnits = height * textScaleToPaper;
			double fontSizePt = TransformHelper.PaperToPdfPoints(fontSizePaperUnits, this._layout);
			if (fontSizePt <= Epsilon)
			{
				this._log.Add(text.Handle, text.SubclassMarker, RenderStatus.Skipped, "Text font size collapsed.");
				return null;
			}

			return new TextRunNode(
				text.Handle,
				value,
				fontName,
				fontSizePt,
				anchor,
				basis.A,
				basis.B,
				basis.C,
				basis.D,
				color,
				toHorizontalAlignment(text.HorizontalAlignment),
				text.VerticalAlignment);
		}

		public RenderNode LayoutMText(MText mtext, double textScaleToPaper, ACadSharp.Color defaultColor)
		{
			if (mtext == null)
			{
				return null;
			}

			if (mtext.Height <= Epsilon)
			{
				this._log.Add(mtext.Handle, mtext.SubclassMarker, RenderStatus.Skipped, "MTEXT height is zero.");
				return null;
			}

			string content = mtext.Value ?? string.Empty;
			if (string.IsNullOrEmpty(content))
			{
				this._log.Add(mtext.Handle, mtext.SubclassMarker, RenderStatus.Skipped, "MTEXT value is empty.");
				return null;
			}

			var initialState = new MTextState
			{
				FontName = this._fontResolver.Resolve(mtext.Style),
				Height = mtext.Height,
				WidthFactor = 1.0,
				ObliqueRad = 0.0,
				ParagraphAlignment = HorizontalAlignment.Left,
				Color = null,
			};

			var parsedLines = parseMText(content, initialState);
			if (parsedLines.Count == 0)
			{
				this._log.Add(mtext.Handle, mtext.SubclassMarker, RenderStatus.Skipped, "MTEXT parser produced no lines.");
				return null;
			}

			double wrapWidth = mtext.RectangleWidth > Epsilon ? mtext.RectangleWidth : 0.0;
			var laidOut = layoutMTextLines(parsedLines, wrapWidth, mtext.Height, mtext.LineSpacing, mtext.LineSpacingStyle, mtext.AttachmentPoint);
			if (laidOut.Count == 0)
			{
				this._log.Add(mtext.Handle, mtext.SubclassMarker, RenderStatus.Skipped, "MTEXT layout produced no runs.");
				return null;
			}

			double rotation = mtext.Rotation;
			var baseBasis = createWorldBasis(mtext.Normal, rotation, obliqueRad: 0.0, widthFactor: 1.0, yScale: 1.0);
			XY baseX = new XY(baseBasis.A, baseBasis.B);
			XY baseY = new XY(baseBasis.C, baseBasis.D);
			XYZ insert = mtext.InsertPoint;

			var children = new List<RenderNode>();
			foreach (var line in laidOut)
			{
				double cursor = 0.0;
				foreach (var run in line.Runs)
				{
					if (string.IsNullOrEmpty(run.Text))
					{
						continue;
					}

					double runWidth = this._metrics.MeasureStringWidth(run.Text, run.FontName, run.Height, run.WidthFactor);
					double runX = line.StartX + cursor;
					double runY = line.BaselineY;
					cursor += runWidth;

					XY anchor = new XY(
						insert.X + (baseX.X * runX) + (baseY.X * runY),
						insert.Y + (baseX.Y * runX) + (baseY.Y * runY));

					var basis = createWorldBasis(mtext.Normal, rotation, run.ObliqueRad, run.WidthFactor, yScale: 1.0);
					double fontSizePt = TransformHelper.PaperToPdfPoints(run.Height * textScaleToPaper, this._layout);
					if (fontSizePt <= Epsilon)
					{
						continue;
					}

					children.Add(new TextRunNode(
						mtext.Handle,
						run.Text,
						run.FontName,
						fontSizePt,
						anchor,
						basis.A,
						basis.B,
						basis.C,
						basis.D,
						run.Color ?? defaultColor,
						toTextAlignment(line.Alignment),
						TextVerticalAlignmentType.Baseline));
				}
			}

			if (children.Count == 0)
			{
				this._log.Add(mtext.Handle, mtext.SubclassMarker, RenderStatus.Skipped, "MTEXT produced no visible runs.");
				return null;
			}

			return new GroupNode(mtext.Handle, Matrix4.Identity, children);
		}

		public MText BuildAttributeMText(AttributeEntity attribute)
		{
			if (attribute == null || attribute.MText == null)
			{
				return null;
			}

			MText mtext = attribute.MText.CloneTyped();
			mtext.Layer = attribute.Layer;
			mtext.Color = attribute.Color;
			mtext.Style = attribute.Style;
			mtext.InsertPoint = attribute.InsertPoint;
			mtext.Normal = attribute.Normal;
			mtext.Height = attribute.Height;
			if (!string.IsNullOrWhiteSpace(attribute.Value))
			{
				mtext.Value = attribute.Value;
			}

			return mtext;
		}

		private static XYZ selectTextAnchor(TextEntity text)
		{
			if (text.HorizontalAlignment == TextHorizontalAlignment.Left && text.VerticalAlignment == TextVerticalAlignmentType.Baseline)
			{
				return text.InsertPoint;
			}

			if (text.HorizontalAlignment == TextHorizontalAlignment.Fit || text.HorizontalAlignment == TextHorizontalAlignment.Aligned)
			{
				return text.InsertPoint;
			}

			return text.AlignmentPoint;
		}

		private static XY resolveTextOffset(TextEntity text, double width, double ascent, double descent)
		{
			double centerY = (ascent + descent) / 2.0;
			double desiredX = 0.0;
			double desiredY = 0.0;

			switch (text.HorizontalAlignment)
			{
				case TextHorizontalAlignment.Center:
					desiredX = width / 2.0;
					break;
				case TextHorizontalAlignment.Right:
					desiredX = width;
					break;
				case TextHorizontalAlignment.Middle:
					desiredX = width / 2.0;
					desiredY = centerY;
					return new XY(-desiredX, -desiredY);
			}

			switch (text.VerticalAlignment)
			{
				case TextVerticalAlignmentType.Bottom:
					desiredY = descent;
					break;
				case TextVerticalAlignmentType.Middle:
					desiredY = centerY;
					break;
				case TextVerticalAlignmentType.Top:
					desiredY = ascent;
					break;
				default:
					desiredY = 0.0;
					break;
			}

			return new XY(-desiredX, -desiredY);
		}

		private static TextBasis createWorldBasis(XYZ normal, double rotationRad, double obliqueRad, double widthFactor, double yScale)
		{
			if (double.IsNaN(rotationRad) || double.IsInfinity(rotationRad))
			{
				rotationRad = 0.0;
			}

			if (double.IsNaN(obliqueRad) || double.IsInfinity(obliqueRad))
			{
				obliqueRad = 0.0;
			}

			if (double.IsNaN(widthFactor) || double.IsInfinity(widthFactor) || Math.Abs(widthFactor) <= Epsilon)
			{
				widthFactor = 1.0;
			}

			if (double.IsNaN(yScale) || double.IsInfinity(yScale) || Math.Abs(yScale) <= Epsilon)
			{
				yScale = 1.0;
			}

			double shear = Math.Tan(obliqueRad);
			if (double.IsNaN(shear) || double.IsInfinity(shear))
			{
				shear = 0.0;
			}
				else
				{
					// Avoid exploding matrices for pathological obliques.
					if (shear < -1e6) shear = -1e6;
					else if (shear > 1e6) shear = 1e6;
				}

			double cos = Math.Cos(rotationRad);
			double sin = Math.Sin(rotationRad);
			if (double.IsNaN(cos) || double.IsInfinity(cos)) cos = 1.0;
			if (double.IsNaN(sin) || double.IsInfinity(sin)) sin = 0.0;

			double a = cos * widthFactor;
			double b = sin * widthFactor;
			double c = yScale * ((-sin) + (cos * shear));
			double d = yScale * (cos + (sin * shear));

			Matrix4 ocsToWcs = TransformHelper.OcsToWcs(normal);
			XYZ x = ocsToWcs * new XYZ(a, b, 0.0);
			XYZ y = ocsToWcs * new XYZ(c, d, 0.0);
			return new TextBasis(x.X, x.Y, y.X, y.Y);
		}

		private List<MTextLineLayout> layoutMTextLines(IReadOnlyList<MTextLine> parsedLines, double wrapWidth, double defaultHeight, double lineSpacingFactor, LineSpacingStyleType style, AttachmentPointType attachmentPoint)
		{
			var lines = new List<MTextLineLayout>();
			foreach (var parsed in parsedLines)
			{
				if (wrapWidth > Epsilon)
				{
					lines.AddRange(wrapLine(parsed, wrapWidth));
				}
				else
				{
					lines.Add(measureLine(parsed.Runs, parsed.Alignment));
				}
			}

			if (lines.Count == 0)
			{
				lines.Add(new MTextLineLayout
				{
					Runs = new List<MTextRun>(),
					Alignment = HorizontalAlignment.Left,
					Width = 0.0,
					MaxHeight = defaultHeight,
				});
			}

			double factor = lineSpacingFactor > Epsilon ? lineSpacingFactor : 1.0;
			double baseAdvance = DefaultLineSpacingFactor * defaultHeight * factor;
			double blockHeight = 0.0;
			foreach (var line in lines)
			{
				double advance = baseAdvance;
				if (style == LineSpacingStyleType.AtLeast)
				{
					advance = Math.Max(baseAdvance, line.MaxHeight);
				}
				line.Advance = advance;
				blockHeight += advance;
			}

			double effectiveWidth = wrapWidth > Epsilon ? wrapWidth : lines.Max(l => l.Width);
			if (effectiveWidth < 0)
			{
				effectiveWidth = 0;
			}

			double yOffset = resolveMTextVerticalOffset(blockHeight, attachmentPoint);
			double y = yOffset;
			for (int i = 0; i < lines.Count; i++)
			{
				var line = lines[i];
				y -= line.Advance;
				line.BaselineY = y;
				line.StartX = resolveMTextHorizontalOffset(line.Alignment, line.Width, effectiveWidth, attachmentPoint);
			}

			return lines;
		}

		private static double resolveMTextVerticalOffset(double blockHeight, AttachmentPointType attachmentPoint)
		{
			switch (attachmentPoint)
			{
				case AttachmentPointType.MiddleLeft:
				case AttachmentPointType.MiddleCenter:
				case AttachmentPointType.MiddleRight:
					return blockHeight / 2.0;
				case AttachmentPointType.BottomLeft:
				case AttachmentPointType.BottomCenter:
				case AttachmentPointType.BottomRight:
					return blockHeight;
				default:
					return 0.0;
			}
		}

		private static double resolveMTextHorizontalOffset(HorizontalAlignment paragraphAlignment, double lineWidth, double effectiveWidth, AttachmentPointType attachmentPoint)
		{
			HorizontalAlignment attachmentAlignment = attachmentPoint switch
			{
				AttachmentPointType.TopCenter or AttachmentPointType.MiddleCenter or AttachmentPointType.BottomCenter => HorizontalAlignment.Center,
				AttachmentPointType.TopRight or AttachmentPointType.MiddleRight or AttachmentPointType.BottomRight => HorizontalAlignment.Right,
				_ => HorizontalAlignment.Left,
			};

			HorizontalAlignment align = paragraphAlignment == HorizontalAlignment.Left ? attachmentAlignment : paragraphAlignment;
			switch (align)
			{
				case HorizontalAlignment.Center:
					return (effectiveWidth - lineWidth) / 2.0;
				case HorizontalAlignment.Right:
					return effectiveWidth - lineWidth;
				default:
					return 0.0;
			}
		}

			private IEnumerable<MTextLineLayout> wrapLine(MTextLine line, double wrapWidth)
			{
				var result = new List<MTextLineLayout>();
				var current = new List<MTextRun>();
				double currentWidth = 0.0;

				foreach (var run in line.Runs)
				{
					foreach (var token in tokenize(run.Text))
					{
						bool breakableSpace = isBreakableWhitespace(token);
						double tokenWidth = this._metrics.MeasureStringWidth(token, run.FontName, run.Height, run.WidthFactor);

						if (breakableSpace && current.Count == 0)
						{
							continue;
						}

						if (!breakableSpace && current.Count == 0 && tokenWidth > wrapWidth + Epsilon)
						{
							// Very long token: fall back to character-level wrapping.
							foreach (string part in splitToken(run, token, wrapWidth))
							{
								appendRun(current, run, part);
								trimTrailingWhitespace(current);
								result.Add(measureLine(current, line.Alignment));
								current = new List<MTextRun>();
								currentWidth = 0.0;
							}
							continue;
						}

						if (!breakableSpace && current.Count > 0 && currentWidth + tokenWidth > wrapWidth + Epsilon)
						{
							trimTrailingWhitespace(current);
							result.Add(measureLine(current, line.Alignment));
							current = new List<MTextRun>();
							currentWidth = 0.0;
						}

						appendRun(current, run, token);
						currentWidth += tokenWidth;
					}
				}

			trimTrailingWhitespace(current);
				result.Add(measureLine(current, line.Alignment));
				return result;
			}

			private IEnumerable<string> splitToken(MTextRun run, string token, double wrapWidth)
			{
				if (string.IsNullOrEmpty(token))
				{
					yield break;
				}

				double perChar = this._metrics.MeasureStringWidth("A", run.FontName, run.Height, run.WidthFactor);
				perChar = Math.Abs(perChar);
				if (perChar <= Epsilon)
				{
					yield return token;
					yield break;
				}

				int maxChars = Math.Max(1, (int)Math.Floor(wrapWidth / perChar));
				for (int i = 0; i < token.Length; i += maxChars)
				{
					yield return token.Substring(i, Math.Min(maxChars, token.Length - i));
				}
			}

			private MTextLineLayout measureLine(IReadOnlyList<MTextRun> runs, HorizontalAlignment alignment)
			{
				double width = 0.0;
				var clonedRuns = new List<MTextRun>(runs.Count);
				foreach (var run in runs)
				{
					if (run == null || string.IsNullOrEmpty(run.Text))
					{
						continue;
					}

					width += this._metrics.MeasureStringWidth(run.Text, run.FontName, run.Height, run.WidthFactor);
					clonedRuns.Add(run);
				}

				return new MTextLineLayout
				{
					Runs = clonedRuns,
					Alignment = alignment,
					Width = width,
					MaxHeight = clonedRuns.Count == 0 ? 0.0 : clonedRuns.Max(r => r.Height),
				};
			}

		private static bool isBreakableWhitespace(string token)
		{
			if (string.IsNullOrEmpty(token))
			{
				return false;
			}

			if (token.IndexOf('\u00A0') >= 0)
			{
				return false;
			}

			for (int i = 0; i < token.Length; i++)
			{
				if (!char.IsWhiteSpace(token[i]))
				{
					return false;
				}
			}
			return true;
		}

		private static IReadOnlyList<string> tokenize(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return Array.Empty<string>();
			}

			var tokens = new List<string>();
			var sb = new StringBuilder();
			bool? inWhitespace = null;
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				bool white = char.IsWhiteSpace(c) && c != '\u00A0';
				if (!inWhitespace.HasValue)
				{
					inWhitespace = white;
				}
				else if (inWhitespace.Value != white)
				{
					tokens.Add(sb.ToString());
					sb.Clear();
					inWhitespace = white;
				}
				sb.Append(c);
			}

			if (sb.Length > 0)
			{
				tokens.Add(sb.ToString());
			}

			return tokens;
		}

		private static void appendRun(List<MTextRun> runs, MTextRun prototype, string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			if (runs.Count > 0)
			{
				MTextRun last = runs[runs.Count - 1];
				if (last.CanMergeWith(prototype))
				{
					last.Text += text;
					return;
				}
			}

			runs.Add(new MTextRun
			{
				Text = text,
				FontName = prototype.FontName,
				Height = prototype.Height,
				WidthFactor = prototype.WidthFactor,
				ObliqueRad = prototype.ObliqueRad,
				Color = prototype.Color,
			});
		}

		private static void trimTrailingWhitespace(List<MTextRun> runs)
		{
			for (int i = runs.Count - 1; i >= 0; i--)
			{
				MTextRun run = runs[i];
				if (string.IsNullOrEmpty(run.Text))
				{
					runs.RemoveAt(i);
					continue;
				}

				string trimmed = run.Text.TrimEnd();
				if (trimmed.Length == 0)
				{
					runs.RemoveAt(i);
					continue;
				}

				if (trimmed.Length != run.Text.Length)
				{
					run.Text = trimmed;
				}
				break;
			}
		}

			private List<MTextLine> parseMText(string content, MTextState initialState)
			{
			var lines = new List<MTextLine>();
			var currentLine = new MTextLine
			{
				Runs = new List<MTextRun>(),
				Alignment = initialState.ParagraphAlignment,
			};
			var stack = new Stack<MTextState>();
			MTextState state = initialState;
			MTextState runState = state;
			var text = new StringBuilder();

			void flushText()
			{
				if (text.Length == 0)
				{
					return;
				}

				currentLine.Runs.Add(new MTextRun
				{
					Text = text.ToString(),
					FontName = runState.FontName,
					Height = runState.Height,
					WidthFactor = runState.WidthFactor,
					ObliqueRad = runState.ObliqueRad,
					Color = runState.Color,
				});
				text.Clear();
			}

			void appendChar(char c)
			{
				if (text.Length == 0)
				{
					runState = state;
				}
				text.Append(c);
			}

			void applyStateChange(Action action)
			{
				flushText();
				action();
			}

			void breakLine()
			{
				flushText();
				currentLine.Alignment = state.ParagraphAlignment;
				lines.Add(currentLine);
				currentLine = new MTextLine
				{
					Runs = new List<MTextRun>(),
					Alignment = state.ParagraphAlignment,
				};
			}

				for (int i = 0; i < content.Length; i++)
				{
					char c = content[i];
					if (c == '\\' && i + 1 < content.Length)
					{
						char cmd = content[i + 1];
						switch (cmd)
						{
						case '\\':
						case '{':
						case '}':
							appendChar(cmd);
							i++;
							continue;
						case 'P':
						case 'N':
						case 'n':
							breakLine();
							i++;
							continue;
						case '~':
							appendChar('\u00A0');
							i++;
							continue;
						case 'U':
							if (tryParseUnicode(content, i, out char unicode, out int consumed))
							{
								appendChar(unicode);
								i = consumed;
								continue;
							}
							break;
						case 'H':
						case 'W':
						case 'Q':
						case 'C':
						case 'c':
						case 'f':
						case 'F':
							case 'S':
							case 'A':
							case 'p':
								if (tryReadCommandValue(content, i + 2, out string value, out int endIndex))
								{
									if (cmd == 'S')
									{
										flushText();
										currentLine.Runs.Add(new MTextRun
										{
											Text = formatStack(value),
											FontName = state.FontName,
											Height = state.Height,
											WidthFactor = state.WidthFactor,
											ObliqueRad = state.ObliqueRad,
											Color = state.Color,
										});
										i = endIndex;
										continue;
									}

									applyStateChange(() => applyCommand(ref state, cmd, value));
									i = endIndex;
									continue;
								}
								break;
						case 'L':
						case 'l':
						case 'O':
						case 'o':
						case 'K':
						case 'k':
							i++;
							continue;
					}
				}

				if (c == '{')
				{
					flushText();
					stack.Push(state);
					continue;
				}

				if (c == '}')
				{
					flushText();
					if (stack.Count > 0)
					{
						state = stack.Pop();
					}
					continue;
				}

				appendChar(c);
			}

			flushText();
			currentLine.Alignment = state.ParagraphAlignment;
				lines.Add(currentLine);
				return lines;
			}

			private static string formatStack(string value)
			{
				if (string.IsNullOrEmpty(value))
				{
					return string.Empty;
				}

				// Approximate stacking by rendering inline.
				// ^ (tolerance) and # (diagonal) are approximated as a slash.
				return value.Replace('^', '/').Replace('#', '/');
			}

			private void applyCommand(ref MTextState state, char command, string value)
			{
				switch (command)
				{
				case 'H':
					if (tryParseHeight(value, out double h, out bool isFactor))
					{
						state.Height = isFactor ? state.Height * h : h;
					}
					break;
				case 'W':
					if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double w) && Math.Abs(w) > Epsilon)
					{
						state.WidthFactor = w;
					}
					break;
				case 'Q':
					if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double degrees))
					{
						state.ObliqueRad = MathHelper.DegToRad(degrees);
					}
					break;
					case 'C':
					case 'c':
						if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int aci))
						{
							if (aci <= 0 || aci == 256)
							{
								state.Color = null;
							}
							else if (aci > 0 && aci <= 255)
							{
								state.Color = new ACadSharp.Color((short)aci);
							}
						}
						break;
				case 'f':
				case 'F':
					state.FontName = this._fontResolver.ResolveRaw(value);
					break;
					case 'p':
						state.ParagraphAlignment = parseParagraphAlignment(value);
						break;
				}
			}

			private static HorizontalAlignment parseParagraphAlignment(string value)
			{
				if (string.IsNullOrEmpty(value))
				{
					return HorizontalAlignment.Left;
				}

				string v = value.ToLowerInvariant();
				if (v.Contains("xqc") || v.Contains("\\qc") || v.Contains("xqj"))
				{
					return HorizontalAlignment.Center;
				}
				if (v.Contains("xqr") || v.Contains("\\qr"))
				{
					return HorizontalAlignment.Right;
				}
				return HorizontalAlignment.Left;
			}

		private static bool tryParseHeight(string value, out double height, out bool isFactor)
		{
			height = 0.0;
			isFactor = false;
			if (string.IsNullOrWhiteSpace(value))
			{
				return false;
			}

			string raw = value.Trim();
			if (raw.EndsWith("x", StringComparison.OrdinalIgnoreCase))
			{
				isFactor = true;
				raw = raw.Substring(0, raw.Length - 1);
			}

			if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out height))
			{
				return false;
			}

			return height > Epsilon;
		}

		private static bool tryReadCommandValue(string content, int startIndex, out string value, out int endIndex)
		{
			value = string.Empty;
			endIndex = startIndex;
			if (startIndex >= content.Length)
			{
				return false;
			}

			int semicolon = content.IndexOf(';', startIndex);
			if (semicolon < 0)
			{
				return false;
			}

			value = content.Substring(startIndex, semicolon - startIndex);
			endIndex = semicolon;
			return true;
		}

		private static bool tryParseUnicode(string content, int slashIndex, out char unicode, out int consumed)
		{
			unicode = '\0';
			consumed = slashIndex;
			if (slashIndex + 6 >= content.Length)
			{
				return false;
			}

			if (content[slashIndex + 1] != 'U' || content[slashIndex + 2] != '+')
			{
				return false;
			}

			string hex = content.Substring(slashIndex + 3, 4);
			if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
			{
				return false;
			}

			unicode = (char)code;
			consumed = slashIndex + 6;
			return true;
		}

		private static TextAlignment toHorizontalAlignment(TextHorizontalAlignment alignment)
		{
			return alignment switch
			{
				TextHorizontalAlignment.Center => TextAlignment.Center,
				TextHorizontalAlignment.Right => TextAlignment.Right,
				TextHorizontalAlignment.Middle => TextAlignment.Center,
				_ => TextAlignment.Left,
			};
		}

		private static TextAlignment toTextAlignment(HorizontalAlignment alignment)
		{
			return alignment switch
			{
				HorizontalAlignment.Center => TextAlignment.Center,
				HorizontalAlignment.Right => TextAlignment.Right,
				_ => TextAlignment.Left,
			};
		}

		private readonly struct TextBasis
		{
			public double A { get; }
			public double B { get; }
			public double C { get; }
			public double D { get; }

			public TextBasis(double a, double b, double c, double d)
			{
				this.A = a;
				this.B = b;
				this.C = c;
				this.D = d;
			}
		}

		private enum HorizontalAlignment
		{
			Left = 0,
			Center = 1,
			Right = 2,
		}

		private sealed class MTextLine
		{
			public List<MTextRun> Runs { get; set; }
			public HorizontalAlignment Alignment { get; set; }
		}

		private sealed class MTextLineLayout
		{
			public List<MTextRun> Runs { get; set; }
			public HorizontalAlignment Alignment { get; set; }
			public double Width { get; set; }
			public double MaxHeight { get; set; }
			public double StartX { get; set; }
			public double BaselineY { get; set; }
			public double Advance { get; set; }
		}

		private sealed class MTextRun
		{
			public string Text { get; set; }
			public string FontName { get; set; }
			public double Height { get; set; }
			public double WidthFactor { get; set; }
			public double ObliqueRad { get; set; }
			public ACadSharp.Color? Color { get; set; }

			public bool CanMergeWith(MTextRun other)
			{
				if (other == null)
				{
					return false;
				}

				return string.Equals(this.FontName, other.FontName, StringComparison.Ordinal)
					&& Math.Abs(this.Height - other.Height) < 1e-12
					&& Math.Abs(this.WidthFactor - other.WidthFactor) < 1e-12
					&& Math.Abs(this.ObliqueRad - other.ObliqueRad) < 1e-12
					&& this.Color.Equals(other.Color);
			}
		}

		private struct MTextState
		{
			public string FontName;
			public double Height;
			public double WidthFactor;
			public double ObliqueRad;
			public ACadSharp.Color? Color;
			public HorizontalAlignment ParagraphAlignment;
		}

			private sealed class ApproximateTextMetrics
			{
				public double MeasureStringWidth(string text, string fontName, double height, double widthFactor)
				{
					if (string.IsNullOrEmpty(text) || height <= Epsilon)
					{
						return 0.0;
					}

					double avg = isMonospace(fontName) ? 0.60 : 0.55;
					double width = text.Length * avg * height;
					return width * Math.Abs(widthFactor);
				}

			public double GetAscent(double height)
			{
				return height;
			}

			public double GetDescent(double height)
			{
				return -0.25 * height;
			}

			private static bool isMonospace(string fontName)
			{
				if (string.IsNullOrEmpty(fontName))
				{
					return false;
				}

				string f = fontName.ToLowerInvariant();
				return f.Contains("mono") || f.Contains("courier") || f.Contains("consolas") || f.Contains("fixed");
			}
		}

		private sealed class FontResolver
		{
			private static readonly Dictionary<string, string> DefaultShxMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["txt.shx"] = "Arial",
				["simplex.shx"] = "Arial",
				["romans.shx"] = "Times New Roman",
				["romand.shx"] = "Times New Roman",
				["romanc.shx"] = "Times New Roman",
				["romant.shx"] = "Times New Roman",
				["isocp.shx"] = "Arial",
				["isocp2.shx"] = "Arial",
				["isocp3.shx"] = "Arial",
				["isoct.shx"] = "Arial",
				["isoct2.shx"] = "Arial",
				["monotxt.shx"] = "Courier New",
				["gothic.shx"] = "Century Gothic",
				["gothicg.shx"] = "Century Gothic",
				["gothice.shx"] = "Century Gothic",
				["syastro.shx"] = "Symbol",
				["symath.shx"] = "Symbol",
				["symap.shx"] = "Symbol",
				["symeteo.shx"] = "Symbol",
			};

			private readonly PdfConfiguration _configuration;

			public FontResolver(PdfConfiguration configuration)
			{
				this._configuration = configuration;
			}

			public string Resolve(TextStyle style)
			{
				if (style == null)
				{
					return "Arial";
				}

				string raw = style.Filename;
				if (string.IsNullOrWhiteSpace(raw))
				{
					raw = style.Name;
				}
				return ResolveRaw(raw);
			}

			public string ResolveRaw(string raw)
			{
				if (string.IsNullOrWhiteSpace(raw))
				{
					return "Arial";
				}

				string trimmed = raw.Trim();
				string candidate = trimmed.Split('|')[0];
				candidate = candidate.TrimStart('\\');
				string file = Path.GetFileName(candidate);
				if (string.IsNullOrWhiteSpace(file))
				{
					file = candidate;
				}

				if (tryResolveCustom(file, out string mapped))
				{
					return mapped;
				}

				if (file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
				{
					return Path.GetFileNameWithoutExtension(file);
				}

				if (DefaultShxMappings.TryGetValue(file, out mapped))
				{
					return mapped;
				}

				if (file.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
				{
					return "Arial";
				}

				return file;
			}

			private bool tryResolveCustom(string key, out string mapped)
			{
				mapped = null;
				if (this._configuration.ShxFontSubstitutions == null || this._configuration.ShxFontSubstitutions.Count == 0)
				{
					return false;
				}

				if (this._configuration.ShxFontSubstitutions.TryGetValue(key, out mapped))
				{
					return true;
				}

				string fileWithoutExt = Path.GetFileNameWithoutExtension(key);
				if (this._configuration.ShxFontSubstitutions.TryGetValue(fileWithoutExt, out mapped))
				{
					return true;
				}

				return false;
			}
		}
	}
}
