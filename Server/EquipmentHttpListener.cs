using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;

namespace EquipmentIsEternal;

[Injectable]
public class EquipmentHttpListener(ISptLogger<EquipmentHttpListener> logger) : IHttpListener
{
    private static readonly PathString BasePath = new("/equipment-is-somewhat-eternal");
    private static readonly PathString ApiPath = new("/equipment-is-somewhat-eternal/api/config");

    public bool CanHandle(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments(BasePath, StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(MongoId sessionId, HttpContext context, CancellationToken cancellationToken)
    {
        var path = context.Request.Path;
        var method = context.Request.Method;

        if (path.Equals(ApiPath, StringComparison.OrdinalIgnoreCase))
        {
            await HandleApiAsync(method, context, cancellationToken);
            return;
        }

        await HandleStaticFileAsync(context, cancellationToken);
    }

    private async Task HandleApiAsync(string method, HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

        if (HttpMethods.IsGet(method))
        {
            var cfg = ConfigStore.Load();
            var json = JsonSerializer.Serialize(cfg, ConfigStore.JsonOptions);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(json, cancellationToken);
            return;
        }

        if (HttpMethods.IsPost(method))
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var updated = JsonSerializer.Deserialize<ModConfig>(body, ConfigStore.JsonOptions);
            if (updated == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync("{\"error\":\"Invalid config JSON\"}", cancellationToken);
                return;
            }

            var saved = ConfigStore.Save(updated);
            logger.Info($"EquipmentIsSomewhatEternal config updated via SIC (Preset: {saved.Preset}, DurabilityLoss: {saved.DurabilityLossPercent}%)");
            var resp = JsonSerializer.Serialize(saved, ConfigStore.JsonOptions);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(resp, cancellationToken);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
    }

    private static async Task HandleStaticFileAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var htmlPath = Path.Combine(ConfigStore.ModDirectory, "wwwroot", "index.html");
        if (!File.Exists(htmlPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("EquipmentIsSomewhatEternal: wwwroot/index.html not found.", cancellationToken);
            return;
        }

        var html = await File.ReadAllTextAsync(htmlPath, cancellationToken);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html, cancellationToken);
    }
}
