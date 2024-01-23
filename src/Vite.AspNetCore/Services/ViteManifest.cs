﻿// Copyright (c) 2024 Quetzal Rivera.
// Licensed under the MIT License, See LICENCE in the project root for license information.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Text.Json;

namespace Vite.AspNetCore.Services;

/// <summary>
/// This class is used to read the manifest.json file generated by Vite.
/// </summary>
public sealed class ViteManifest : IViteManifest
{
	private static bool warnAboutManifestOnce = true;
	private readonly ILogger<ViteManifest> logger;
	private readonly IReadOnlyDictionary<string, ViteChunk> chunks;
	private readonly IViteDevServerStatus devServerStatus;
	private readonly string? basePath;

	/// <summary>
	/// Initializes a new instance of the <see cref="ViteManifest"/> class.
	/// </summary>
	/// <param name="logger">The service used to log messages.</param>
	/// <param name="options">The vite configuration options.</param>
	/// <param name="environment">Information about the web hosting environment.</param>
	public ViteManifest(ILogger<ViteManifest> logger, IOptions<ViteOptions> options, IViteDevServerStatus viteDevServer, IWebHostEnvironment environment)
	{
		this.logger = logger;
		this.devServerStatus = viteDevServer;

		// If the middleware is enabled, don't read the manifest.json file.
		if (viteDevServer.IsEnabled)
		{
			if (warnAboutManifestOnce)
			{
				logger.LogInformation("The manifest file won't be read because the vite development service is enabled. The service will always return null chunks");
				warnAboutManifestOnce = false;
			}

			this.chunks = new Dictionary<string, ViteChunk>();
			return;
		}

		// Get vite options.
		var viteOptions = options.Value;

		// Read tha name of the manifest file from the configuration.
		var manifestName = viteOptions.Manifest;

		// If the manifest file is in a subfolder, get the subfolder path.
		this.basePath = viteOptions.Base?.TrimStart('/');

		// Get the manifest.json file path
		string rootDir = Path.Combine(environment.WebRootPath, this.basePath ?? string.Empty);
		PhysicalFileProvider fileProvider = new(rootDir);
		IFileInfo manifestFile = fileProvider.GetFileInfo(manifestName);

		// If the manifest file doesn't exist, try to remove the ".vite/" prefix from the manifest file name. The default name for Vite 5 is ".vite/manifest.json" but for Vite 4 is "manifest.json".
		if (!manifestFile.Exists && manifestName.StartsWith(".vite"))
		{
			// Get the manifest.json file name without the ".vite/" prefix.
			var legacyManifestName = Path.GetFileName(manifestName);

			// Get the manifest.json file path
			manifestFile = fileProvider.GetFileInfo(legacyManifestName);
		}

		// If the manifest.json file exists, deserialize it into a dictionary.
		if (manifestFile.Exists)
		{
			// Read the manifest.json file and deserialize it into a dictionary
			using Stream readStream = manifestFile.CreateReadStream();
			this.chunks = JsonSerializer.Deserialize<IReadOnlyDictionary<string, ViteChunk>>(readStream, new JsonSerializerOptions()
			{
				PropertyNameCaseInsensitive = true
			})!;
			// TODO: refresh token/watch
		}
		else
		{
			if (warnAboutManifestOnce)
			{
				logger.LogWarning(
					"The manifest file was not found. Did you forget to build the assets? ('npm run build')");
				warnAboutManifestOnce = false;
			}

			// Create an empty dictionary.
			this.chunks = new Dictionary<string, ViteChunk>();
		}
	}

	/// <summary>
	/// Gets the Vite chunk for the specified entry point if it exists.
	/// If Dev Server is enabled, this will always return <see langword="null"/>.
	/// </summary>
	/// <param name="key"></param>
	/// <returns>The chunk if it exists, otherwise <see langword="null"/>.</returns>
	public IViteChunk? this[string key]
	{
		get
		{
			if (this.devServerStatus.IsEnabled)
			{
				this.logger.LogWarning("Attempted to get a record from the manifest file while the vite development server is enabled. Null was returned");
				return null;
			}

			if (!string.IsNullOrEmpty(this.basePath))
			{
				var basePath = this.basePath.Trim('/');
				// If the key starts with the base path, remove it and warn the user.
				if (key.StartsWith(basePath))
				{
					this.logger.LogWarning("Requesting a chunk with the base path included is deprecated. Please remove the base path from the key '{Key}'", key);
					key = key[basePath.Length..].TrimStart('/');
				}
			}

			// Try to get the chunk from the dictionary.
			if (!this.chunks.TryGetValue(key, out var chunk))
			{
				this.logger.LogWarning("The chunk '{Key}' was not found", key);
			}

			return chunk;
		}
	}

	/// <inheritdoc/>
	IEnumerator<IViteChunk> IEnumerable<IViteChunk>.GetEnumerator()
	{
		return this.chunks.Values.GetEnumerator();
	}

	/// <inheritdoc/>
	IEnumerator IEnumerable.GetEnumerator() => this.chunks.Values.GetEnumerator();

	/// <inheritdoc/>
	IEnumerable<string> IViteManifest.Keys => this.chunks.Keys;

	/// <inheritdoc/>
	bool IViteManifest.ContainsKey(string key) => this.chunks.ContainsKey(key);
}
