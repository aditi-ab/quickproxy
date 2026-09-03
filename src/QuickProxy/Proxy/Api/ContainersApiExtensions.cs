using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Docker.DotNet;
using QuickProxy.Proxy.Containers;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Api;

public static class ContainersApiExtensions
{
    private static readonly JsonSerializerOptions NdjsonJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapContainersApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.AdminRoot}/containers").RequireAuthorization();

        group.MapGet("/", (IContainerInventory inventory) => { return Results.Ok(inventory.GetSnapshot()); });

        group.MapGet("/images",
            async (bool? all, IContainerRuntimeClient runtimeClient, CancellationToken cancellationToken) =>
            {
                var images = await runtimeClient.ListImagesAsync(all ?? false, cancellationToken);
                return Results.Ok(new
                {
                    images
                });
            });

        group.MapPost("/images/prune",
            async (IContainerRuntimeClient runtimeClient, CancellationToken cancellationToken) =>
            {
                try
                {
                    var removedCount = await runtimeClient.PruneUnusedImagesAsync(cancellationToken);
                    return Results.Ok(new
                    {
                        removedCount,
                        message = $"Removed {removedCount} unused image(s)."
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "container_action_failed",
                        message = ex.Message
                    });
                }
            });

        group.MapGet("/default-sets", (IContainerDefaultsStore store) =>
        {
            return Results.Ok(new
            {
                sets = store.List()
            });
        });

        group.MapGet("/default-sets/{id}", (string id, IContainerDefaultsStore store) =>
        {
            var normalizedId = NormalizeDefaultsSetId(id);
            if (string.IsNullOrWhiteSpace(normalizedId))
                return Results.BadRequest(new
                {
                    code = "validation_error",
                    message = "Defaults set id is required."
                });

            var set = store.Get(normalizedId);
            if (set is null)
                return Results.NotFound(new
                {
                    code = "not_found",
                    message = $"Defaults set '{normalizedId}' was not found."
                });

            return Results.Ok(set);
        });

        group.MapPut("/default-sets/{id}", async (string id, ContainerDefaultsSetUpsertRequest request,
            IContainerDefaultsStore store, IContainerDefaultsApplier defaultsApplier,
            CancellationToken cancellationToken) =>
        {
            var normalizedId = NormalizeDefaultsSetId(id);
            if (string.IsNullOrWhiteSpace(normalizedId)) return Validation("Defaults set id is required.");

            var bodyId = NormalizeDefaultsSetId(request.Id);
            if (!string.IsNullOrWhiteSpace(bodyId) &&
                !string.Equals(bodyId, normalizedId, StringComparison.OrdinalIgnoreCase))
                return Validation("Request id must match route id.");

            var normalizedLabels = NormalizeKeyValuePairs(request.Labels, true, "labels", out var labelsError);
            if (!string.IsNullOrWhiteSpace(labelsError)) return Validation(labelsError);

            var normalizedEnvVars = NormalizeKeyValuePairs(request.EnvVars, false, "envVars", out var envError);
            if (!string.IsNullOrWhiteSpace(envError)) return Validation(envError);

            var normalizedMountBindings = NormalizeMountBindings(request.MountBindings, out var mountBindingsError);
            if (!string.IsNullOrWhiteSpace(mountBindingsError)) return Validation(mountBindingsError);

            var normalizedHostMappings = NormalizeHostMappings(request.HostMappings, out var hostMappingsError);
            if (!string.IsNullOrWhiteSpace(hostMappingsError)) return Validation(hostMappingsError);

            var normalizedNetworkAliases = NormalizeNetworkAliases(request.NetworkAliases, out var networkAliasesError);
            if (!string.IsNullOrWhiteSpace(networkAliasesError)) return Validation(networkAliasesError);

            var upserted = store.Upsert(new ContainerDefaultsSet
            {
                Id = normalizedId,
                Labels = normalizedLabels,
                EnvVars = normalizedEnvVars,
                MountBindings = normalizedMountBindings,
                HostMappings = normalizedHostMappings,
                NetworkAliases = normalizedNetworkAliases
            });

            var appliedCount = await defaultsApplier.ApplyForDefaultsSetAsync(normalizedId, cancellationToken);

            return Results.Ok(new
            {
                set = upserted,
                appliedContainers = appliedCount
            });
        });

        group.MapDelete("/default-sets/{id}", (string id, IContainerDefaultsStore store) =>
        {
            var normalizedId = NormalizeDefaultsSetId(id);
            if (string.IsNullOrWhiteSpace(normalizedId)) return Validation("Defaults set id is required.");

            return store.Delete(normalizedId)
                ? Results.NoContent()
                : Results.NotFound(new
                {
                    code = "not_found",
                    message = $"Defaults set '{normalizedId}' was not found."
                });
        });

        group.MapGet("/projects", (ComposeProjectService service) =>
        {
            return Results.Ok(new
            {
                projects = service.List()
            });
        });

        group.MapGet("/projects/{id}",
            async (string id, ComposeProjectService service, CancellationToken cancellationToken) =>
            {
                var project = await service.GetAsync(id, cancellationToken);
                if (project is null)
                    return Results.NotFound(new
                    {
                        code = "not_found",
                        message = $"Compose project '{id}' was not found."
                    });

                return Results.Ok(project);
            });

        group.MapPut("/projects/{id}", (string id, ComposeProjectUpsertRequest request, ComposeProjectService service)
            => UpsertComposeProject(id, request, service));

        group.MapDelete("/projects/{id}", (string id, ComposeProjectService service) =>
        {
            return service.Delete(id)
                ? Results.NoContent()
                : Results.NotFound(new
                {
                    code = "not_found",
                    message = $"Compose project '{id}' was not found."
                });
        });

        group.MapPost("/projects/{id}/validate",
            async (string id, ComposeProjectService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.ValidateAsync(id, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "compose_validation_failed",
                        message = ex.Message
                    });
                }
            });

        group.MapPost("/projects/{id}/deploy",
            async (string id, ComposeProjectService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.DeployAsync(id, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "compose_action_failed",
                        message = ex.Message
                    });
                }
            });

        group.MapPost("/projects/{id}/start",
            async (string id, ComposeProjectService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.StartAsync(id, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "compose_action_failed",
                        message = ex.Message
                    });
                }
            });

        group.MapPost("/projects/{id}/stop",
            async (string id, ComposeProjectService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.StopAsync(id, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "compose_action_failed",
                        message = ex.Message
                    });
                }
            });

        group.MapPost("/projects/{id}/restart",
            async (string id, ComposeProjectService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.RestartAsync(id, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "compose_action_failed",
                        message = ex.Message
                    });
                }
            });

        group.MapPost("/projects/{id}/pull",
            async (string id, ComposeProjectService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.PullAsync(id, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "compose_action_failed",
                        message = ex.Message
                    });
                }
            });

        group.MapPost("/projects/{id}/down",
            async (string id, ComposeProjectService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.DownAsync(id, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "compose_action_failed",
                        message = ex.Message
                    });
                }
            });

        group.MapGet("/projects/{id}/logs/stream", async (string id, HttpContext httpContext,
            ComposeProjectService service, CancellationToken cancellationToken) =>
        {
            var tail = 200;
            if (int.TryParse(httpContext.Request.Query["tail"], out var requestedTail))
                tail = Math.Clamp(requestedTail, 1, 2000);

            var targetService = httpContext.Request.Query["service"].ToString();
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Append("X-Accel-Buffering", "no");
            httpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";

            try
            {
                await foreach (var entry in service.StreamLogsAsync(id, targetService, tail, cancellationToken))
                {
                    var payload = JsonSerializer.Serialize(entry, NdjsonJsonOptions);
                    await httpContext.Response.WriteAsync(payload + "\n", Encoding.UTF8, cancellationToken);
                    await httpContext.Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (InvalidOperationException ex)
            {
                if (!httpContext.Response.HasStarted)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await httpContext.Response.WriteAsJsonAsync(new
                    {
                        code = "compose_action_failed",
                        message = ex.Message
                    }, cancellationToken);
                }
            }
        });

        group.MapGet("/{name}", (string name, IContainerInventory inventory) =>
        {
            var container = inventory.GetContainer(name);
            if (container is null)
                return Results.NotFound(new
                {
                    code = "not_found",
                    message = $"Container '{name}' was not found."
                });

            return Results.Ok(new
            {
                status = inventory.GetStatus(),
                container
            });
        });

        group.MapGet("/{name}/edit",
            async (string name, IContainerRuntimeClient runtimeClient, CancellationToken cancellationToken) =>
            {
                try
                {
                    var container = await runtimeClient.GetEditableContainerAsync(name, cancellationToken);
                    return Results.Ok(container);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.NotFound(new
                    {
                        code = "not_found",
                        message = ex.Message
                    });
                }
            });

        group.MapGet("/{name}/logs/stream", async (string name, HttpContext httpContext,
            IContainerRuntimeClient runtimeClient, CancellationToken cancellationToken) =>
        {
            var tail = 200;
            if (int.TryParse(httpContext.Request.Query["tail"], out var requestedTail))
                tail = Math.Clamp(requestedTail, 1, 2000);

            var since = httpContext.Request.Query["since"].ToString();
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Append("X-Accel-Buffering", "no");
            httpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";

            try
            {
                await foreach (var entry in
                               runtimeClient.StreamContainerLogsAsync(name, since, tail, cancellationToken))
                {
                    var payload = JsonSerializer.Serialize(entry, NdjsonJsonOptions);
                    await httpContext.Response.WriteAsync(payload + "\n", Encoding.UTF8, cancellationToken);
                    await httpContext.Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (InvalidOperationException ex)
            {
                if (!httpContext.Response.HasStarted)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                    await httpContext.Response.WriteAsJsonAsync(new
                    {
                        code = "not_found",
                        message = ex.Message
                    }, cancellationToken);
                }
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotImplemented)
            {
                if (!httpContext.Response.HasStarted)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await httpContext.Response.WriteAsJsonAsync(new
                    {
                        code = "container_action_failed",
                        message = "The configured logging driver for this container does not support reading logs."
                    }, cancellationToken);
                }
            }
            catch (IOException ex) when (ex.Message.Contains("unknown stream type", StringComparison.OrdinalIgnoreCase))
            {
                if (!httpContext.Response.HasStarted)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await httpContext.Response.WriteAsJsonAsync(new
                    {
                        code = "container_action_failed",
                        message =
                            "The container log stream could not be read with the current Docker log configuration."
                    }, cancellationToken);
                }
            }
        });

        group.MapPost("/inspect-image-archive", async (HttpRequest httpRequest, CancellationToken cancellationToken) =>
        {
            try
            {
                if (!httpRequest.HasFormContentType)
                    throw new InvalidOperationException("Image archive form payload is required.");

                var form = await httpRequest.ReadFormAsync(cancellationToken);
                var archive = form.Files.GetFile("imageArchive");
                if (archive is null || archive.Length == 0)
                    throw new InvalidOperationException("Image archive file is required.");

                var extension = Path.GetExtension(archive.FileName);
                var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
                try
                {
                    await using (var target = File.Create(tempPath))
                    await using (var source = archive.OpenReadStream())
                    {
                        await source.CopyToAsync(target, cancellationToken);
                    }

                    var repoTags = await ContainerImageArchiveInspector.ReadRepoTagsAsync(tempPath, cancellationToken);
                    return Results.Ok(new
                    {
                        repoTags,
                        suggestedImage = repoTags.Count == 1 ? repoTags[0] : null
                    });
                }
                finally
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new
                {
                    code = "container_action_failed",
                    message = ex.Message
                });
            }
        });

        group.MapPost("/",
            async (HttpRequest httpRequest, IContainerRuntimeClient runtimeClient,
                IContainerDefaultsApplier defaultsApplier, CancellationToken cancellationToken) =>
            {
                try
                {
                    var (request, archivePath) = await ParseContainerRequestAsync(httpRequest, cancellationToken);
                    defaultsApplier.ApplyToRequest(request);
                    try
                    {
                        await runtimeClient.CreateContainerAsync(request, archivePath, cancellationToken);
                    }
                    finally
                    {
                        if (!string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath))
                            File.Delete(archivePath);
                    }

                    return Results.Created(
                        $"{InternalApiPaths.AdminRoot}/containers/{Uri.EscapeDataString(request.Name)}", request);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "container_action_failed",
                        message = ex.Message
                    });
                }
                catch (DockerApiException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "container_action_failed",
                        message = ex.ResponseBody ?? ex.Message
                    });
                }
            });

        group.MapGet("/{name}/shell/stream", async (string name, HttpContext httpContext,
            IContainerRuntimeClient runtimeClient, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("QuickProxy.ContainerShellWebSocket");
            logger.LogInformation(
                "Container shell websocket request received. Container='{ContainerName}', IsWebSocketRequest='{IsWebSocketRequest}', Scheme='{Scheme}', Host='{Host}', Path='{Path}', User='{User}'.",
                name,
                httpContext.WebSockets.IsWebSocketRequest,
                httpContext.Request.Scheme,
                httpContext.Request.Host.Value,
                httpContext.Request.Path.Value,
                httpContext.User.Identity?.Name ?? string.Empty);

            if (!httpContext.WebSockets.IsWebSocketRequest)
            {
                logger.LogWarning(
                    "Container shell websocket request rejected because it was not a websocket upgrade. Container='{ContainerName}', Headers='{Headers}'.",
                    name,
                    string.Join("; ", httpContext.Request.Headers.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                return Results.BadRequest(new
                {
                    code = "invalid_request",
                    message = "WebSocket connection is required."
                });
            }

            WebSocket webSocket;
            try
            {
                webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
                logger.LogInformation(
                    "Container shell websocket upgrade accepted. Container='{ContainerName}', SubProtocol='{SubProtocol}'.",
                    name,
                    webSocket.SubProtocol ?? string.Empty);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Container shell websocket upgrade failed. Container='{ContainerName}'.", name);
                throw;
            }

            using (webSocket)
            {
                using var linkedCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, httpContext.RequestAborted);
                var inputChannel = Channel.CreateUnbounded<ContainerShellClientMessage>();
                var receiveTask = ReceiveShellMessagesAsync(webSocket, inputChannel.Writer, linkedCts.Token);

                try
                {
                    await runtimeClient.StreamContainerShellAsync(name, inputChannel.Reader,
                        async (message, ct) => { await SendWebSocketMessageAsync(webSocket, message, ct); },
                        linkedCts.Token);
                    logger.LogInformation(
                        "Container shell websocket stream completed normally. Container='{ContainerName}'.", name);
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogWarning(ex,
                        "Container shell websocket stream failed with invalid operation. Container='{ContainerName}'.",
                        name);
                    await SendWebSocketMessageAsync(webSocket,
                        new ContainerShellServerMessage("error", Message: ex.Message), linkedCts.Token);
                }
                catch (DockerApiException ex)
                {
                    logger.LogWarning(ex,
                        "Container shell websocket stream failed with Docker API error. Container='{ContainerName}'.",
                        name);
                    await SendWebSocketMessageAsync(webSocket,
                        new ContainerShellServerMessage("error", Message: ex.ResponseBody ?? ex.Message),
                        linkedCts.Token);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Container shell websocket stream failed unexpectedly. Container='{ContainerName}'.", name);
                    throw;
                }
                finally
                {
                    inputChannel.Writer.TryComplete();
                    linkedCts.Cancel();
                    try
                    {
                        await receiveTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    logger.LogInformation(
                        "Container shell websocket closing. Container='{ContainerName}', WebSocketState='{WebSocketState}'.",
                        name,
                        webSocket.State);

                    if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shell session ended.",
                            CancellationToken.None);
                }
            }

            return Results.Empty;
        });

        group.MapPut("/{name}",
            async (string name, HttpRequest httpRequest, IContainerRuntimeClient runtimeClient,
                IContainerDefaultsApplier defaultsApplier, CancellationToken cancellationToken) =>
            {
                try
                {
                    var (request, archivePath) = await ParseContainerRequestAsync(httpRequest, cancellationToken);
                    defaultsApplier.ApplyToRequest(request);
                    try
                    {
                        await runtimeClient.UpdateContainerAsync(name, request, archivePath, cancellationToken);
                    }
                    finally
                    {
                        if (!string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath))
                            File.Delete(archivePath);
                    }

                    return Results.Ok(request);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "container_action_failed",
                        message = ex.Message
                    });
                }
                catch (DockerApiException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "container_action_failed",
                        message = ex.ResponseBody ?? ex.Message
                    });
                }
            });

        group.MapDelete("/{name}",
            async (string name, IContainerRuntimeClient runtimeClient, CancellationToken cancellationToken) =>
            {
                try
                {
                    await runtimeClient.DeleteContainerAsync(name, cancellationToken);
                    return Results.Ok(new { message = $"Container '{name}' was removed." });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.NotFound(new
                    {
                        code = "not_found",
                        message = ex.Message
                    });
                }
                catch (DockerApiException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "container_action_failed",
                        message = ex.ResponseBody ?? ex.Message
                    });
                }
            });

        group.MapPost("/{name}/start",
            async (string name, IContainerRuntimeClient runtimeClient, IContainerDefaultsApplier defaultsApplier,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var applyResult = await defaultsApplier.ApplyForStartAsync(name, true, cancellationToken);
                    if (!applyResult.StartedByApply) await runtimeClient.StartContainerAsync(name, cancellationToken);

                    return Results.Ok(new
                    {
                        message = $"Container '{name}' started.",
                        defaultsApplied = applyResult.Applied
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.NotFound(new
                    {
                        code = "not_found",
                        message = ex.Message
                    });
                }
                catch (DockerApiException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "container_action_failed",
                        message = ex.ResponseBody ?? ex.Message
                    });
                }
            });

        group.MapPost("/{name}/stop",
            async (string name, IContainerRuntimeClient runtimeClient, CancellationToken cancellationToken) =>
            {
                try
                {
                    await runtimeClient.StopContainerAsync(name, cancellationToken);
                    return Results.Ok(new { message = $"Container '{name}' stopped." });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.NotFound(new
                    {
                        code = "not_found",
                        message = ex.Message
                    });
                }
            });

        group.MapPost("/{name}/repull-restart",
            async (string name, IContainerRuntimeClient runtimeClient, CancellationToken cancellationToken) =>
            {
                try
                {
                    await runtimeClient.RepullImageAndRestartContainerAsync(name, cancellationToken);
                    return Results.Ok(new { message = $"Container '{name}' was re-pulled and restarted." });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "container_action_failed",
                        message = ex.Message
                    });
                }
                catch (DockerApiException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "container_action_failed",
                        message = ex.ResponseBody ?? ex.Message
                    });
                }
            });

        return app;
    }

    public static IEndpointRouteBuilder MapPublicContainersApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.Root}/containers");

        group.MapPut("/projects/{id}", (string id, ComposeProjectUpsertRequest request, ComposeProjectService service)
            => UpsertComposeProject(id, request, service));

        group.MapPost("/projects/{id}/deploy",
            async (string id, ComposeProjectService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.DeployAsync(id, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "compose_action_failed",
                        message = ex.Message
                    });
                }
            });

        group.MapPost("/projects/{id}/down",
            async (string id, ComposeProjectService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.DownAsync(id, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new
                    {
                        code = "compose_action_failed",
                        message = ex.Message
                    });
                }
            });

        return app;
    }

    private static async Task<(ContainerEditRequest Request, string? ArchivePath)> ParseContainerRequestAsync(
        HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!httpRequest.HasFormContentType)
        {
            var jsonRequest = await httpRequest.ReadFromJsonAsync<ContainerEditRequest>(cancellationToken)
                              ?? throw new InvalidOperationException("Container request payload is required.");
            return (jsonRequest, null);
        }

        var form = await httpRequest.ReadFormAsync(cancellationToken);
        var payload = form["request"].ToString();
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException("Container request payload is required.");

        var request = JsonSerializer.Deserialize<ContainerEditRequest>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Container request payload is invalid.");

        var archive = form.Files.GetFile("imageArchive");
        if (archive is null || archive.Length == 0) return (request, null);

        var extension = Path.GetExtension(archive.FileName);
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        await using (var target = File.Create(tempPath))
        await using (var source = archive.OpenReadStream())
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        return (request, tempPath);
    }

    private static string NormalizeDefaultsSetId(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static List<ContainerKeyValuePair> NormalizeKeyValuePairs(
        IReadOnlyList<ContainerKeyValuePair>? source,
        bool disallowQuickProxyInternalKeys,
        string fieldName,
        out string? error)
    {
        error = null;
        var result = new List<ContainerKeyValuePair>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in source ?? [])
        {
            var key = (pair.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                error = $"{fieldName} must not contain empty keys.";
                return [];
            }

            if (disallowQuickProxyInternalKeys &&
                key.StartsWith("quickproxy.internal.", StringComparison.OrdinalIgnoreCase))
            {
                error = $"{fieldName} key '{key}' is reserved.";
                return [];
            }

            if (seen.Contains(key))
            {
                error = $"{fieldName} contains duplicate key '{key}'.";
                return [];
            }

            seen.Add(key);
            result.Add(new ContainerKeyValuePair
            {
                Key = key,
                Value = pair.Value ?? string.Empty
            });
        }

        return result;
    }

    private static List<ContainerMountBindingRequest> NormalizeMountBindings(
        IReadOnlyList<ContainerMountBindingRequest>? source,
        out string? error)
    {
        error = null;
        var result = new List<ContainerMountBindingRequest>();
        var seenContainerPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in source ?? [])
        {
            var hostPath = (binding.HostPath ?? string.Empty).Trim();
            var containerPath = (binding.ContainerPath ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(hostPath))
            {
                error = "mountBindings must not contain empty hostPath values.";
                return [];
            }

            if (string.IsNullOrWhiteSpace(containerPath))
            {
                error = "mountBindings must not contain empty containerPath values.";
                return [];
            }

            if (!seenContainerPaths.Add(containerPath))
            {
                error = $"mountBindings contains duplicate containerPath '{containerPath}'.";
                return [];
            }

            result.Add(new ContainerMountBindingRequest
            {
                HostPath = hostPath,
                ContainerPath = containerPath,
                ReadOnly = binding.ReadOnly
            });
        }

        return result;
    }

    private static List<ContainerNetworkAliasRequest> NormalizeNetworkAliases(
        IReadOnlyList<ContainerNetworkAliasRequest>? source,
        out string? error)
    {
        error = null;
        var result = new List<ContainerNetworkAliasRequest>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in source ?? [])
        {
            var network = (alias.Network ?? string.Empty).Trim();
            var value = (alias.Alias ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(network) || string.IsNullOrWhiteSpace(value)) continue;

            var dedupeKey = $"{network}\u001f{value}";
            if (!seen.Add(dedupeKey))
            {
                error = $"networkAliases contains duplicate alias '{value}' for network '{network}'.";
                return [];
            }

            result.Add(new ContainerNetworkAliasRequest
            {
                Network = network,
                Alias = value
            });
        }

        return result;
    }

    private static List<ContainerHostMappingRequest> NormalizeHostMappings(
        IReadOnlyList<ContainerHostMappingRequest>? source,
        out string? error)
    {
        error = null;
        var result = new List<ContainerHostMappingRequest>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in source ?? [])
        {
            var hostname = (mapping.Hostname ?? string.Empty).Trim();
            var address = (mapping.Address ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(address)) continue;

            if (!seen.Add(hostname))
            {
                error = $"hostMappings contains duplicate hostname '{hostname}'.";
                return [];
            }

            result.Add(new ContainerHostMappingRequest
            {
                Hostname = hostname,
                Address = address
            });
        }

        return result;
    }

    private static IResult Validation(string message)
    {
        return Results.BadRequest(new
        {
            code = "validation_error",
            message = "Validation failed.",
            details = new[] { message }
        });
    }

    private static IResult UpsertComposeProject(string id, ComposeProjectUpsertRequest request,
        ComposeProjectService service)
    {
        try
        {
            var normalizedId = ComposeProjectStorageHelper.NormalizeId(id);
            if (string.IsNullOrWhiteSpace(normalizedId)) return Validation("Project id is required.");

            normalizedId = ComposeProjectStorageHelper.NormalizeProjectName(normalizedId);

            var bodyId = ComposeProjectStorageHelper.NormalizeId(request.Id);
            if (!string.IsNullOrWhiteSpace(bodyId) &&
                !string.Equals(ComposeProjectStorageHelper.NormalizeProjectName(bodyId), normalizedId,
                    StringComparison.OrdinalIgnoreCase))
                return Validation("Request id must match route id.");

            var stored = service.Upsert(new ComposeProject
            {
                Id = normalizedId,
                DisplayName = normalizedId,
                Slug = normalizedId,
                Status = (request.Status ?? string.Empty).Trim(),
                ComposeYaml = request.ComposeYaml ?? string.Empty,
                ManagedFiles = (request.ManagedFiles ?? []).Select(x => new ComposeManagedFile
                {
                    Path = x.Path ?? string.Empty,
                    Content = x.Content ?? string.Empty
                }).ToList()
            });

            return Results.Ok(stored);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new
            {
                code = "validation_error",
                message = ex.Message
            });
        }
    }

    private static async Task ReceiveShellMessagesAsync(
        WebSocket webSocket,
        ChannelWriter<ContainerShellClientMessage> writer,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var payload = new ArrayBufferWriter<byte>();

        try
        {
            while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                payload.Clear();
                WebSocketReceiveResult result;

                do
                {
                    result = await webSocket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        writer.TryComplete();
                        return;
                    }

                    payload.Write(buffer.AsSpan(0, result.Count));
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text || payload.WrittenCount == 0) continue;

                var message =
                    JsonSerializer.Deserialize<ContainerShellClientMessage>(payload.WrittenSpan, NdjsonJsonOptions);
                if (message is not null) await writer.WriteAsync(message, cancellationToken);
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static async Task SendWebSocketMessageAsync(WebSocket webSocket, ContainerShellServerMessage message,
        CancellationToken cancellationToken)
    {
        if (webSocket.State != WebSocketState.Open) return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, NdjsonJsonOptions);
        await webSocket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
    }
}

public sealed class ContainerDefaultsSetUpsertRequest
{
    public string? Id { get; set; }
    public List<ContainerKeyValuePair> Labels { get; set; } = [];
    public List<ContainerKeyValuePair> EnvVars { get; set; } = [];
    public List<ContainerMountBindingRequest> MountBindings { get; set; } = [];
    public List<ContainerHostMappingRequest> HostMappings { get; set; } = [];
    public List<ContainerNetworkAliasRequest> NetworkAliases { get; set; } = [];
}

public sealed class ComposeProjectUpsertRequest
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Slug { get; set; }
    public string? Status { get; set; }
    public string? ComposeYaml { get; set; }
    public List<ComposeManagedFileUpsertRequest> ManagedFiles { get; set; } = [];
}

public sealed class ComposeManagedFileUpsertRequest
{
    public string? Path { get; set; }
    public string? Content { get; set; }
}