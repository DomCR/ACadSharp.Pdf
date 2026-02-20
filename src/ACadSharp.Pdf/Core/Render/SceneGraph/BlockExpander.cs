using ACadSharp.Entities;
using ACadSharp.Pdf.Core.Render.Transforms;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections.Generic;

namespace ACadSharp.Pdf.Core.Render.SceneGraph
{
	internal sealed class BlockExpander
	{
		private readonly RenderLog _log;
		private readonly int _maxDepth;

		public BlockExpander(RenderLog log, int maxDepth = 64)
		{
			this._log = log ?? throw new ArgumentNullException(nameof(log));
			this._maxDepth = Math.Max(1, maxDepth);
		}

		public bool TryEnter(Insert insert, int depth, ISet<string> activeBlocks, out BlockRecord block, out string blockKey)
		{
			block = insert?.Block;
			blockKey = string.Empty;

			if (insert == null)
			{
				return false;
			}

			if (block == null)
			{
				this._log.Add(insert.Handle, insert.SubclassMarker, RenderStatus.Skipped, "INSERT has no block reference.");
				return false;
			}

			if (depth > this._maxDepth)
			{
				this._log.Add(insert.Handle, insert.SubclassMarker, RenderStatus.Error, $"INSERT recursion depth exceeded ({this._maxDepth}).");
				return false;
			}

			blockKey = getBlockKey(block);
			if (activeBlocks.Contains(blockKey))
			{
				this._log.Add(insert.Handle, insert.SubclassMarker, RenderStatus.Error, $"Circular INSERT detected for block '{block.Name}'.");
				return false;
			}

			activeBlocks.Add(blockKey);
			return true;
		}

		public void Leave(string blockKey, ISet<string> activeBlocks)
		{
			if (string.IsNullOrEmpty(blockKey) || activeBlocks == null)
			{
				return;
			}

			activeBlocks.Remove(blockKey);
		}

		public IReadOnlyList<Matrix4> ComputeCellTransforms(Insert insert, Matrix4 parentTransform)
		{
			if (insert == null || insert.Block == null)
			{
				return Array.Empty<Matrix4>();
			}

			double sx = insert.XScale;
			double sy = insert.YScale;
			double sz = insert.ZScale;
			if (isNearlyZero(sx) || isNearlyZero(sy) || isNearlyZero(sz))
			{
				this._log.Add(insert.Handle, insert.SubclassMarker, RenderStatus.Skipped, "INSERT has degenerate scale.");
				return Array.Empty<Matrix4>();
			}

			XYZ basePoint = insert.Block.BlockEntity?.BasePoint ?? XYZ.Zero;
			Matrix4 toBase = Matrix4.CreateTranslation(-basePoint);
			Matrix4 scale = Matrix4.CreateScale(new XYZ(sx, sy, sz));
			Matrix4 rotate = Matrix4.CreateFromAxisAngle(XYZ.AxisZ, normalizeAngle(insert.Rotation));
			Matrix4 toInsert = Matrix4.CreateTranslation(insert.InsertPoint);
			Matrix4 ocsToWcs = TransformHelper.OcsToWcs(insert.Normal);

			int cols = Math.Max(1, (int)insert.ColumnCount);
			int rows = Math.Max(1, (int)insert.RowCount);

			const int maxCells = 10_000;
			long totalCells = (long)rows * (long)cols;
			if (totalCells > maxCells)
			{
				this._log.Add(insert.Handle, insert.SubclassMarker, RenderStatus.Error, $"MINSERT grid too large ({rows}x{cols}); skipping expansion.");
				return Array.Empty<Matrix4>();
			}

			var transforms = new List<Matrix4>((int)totalCells);

			for (int row = 0; row < rows; row++)
			{
				for (int col = 0; col < cols; col++)
				{
					double offsetX = col * insert.ColumnSpacing;
					double offsetY = row * insert.RowSpacing;
					Matrix4 gridOffset = Matrix4.CreateTranslation(offsetX, offsetY, 0.0);

					// Grid spacing should scale/rotate with the INSERT (spacing is specified in INSERT-local units).
					Matrix4 local = ocsToWcs * toInsert * rotate * scale * gridOffset * toBase;
					transforms.Add(parentTransform * local);
				}
			}

			return transforms;
		}

		public double ComputeInsertScaleFactor(Insert insert)
		{
			if (insert == null)
			{
				return 1.0;
			}

			double sx = Math.Abs(insert.XScale);
			double sy = Math.Abs(insert.YScale);
			double s = 0.5 * (sx + sy);
			if (isNearlyZero(s))
			{
				return 1.0;
			}

			return s;
		}

		private static string getBlockKey(BlockRecord block)
		{
			if (block == null)
			{
				return string.Empty;
			}

			if (!string.IsNullOrWhiteSpace(block.Name))
			{
				return block.Name;
			}

			return "#" + block.Handle.ToString();
		}

		private static bool isNearlyZero(double value)
		{
			return Math.Abs(value) < 1e-12;
		}

		private static double normalizeAngle(double angle)
		{
			double twoPi = Math.PI * 2.0;
			double norm = angle % twoPi;
			if (norm < 0)
			{
				norm += twoPi;
			}

			return norm;
		}
	}
}
