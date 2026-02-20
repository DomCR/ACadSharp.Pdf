using System;
using System.Collections.Generic;

namespace ACadSharp.Pdf.Core.Render
{
	public enum RenderStatus
	{
		Rendered = 0,
		Skipped = 1,
		NotImplemented = 2,
		Error = 3,
	}

	public sealed class RenderLogEntry
	{
		public ulong Handle { get; }
		public string EntityType { get; }
		public RenderStatus Status { get; }
		public string Reason { get; }

		public RenderLogEntry(ulong handle, string entityType, RenderStatus status, string reason)
		{
			this.Handle = handle;
			this.EntityType = entityType ?? string.Empty;
			this.Status = status;
			this.Reason = reason ?? string.Empty;
		}
	}

	public sealed class RenderLog
	{
		private readonly List<RenderLogEntry> _entries = new();
		private readonly Dictionary<ulong, int> _indexByHandle = new();

		public IReadOnlyList<RenderLogEntry> Entries => this._entries;

		public void Add(ulong handle, string entityType, RenderStatus status, string reason)
		{
			if (this._indexByHandle.TryGetValue(handle, out int idx))
			{
				var existing = this._entries[idx];

				RenderStatus mergedStatus = moreSevere(existing.Status, status);
				string mergedReason = mergeReason(existing.Reason, reason);

				this._entries[idx] = new RenderLogEntry(handle, entityType, mergedStatus, mergedReason);
				return;
			}

			this._indexByHandle[handle] = this._entries.Count;
			this._entries.Add(new RenderLogEntry(handle, entityType, status, reason));
		}

		public void Clear()
		{
			this._entries.Clear();
			this._indexByHandle.Clear();
		}

		private static RenderStatus moreSevere(RenderStatus a, RenderStatus b)
		{
			// Higher enum value is not guaranteed to mean more severe; define explicit ordering.
			int sa = severity(a);
			int sb = severity(b);
			return sa >= sb ? a : b;
		}

		private static int severity(RenderStatus s)
		{
			switch (s)
			{
				case RenderStatus.Error:
					return 3;
				case RenderStatus.NotImplemented:
					return 2;
				case RenderStatus.Skipped:
					return 1;
				case RenderStatus.Rendered:
				default:
					return 0;
			}
		}

		private static string mergeReason(string a, string b)
		{
			a = a ?? string.Empty;
			b = b ?? string.Empty;

			if (string.IsNullOrWhiteSpace(a))
			{
				return b;
			}

			if (string.IsNullOrWhiteSpace(b))
			{
				return a;
			}

			if (a.IndexOf(b, StringComparison.Ordinal) >= 0)
			{
				return a;
			}

			return a + " | " + b;
		}
	}
}
