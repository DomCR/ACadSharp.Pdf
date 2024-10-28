using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TestFramework("ACadSharp.Pdf.Tests.TestSetup", "ACadSharp.Pdf.Tests")]

namespace ACadSharp.Pdf.Tests
{
	public sealed class TestSetup : XunitTestFramework
	{
		public TestSetup(IMessageSink messageSink)
		  : base(messageSink)
		{
			this.init();
		}

		private void init()
		{
			TestVariables.CreateOutputFolders();
		}
	}
}