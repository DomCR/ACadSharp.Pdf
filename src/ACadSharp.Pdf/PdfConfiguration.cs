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
		};

		/// <summary>
		/// Notification event to get information about the export process.
		/// </summary>
		/// <remarks>
		/// The notification system informs about any issue or non critical errors during the export.
		/// </remarks>
		public event NotificationEventHandler OnNotification;

		public CadDocument ReferenceDocument { get; set; }

		public Dictionary<LineWeightType, double> LineWeightValues { get; set; } = new();

		internal void Notify(string message, NotificationType notificationType, Exception ex = null)
		{
			this.OnNotification?.Invoke(this, new NotificationEventArgs(message, notificationType, ex));
		}
	}
}
