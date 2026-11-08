using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Common.Models.Logging;

namespace EquipmentIsEternal;

public static class WebConfigServer
{
    private static HttpListener? _listener;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Start(string modPath, int port, ISptLogger<EquipmentIsEternal> logger)
    {
        if (_listener != null) return;

        var prefix = $"http://127.0.0.1:{port}/";
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();

            _ = Task.Run(() => ListenLoopAsync(modPath, logger));
            logger.Success($"EquipmentIsSomewhatEternal Web Config UI -> {prefix}");
        }
        catch (Exception ex)
        {
            logger.Warning($"EquipmentIsSomewhatEternal: Could not start Web Config UI on {prefix} ({ex.Message})");
        }
    }

    private static async Task ListenLoopAsync(string modPath, ISptLogger<EquipmentIsEternal> logger)
    {
        while (_listener is { IsListening: true })
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(ctx, modPath, logger));
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.Warning($"EquipmentIsSomewhatEternal Web UI error: {ex.Message}");
            }
        }
    }

    private static async Task HandleRequestAsync(HttpListenerContext ctx, string modPath, ISptLogger<EquipmentIsEternal> logger)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        try
        {
            var path = req.Url?.AbsolutePath ?? "/";

            if (req.HttpMethod == "GET" && (path == "/" || path == "/index.html"))
            {
                var htmlPath = Path.Combine(modPath, "wwwroot", "index.html");
                var html = File.Exists(htmlPath)
                    ? await File.ReadAllTextAsync(htmlPath, Encoding.UTF8)
                    : "<h1>wwwroot/index.html not found</h1>";
                await WriteResponseAsync(res, 200, "text/html; charset=utf-8", html);
                return;
            }

            if (req.HttpMethod == "GET" && path == "/api/config")
            {
                var current = ConfigStore.Load();
                var json = JsonSerializer.Serialize(current, JsonOptions);
                await WriteResponseAsync(res, 200, "application/json; charset=utf-8", json);
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/config")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var incoming = JsonSerializer.Deserialize<ModConfig>(body, JsonOptions);
                if (incoming == null)
                {
                    await WriteResponseAsync(res, 400, "application/json; charset=utf-8", "{\"error\":\"Invalid config payload\"}");
                    return;
                }

                var saved = ConfigStore.Save(incoming);
                logger.Info($"EquipmentIsSomewhatEternal: Config updated via Web UI (Preset={saved.Preset}, DurabilityLoss={saved.DurabilityLossPercent}%)");
                var json = JsonSerializer.Serialize(saved, JsonOptions);
                await WriteResponseAsync(res, 200, "application/json; charset=utf-8", json);
                return;
            }

            await WriteResponseAsync(res, 404, "text/plain; charset=utf-8", "Not found");
        }
        catch (Exception ex)
        {
            await WriteResponseAsync(res, 500, "application/json; charset=utf-8", JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    private static async Task WriteResponseAsync(HttpListenerResponse res, int statusCode, string contentType, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        res.StatusCode = statusCode;
        res.ContentType = contentType;
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }
}
