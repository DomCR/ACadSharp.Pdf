﻿namespace ACadSharp.Pdf.Core
{
	public static class PdfKey
	{
		public const string CommentSeparator = "%--------------------------------";

		//Graphics State Operators 

		/// <summary>
		///  Save the current graphics state on the graphics state stack (see 8.4.2, "Graphics State Stack").
		/// </summary>
		public const string StackStart = "q";

		/// <summary>
		/// Restore the graphics state by removing the most recently saved 
		/// state from the stack and making it the current state(see 8.4.2,
		/// "Graphics State Stack"). 
		/// </summary>
		public const string StackEnd = "Q";

		/// <summary>
		/// Modify the current transformation matrix (CTM) by concatenating 
		/// the specified matrix(see 8.3.2, "Coordinate Spaces"). Although the
		/// operands specify a matrix, they shall be written as six separate
		/// numbers, not as an array.
		/// </summary>
		public const string CurrentMatrix = "cm";

		/// <summary>
		/// Set the line width in the graphics state (see 8.4.3.2, "Line Width"). 
		/// </summary>
		public const string LineWidth = "w";

		//Path Construction Operators

		/// <summary>
		/// Append a cubic Bézier curve to the current path. The curve 
		/// shall extend from the current point to the point(x3 , y3 ), using 
		/// (x1, y1) and(x2 , y2 ) as the Bézier control points(see 8.5.2.2,
		/// "Cubic Bézier Curves"). The new current point shall be
		/// (x3 , y3 ). 
		/// </summary>
		public const string Arc = "c";

		/// <summary>
		/// Begin a new subpath by moving the current point to 
		/// coordinates(x, y), omitting any connecting line segment.If
		/// the previous path construction operator in the current path
		/// was also m, the new m overrides it; no vestige of the
		/// previous m operation remains in the path.
		/// </summary>
		public const string BeginPath = "m";

		public const string Line = "l";

		/// <summary>
		/// Append a rectangle to the current path as a complete 
		/// subpath, with lower-left corner(x, y) and dimensions width
		/// and height in user space.The operation
		/// x y width height re
		/// is equivalent to
		/// x y m
		/// (x + width ) y l
		/// (x + width ) (y + height ) l
		/// x(y + height ) l
		/// h
		/// </summary>
		public const string Rectangle = "re";

		//Path-Painting Operators

		/// <summary>
		/// Fill the path, using the nonzero winding number rule to determine the region 
		/// to fill(see 8.5.3.3.2, "Nonzero Winding Number Rule"). Any subpaths that
		/// are open shall be implicitly closed before being filled.
		/// </summary>
		public const string Fill = "f";

		/// <summary>
		/// Close and Stroke the path.
		/// </summary>
		public const string Stroke = "S";

		public const string TypeFont = "Tf";

		/// <summary>
		/// Move to the start of the next line, offset from the start of the current line by 
		/// (tx, ty). tx and ty shall denote numbers expressed in unscaled text space
		/// units. More precisely, this operator shall perform these assignments.
		/// </summary>
		public const string TextTranslation = "Td";

		/// <summary>
		/// Move to the start of the next line, offset from the start of the current line by 
		/// (tx, ty). As a side effect, this operator shall set the leading parameter in 
		/// the text state.This operator shall have the same effect as this code: 
		/// </summary>
		public const string TextNextLineTranslation = "TD";

		/// <summary>
		/// Set the text matrix, Tm , and the text line matrix, Tlm.
		/// The operands shall all be numbers, and the initial value for Tm and Tlm
		/// shall be the identity matrix, [1 0 0 1 0 0]. Although the operands
		/// specify a matrix, they shall be passed to Tm as six separate numbers, not 
		/// as an array. <br/>
		/// The matrix specified by the operands shall not be concatenated onto the
		/// current text matrix, but shall replace it. 
		/// </summary>
		public const string TextMatrix = "Tm";

		public const string BasicTextStart = "BT";
		public const string BasicTextEnd = "ET";

		//Clipping Path Operators

		/// <summary>
		/// Modify the current clipping path by intersecting it with the current path, using 
		/// the nonzero winding number rule to determine which regions lie inside the
		/// clipping path.
		/// </summary>
		public const string ClippingPath = "W";


		//Not classified

		public const string StreamStart = "stream";
		public const string StreamEnd = "endstream";
		public const string EndObj = "endobj";

		public const string StartXref = "startxref";
	}
}
