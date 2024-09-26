using System;
using System.IO;
using System.Runtime.Intrinsics.X86;

namespace ACadSharp.Pdf
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

		/// <summary>
		/// Stroke the path.
		/// </summary>
		public const string Stroke = "S";

		/// <summary>
		/// 
		/// </summary>
		public const string CloseStroke = "s";

		//Path Construction Operators

		/// <summary>
		/// Begin a new subpath by moving the current point to 
		/// coordinates(x, y), omitting any connecting line segment.If
		/// the previous path construction operator in the current path
		/// was also m, the new m overrides it; no vestige of the
		/// previous m operation remains in the path.
		/// </summary>
		public const string BeginPath = "m";

		public const string Line = "l";

		//Not classified
		public const string Arc = "c";
		public const string Fill = "f";
		public const string StreamStart = "stream";
		public const string StreamEnd = "endstream";
		public const string EndObj = "endobj";
	}
}
