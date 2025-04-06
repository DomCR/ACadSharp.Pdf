using CSUtilities;
using System;
using System.IO;

namespace ACadSharp.Pdf.Tests
{
	public static class TestVariables
	{
		public static string DesktopFolder { get { return Environment.GetFolderPath(Environment.SpecialFolder.Desktop); } }

		public static string SamplesFolder { get { return EnvironmentVars.Get<string>("SAMPLES_FOLDER"); } }

		public static string OutputSamplesFolder { get { return EnvironmentVars.Get<string>("OUTPUT_SAMPLES_FOLDER"); } }

		public static bool LocalEnv { get { return EnvironmentVars.Get<bool>("LOCAL_ENV"); } }

		static TestVariables()
		{
			EnvironmentVars.SetIfNull("SAMPLES_FOLDER", "../../../../../samples/");
			EnvironmentVars.SetIfNull("OUTPUT_SAMPLES_FOLDER", "../../../../../samples/out");
			EnvironmentVars.SetIfNull("LOCAL_ENV", "true");
		}

		public static void CreateOutputFolders()
		{
			craateFolderIfDoesNotExist(OutputSamplesFolder);

			string outputSamplesFolder = OutputSamplesFolder;

#if NETFRAMEWORK
			string curr = AppDomain.CurrentDomain.BaseDirectory;
			outputSamplesFolder = Path.GetFullPath(Path.Combine(curr, OutputSamplesFolder));
#endif

			craateFolderIfDoesNotExist(outputSamplesFolder);
		}

		private static void craateFolderIfDoesNotExist(string path)
		{
			if (!Directory.Exists(path))
			{
				Directory.CreateDirectory(path);
			}
		}
	}
}
