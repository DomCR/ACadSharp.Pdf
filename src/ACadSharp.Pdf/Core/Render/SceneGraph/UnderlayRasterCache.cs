using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace ACadSharp.Pdf.Core.Render.SceneGraph
{
	internal sealed class UnderlayRasterCache
	{
		internal sealed class CachedRaster
		{
			public byte[] Rgb24Data { get; }
			public int Width { get; }
			public int Height { get; }

			public CachedRaster(byte[] rgb24Data, int width, int height)
			{
				this.Rgb24Data = rgb24Data ?? throw new ArgumentNullException(nameof(rgb24Data));
				this.Width = width;
				this.Height = height;
			}
		}

		private enum RasterKind : byte
		{
			Image = 1,
			PdfPage = 2,
		}

		private sealed class CacheEntry
		{
			public CacheKey Key { get; }
			public CachedRaster Raster { get; }
			public long SizeBytes { get; }

			public CacheEntry(CacheKey key, CachedRaster raster, long sizeBytes)
			{
				this.Key = key;
				this.Raster = raster;
				this.SizeBytes = sizeBytes;
			}
		}

		private struct CacheKey : IEquatable<CacheKey>
		{
			public RasterKind Kind { get; }
			public string Path { get; }
			public int PageIndex { get; }
			public int Dpi { get; }

			public CacheKey(RasterKind kind, string path, int pageIndex, int dpi)
			{
				this.Kind = kind;
				this.Path = path ?? string.Empty;
				this.PageIndex = pageIndex;
				this.Dpi = dpi;
			}

			public bool Equals(CacheKey other)
			{
				return this.Kind == other.Kind
					&& this.PageIndex == other.PageIndex
					&& this.Dpi == other.Dpi
					&& string.Equals(this.Path, other.Path, StringComparison.OrdinalIgnoreCase);
			}

			public override bool Equals(object obj)
			{
				return obj is CacheKey key && Equals(key);
			}

			public override int GetHashCode()
			{
				unchecked
				{
					int hash = (int)this.Kind;
					hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(this.Path);
					hash = (hash * 397) ^ this.PageIndex;
					hash = (hash * 397) ^ this.Dpi;
					return hash;
				}
			}
		}

		private readonly PdfConfiguration _configuration;
		private readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> _entries = new Dictionary<CacheKey, LinkedListNode<CacheEntry>>();
		private readonly LinkedList<CacheEntry> _lru = new LinkedList<CacheEntry>();
		private long _cacheBytes;

		public UnderlayRasterCache(PdfConfiguration configuration)
		{
			this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		}

		public bool TryLoadRasterImage(string referencedPath, out CachedRaster raster, out string resolvedPath, out string reason)
		{
			raster = null;
			resolvedPath = null;
			reason = null;

			if (!tryResolvePath(referencedPath, out resolvedPath, out reason))
			{
				return false;
			}

			CacheKey key = new CacheKey(RasterKind.Image, resolvedPath, 0, 0);
			if (tryGet(key, out raster))
			{
				return true;
			}

			try
			{
				using (SKBitmap bitmap = SKBitmap.Decode(resolvedPath))
				{
					if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
					{
						reason = $"Image decode failed: {resolvedPath}";
						return false;
					}

					int maxDim = getMaxRasterDimension();
					using (SKBitmap scaled = scaleDownIfNeeded(bitmap, maxDim))
					{
						SKBitmap source = scaled ?? bitmap;
						byte[] rgb24 = toRgb24(source);
						raster = new CachedRaster(rgb24, source.Width, source.Height);
					}
					addToCache(key, raster);
					return true;
				}
			}
			catch (Exception ex)
			{
				reason = $"Image load failed: {ex.Message}";
				return false;
			}
		}

		public bool TryRasterizePdf(string referencedPath, int pageIndex, int dpi, out CachedRaster raster, out string resolvedPath, out string reason)
		{
			raster = null;
			resolvedPath = null;
			reason = null;

			if (!tryResolvePath(referencedPath, out resolvedPath, out reason))
			{
				return false;
			}

			int requestedDpi = clamp(dpi, 72, 600);
			if (pageIndex < 0)
			{
				pageIndex = 0;
			}

			int effectiveDpi = requestedDpi;
			double effectiveScale = requestedDpi / 72.0;
			int maxDim = getMaxRasterDimension();

			try
			{
				using (var probe = DocLib.Instance.GetDocReader(resolvedPath, new PageDimensions(1.0)))
				{
					int pageCount = probe.GetPageCount();
					if (pageCount <= 0)
					{
						reason = $"PDF has no pages: {resolvedPath}";
						return false;
					}

					if (pageIndex >= pageCount)
					{
						reason = $"PDF page {pageIndex + 1} out of range (pages: {pageCount}).";
						return false;
					}

					using (var page = probe.GetPageReader(pageIndex))
					{
						int baseW = page.GetPageWidth();
						int baseH = page.GetPageHeight();
						if (baseW <= 0 || baseH <= 0)
						{
							reason = $"Rasterized PDF page has invalid base size ({baseW}x{baseH}).";
							return false;
						}

						int maxBase = Math.Max(baseW, baseH);
						if (maxDim > 0)
						{
							double maxScaleByDim = (double)maxDim / maxBase;
							if (effectiveScale > maxScaleByDim)
							{
								effectiveScale = maxScaleByDim;
							}
						}

						if (effectiveScale <= 0)
						{
							effectiveScale = 1.0;
						}

						effectiveDpi = Math.Max(1, (int)Math.Round(72.0 * effectiveScale));
					}
				}
			}
			catch (Exception ex)
			{
				reason = $"PDF probe failed: {ex.Message}";
				return false;
			}

			CacheKey key = new CacheKey(RasterKind.PdfPage, resolvedPath, pageIndex, effectiveDpi);
			if (tryGet(key, out raster))
			{
				return true;
			}

			try
			{
				using (var docReader = DocLib.Instance.GetDocReader(resolvedPath, new PageDimensions(effectiveScale)))
				{
					int pageCount = docReader.GetPageCount();
					if (pageCount <= 0)
					{
						reason = $"PDF has no pages: {resolvedPath}";
						return false;
					}

					if (pageIndex >= pageCount)
					{
						reason = $"PDF page {pageIndex + 1} out of range (pages: {pageCount}).";
						return false;
					}

					using (var pageReader = docReader.GetPageReader(pageIndex))
					{
						int width = pageReader.GetPageWidth();
						int height = pageReader.GetPageHeight();
						if (width <= 0 || height <= 0)
						{
							reason = $"Rasterized PDF page has invalid size ({width}x{height}).";
							return false;
						}

						byte[] bgra = pageReader.GetImage();
						if (bgra == null || bgra.Length < width * height * 4)
						{
							reason = "PDF rasterizer returned invalid pixel buffer.";
							return false;
						}

						byte[] rgb24 = bgraToRgb24(bgra, width, height);
						raster = new CachedRaster(rgb24, width, height);
						addToCache(key, raster);
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				reason = $"PDF rasterization failed: {ex.Message}";
				return false;
			}
		}

		public static byte[] ApplyRasterImageAdjustments(byte[] rgb24, byte brightness, byte contrast, byte fade)
		{
			if (rgb24 == null || rgb24.Length == 0)
			{
				return rgb24;
			}

			if (brightness == 50 && contrast == 50 && fade == 0)
			{
				return rgb24;
			}

			double brightnessDelta = (brightness - 50.0) / 50.0;
			double contrastFactor = contrast / 50.0;
			double fadeFactor = fade / 100.0;

			byte[] adjusted = new byte[rgb24.Length];
			for (int i = 0; i < rgb24.Length; i += 3)
			{
				adjusted[i + 0] = adjustChannel(rgb24[i + 0], brightnessDelta, contrastFactor, fadeFactor);
				adjusted[i + 1] = adjustChannel(rgb24[i + 1], brightnessDelta, contrastFactor, fadeFactor);
				adjusted[i + 2] = adjustChannel(rgb24[i + 2], brightnessDelta, contrastFactor, fadeFactor);
			}

			return adjusted;
		}

		public static byte[] ApplyUnderlayAdjustments(byte[] rgb24, byte contrast, byte fade, bool monochrome)
		{
			if (rgb24 == null || rgb24.Length == 0)
			{
				return rgb24;
			}

			bool needsAdjust = !(contrast == 100 && fade == 0);

			byte[] working = rgb24;
			if (monochrome)
			{
				working = toMonochromeRgb24(working);
			}

			if (!needsAdjust)
			{
				return working;
			}

			// Underlay contrast: 0-100, where 100 is neutral.
			double contrastFactor = contrast / 100.0;
			double fadeFactor = fade / 100.0;

			byte[] adjusted = new byte[working.Length];
			for (int i = 0; i < working.Length; i += 3)
			{
				adjusted[i + 0] = adjustChannel(working[i + 0], brightnessDelta: 0.0, contrastFactor: contrastFactor, fadeFactor: fadeFactor);
				adjusted[i + 1] = adjustChannel(working[i + 1], brightnessDelta: 0.0, contrastFactor: contrastFactor, fadeFactor: fadeFactor);
				adjusted[i + 2] = adjustChannel(working[i + 2], brightnessDelta: 0.0, contrastFactor: contrastFactor, fadeFactor: fadeFactor);
			}

			return adjusted;
		}

		private static byte adjustChannel(byte value, double brightnessDelta, double contrastFactor, double fadeFactor)
		{
			double normalized = value / 255.0;
			normalized = (normalized - 0.5) * contrastFactor + 0.5;
			normalized += brightnessDelta;
			normalized = normalized * (1.0 - fadeFactor) + fadeFactor;
			normalized = clamp(normalized, 0.0, 1.0);
			return (byte)(normalized * 255.0);
		}

		private bool tryResolvePath(string referencedPath, out string resolvedPath, out string reason)
		{
			resolvedPath = null;
			reason = null;

			string normalized = normalizePathValue(referencedPath);
			if (string.IsNullOrWhiteSpace(normalized))
			{
				reason = "External file path is empty.";
				return false;
			}

			if (Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri)
				&& (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
			{
				reason = $"External URL references are not supported: {normalized}";
				return false;
			}

			List<string> candidates = new List<string>();
			addOverrideCandidates(candidates, normalized);

			if (Path.IsPathRooted(normalized))
			{
				candidates.Add(normalized);
			}
			else
			{
				string basePath = this._configuration.BasePath;
				if (!string.IsNullOrWhiteSpace(basePath))
				{
					candidates.Add(Path.Combine(basePath, normalized));
				}

				candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), normalized));
				candidates.Add(normalized);

				string fileName = Path.GetFileName(normalized);
				if (!string.IsNullOrWhiteSpace(fileName))
				{
					if (!string.IsNullOrWhiteSpace(basePath))
					{
						candidates.Add(Path.Combine(basePath, fileName));
					}

					candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), fileName));
				}
			}

			foreach (string candidate in candidates)
			{
				if (string.IsNullOrWhiteSpace(candidate))
				{
					continue;
				}

				string fullPath;
				try
				{
					fullPath = Path.GetFullPath(candidate);
				}
				catch
				{
					continue;
				}

				if (File.Exists(fullPath))
				{
					resolvedPath = fullPath;
					return true;
				}
			}

			reason = $"External file not found: {normalized}";
			return false;
		}

		private void addOverrideCandidates(List<string> candidates, string normalized)
		{
			if (candidates == null)
			{
				return;
			}

			var overrides = this._configuration.ImagePathOverrides;
			if (overrides == null || overrides.Count == 0)
			{
				return;
			}

			if (overrides.TryGetValue(normalized, out string mapped) && !string.IsNullOrWhiteSpace(mapped))
			{
				candidates.Add(normalizePathValue(mapped));
			}

			string fileName = Path.GetFileName(normalized);
			if (!string.IsNullOrWhiteSpace(fileName)
				&& overrides.TryGetValue(fileName, out mapped)
				&& !string.IsNullOrWhiteSpace(mapped))
			{
				candidates.Add(normalizePathValue(mapped));
			}
		}

		private static string normalizePathValue(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return string.Empty;
			}

			string normalized = value.Trim();
			if (normalized.Length >= 2
				&& ((normalized.StartsWith("\"", StringComparison.Ordinal) && normalized.EndsWith("\"", StringComparison.Ordinal))
					|| (normalized.StartsWith("'", StringComparison.Ordinal) && normalized.EndsWith("'", StringComparison.Ordinal))))
			{
				normalized = normalized.Substring(1, normalized.Length - 2);
			}

			normalized = normalized.Replace('\\', Path.DirectorySeparatorChar);
			normalized = normalized.Replace('/', Path.DirectorySeparatorChar);
			return normalized.Trim();
		}

		private bool tryGet(CacheKey key, out CachedRaster raster)
		{
			raster = null;
			if (!this._entries.TryGetValue(key, out LinkedListNode<CacheEntry> node))
			{
				return false;
			}

			this._lru.Remove(node);
			this._lru.AddFirst(node);
			raster = node.Value.Raster;
			return true;
		}

		private void addToCache(CacheKey key, CachedRaster raster)
		{
			if (raster == null || raster.Rgb24Data == null)
			{
				return;
			}

			long sizeBytes = raster.Rgb24Data.LongLength;
			if (sizeBytes <= 0)
			{
				return;
			}

			long maxBytes = getMaxCacheBytes();
			if (sizeBytes > maxBytes)
			{
				// Too large to cache; return uncached without evicting the whole cache.
				return;
			}

			while (this._cacheBytes + sizeBytes > maxBytes && this._lru.Count > 0)
			{
				LinkedListNode<CacheEntry> last = this._lru.Last;
				this._lru.RemoveLast();
				this._entries.Remove(last.Value.Key);
				this._cacheBytes -= last.Value.SizeBytes;
			}

			CacheEntry entry = new CacheEntry(key, raster, sizeBytes);
			LinkedListNode<CacheEntry> node = this._lru.AddFirst(entry);
			this._entries[key] = node;
			this._cacheBytes += sizeBytes;
		}

		private long getMaxCacheBytes()
		{
			int mb = this._configuration.MaxImageCacheMemoryMB;
			if (mb <= 0)
			{
				mb = 1;
			}

			return (long)mb * 1024L * 1024L;
		}

		private int getMaxRasterDimension()
		{
			int dim = this._configuration.MaxRasterPixelDimension;
			if (dim <= 0)
			{
				return 0;
			}

			if (dim < 16)
			{
				return 16;
			}

			return dim;
		}

		private static SKBitmap scaleDownIfNeeded(SKBitmap bitmap, int maxDim)
		{
			if (bitmap == null || maxDim <= 0)
			{
				return null;
			}

			int maxSide = Math.Max(bitmap.Width, bitmap.Height);
			if (maxSide <= maxDim)
			{
				return null;
			}

			double scale = (double)maxDim / maxSide;
			int newW = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
			int newH = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

			return bitmap.Resize(new SKImageInfo(newW, newH), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
		}

		private static byte[] toRgb24(SKBitmap bitmap)
		{
			SKColor[] pixels = bitmap.Pixels;
			byte[] rgb24 = new byte[pixels.Length * 3];

			for (int i = 0; i < pixels.Length; i++)
			{
				SKColor px = pixels[i];
				byte alpha = px.Alpha;
				int baseIndex = i * 3;
				rgb24[baseIndex + 0] = compositeToWhite(px.Red, alpha);
				rgb24[baseIndex + 1] = compositeToWhite(px.Green, alpha);
				rgb24[baseIndex + 2] = compositeToWhite(px.Blue, alpha);
			}

			return rgb24;
		}

		private static byte[] toMonochromeRgb24(byte[] rgb24)
		{
			if (rgb24 == null || rgb24.Length == 0)
			{
				return rgb24;
			}

			byte[] mono = new byte[rgb24.Length];
			for (int i = 0; i < rgb24.Length; i += 3)
			{
				byte r = rgb24[i + 0];
				byte g = rgb24[i + 1];
				byte b = rgb24[i + 2];
				byte y = (byte)((r * 299 + g * 587 + b * 114) / 1000);
				mono[i + 0] = y;
				mono[i + 1] = y;
				mono[i + 2] = y;
			}

			return mono;
		}

		private static byte[] bgraToRgb24(byte[] bgra, int width, int height)
		{
			int pixelCount = width * height;
			byte[] rgb24 = new byte[pixelCount * 3];

			for (int i = 0; i < pixelCount; i++)
			{
				int src = i * 4;
				int dst = i * 3;

				byte b = bgra[src + 0];
				byte g = bgra[src + 1];
				byte r = bgra[src + 2];
				byte a = bgra[src + 3];

				rgb24[dst + 0] = compositeToWhite(r, a);
				rgb24[dst + 1] = compositeToWhite(g, a);
				rgb24[dst + 2] = compositeToWhite(b, a);
			}

			return rgb24;
		}

		private static byte compositeToWhite(byte channel, byte alpha)
		{
			int value = (channel * alpha) + (255 * (255 - alpha));
			return (byte)(value / 255);
		}

		private static int clamp(int value, int min, int max)
		{
			if (value < min) return min;
			if (value > max) return max;
			return value;
		}

		private static double clamp(double value, double min, double max)
		{
			if (value < min) return min;
			if (value > max) return max;
			return value;
		}
	}
}
