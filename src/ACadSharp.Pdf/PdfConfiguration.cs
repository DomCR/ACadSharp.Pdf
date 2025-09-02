using ACadSharp.IO;
using System;
using System.Collections.Generic;

namespace ACadSharp.Pdf
{
	public class PdfConfiguration
	{
		/// <summary>
		/// Line weight default values.
		/// </summary>
		public static readonly IReadOnlyDictionary<LineweightType, double> LineWeightDefaultValues =
			new Dictionary<LineweightType, double>()
		{
			{ LineweightType.Default, 0 },
			{ LineweightType.W0, 0.001 },
			{ LineweightType.W5, 0.05 },
			{ LineweightType.W9, 0.09 },
			{ LineweightType.W13, 0.13 },
			{ LineweightType.W15, 0.15 },
			{ LineweightType.W18, 0.18 },
			{ LineweightType.W20, 0.20 },
			{ LineweightType.W25, 0.25 },
			{ LineweightType.W30, 0.30 },
			{ LineweightType.W35, 0.35 },
			{ LineweightType.W40, 0.40 },
			{ LineweightType.W50, 0.50 },
			{ LineweightType.W53, 0.53 },
			{ LineweightType.W60, 0.60 },
			{ LineweightType.W70, 0.70 },
			{ LineweightType.W80, 0.80 },
			{ LineweightType.W90, 0.90 },
			{ LineweightType.W100, 1.00 },
			{ LineweightType.W106, 1.06 },
			{ LineweightType.W120, 1.20 },
			{ LineweightType.W140, 1.40 },
			{ LineweightType.W158, 1.58 },
			{ LineweightType.W200, 2.00 },
			{ LineweightType.W211, 2.11 },
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
		public double DotSize { get; set; } = 0.01d;

		/// <summary>
		/// Number of divisions performed in the arcs when drawing the shape.
		/// </summary>
		public ushort ArcPrecision { get; set; } = 256;

		/// <summary>
		/// Decimal format and precision set for the pdf file.
		/// </summary>
		public string DecimalFormat { get; set; } = "0.####";

		public Dictionary<LineweightType, double> LineWeightValues { get; set; } = new();

		public double GetLineWeightValue(LineweightType lineWeight)
		{
			double value = 0.0d;
			if (LineWeightDefaultValues.TryGetValue(lineWeight, out value))
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
