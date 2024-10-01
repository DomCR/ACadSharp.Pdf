using ACadSharp.IO;
using System;
using System.Collections.Generic;

namespace ACadSharp.Pdf
{
	public class PdfConfiguration
	{
		public static readonly IReadOnlyDictionary<LineWeightType, double> LineWeightDefaultValues =
		new Dictionary<LineWeightType, double>()
		{
			{ LineWeightType.Default, 0 },
			{ LineWeightType.W0, 0.001 },
			{ LineWeightType.W5, 0.05 },
			{ LineWeightType.W9, 0.09 },
			{ LineWeightType.W13, 0.13 },
			{ LineWeightType.W15, 0.15 },
			{ LineWeightType.W18, 0.18 },
			{ LineWeightType.W20, 0.20 },
			{ LineWeightType.W25, 0.25 },
			{ LineWeightType.W30, 0.30 },
			{ LineWeightType.W35, 0.35 },
			{ LineWeightType.W40, 0.40 },
			{ LineWeightType.W50, 0.50 },
			{ LineWeightType.W53, 0.53 },
			{ LineWeightType.W60, 0.60 },
			{ LineWeightType.W70, 0.70 },
			{ LineWeightType.W80, 0.80 },
			{ LineWeightType.W90, 0.90 },
			{ LineWeightType.W100, 1.00 },
			{ LineWeightType.W106, 1.06 },
			{ LineWeightType.W120, 1.20 },
			{ LineWeightType.W140, 1.40 },
			{ LineWeightType.W158, 1.58 },
			{ LineWeightType.W200, 2.00 },
			{ LineWeightType.W211, 2.11 },
		};

		/// <summary>
		/// Notification event to get information about the export process.
		/// </summary>
		/// <remarks>
		/// The notification system informs about any issue or non critical errors during the export.
		/// </remarks>
		public event NotificationEventHandler OnNotification;

		/// <summary>
		/// Set the dot size.
		/// </summary>
		/// <remarks>
		/// The units used to draw the points are the same as the paper.
		/// </remarks>
		public double DotSize { get; set; } = 0.1d;

		public ushort ArcPrecision { get; set; } = 100;

		public short DotShape { get; set; }

		public string DecimalFormat { get; set; } = "0.####";

		public CadDocument ReferenceDocument { get; set; }

		public Dictionary<LineWeightType, double> LineWeightValues { get; set; } = new();

		public double GetLineWeightValue(LineWeightType lineWeight)
		{
			double value = 0.0d;
			if (this.LineWeightValues.TryGetValue(lineWeight, out value)
				|| LineWeightDefaultValues.TryGetValue(lineWeight, out value))
			{
				return value;
			}

			return value;
		}

		internal void Notify(string message, NotificationType notificationType, Exception ex = null)
		{
			this.OnNotification?.Invoke(this, new NotificationEventArgs(message, notificationType, ex));
		}
	}
}
