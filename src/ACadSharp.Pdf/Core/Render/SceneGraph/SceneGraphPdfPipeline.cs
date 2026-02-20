using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Pdf.Core.Render.Flattening;
using ACadSharp.Pdf.Core.Render.Pdf;
using ACadSharp.Pdf.Core.Render.Style;
using System;
using System.Collections.Generic;

namespace ACadSharp.Pdf.Core.Render.SceneGraph
{
	internal sealed class SceneGraphPdfPipeline
	{
		private readonly Layout _layout;
		private readonly PdfConfiguration _configuration;

		public SceneGraphPdfPipeline(Layout layout, PdfConfiguration configuration)
		{
			this._layout = layout ?? throw new ArgumentNullException(nameof(layout));
			this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		}

		public string Render(IReadOnlyList<Viewport> viewports, IReadOnlyList<Entity> paperEntities, out RenderLog log)
		{
			log = new RenderLog();

			var resolver = new PropertyResolver(this._configuration, log);
			var builder = new SceneGraphBuilder(this._layout, this._configuration, resolver, log);
			var nodes = builder.Build(viewports, paperEntities);

			var flattener = new SceneFlattener(this._layout);
			var commands = flattener.Flatten(nodes);

			var backend = new PdfRenderBackend(this._configuration);
			return backend.Serialize(commands);
		}
	}
}
