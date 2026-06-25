using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hookline.SharedKernel.Slack;

/// <summary>
/// DEV-ONLY inbound Slack Socket Mode transport. Opens a WebSocket via <c>apps.connections.open</c>
/// and dispatches envelopes to the derived module's handler. Each module subclasses this to route
/// its own envelope types (events_api, interactive, etc.) while sharing the connection/reconnect
/// infrastructure.
/// <para>
/// Invariants: OFF by default and REFUSED in Production (modules guard this in their own config
/// validation). The WebSocket transport handles reconnection, ACKing, and error isolation so a bad
/// envelope never drops the connection.
/// </para>
/// </summary>
public abstract class SlackSocketModeServiceBase(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger logger) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    /// <summary>Human-readable module name for log messages (e.g. "YouTube Comments").</summary>
    protected abstract string ModuleName { get; }

    /// <summary>Whether Socket Mode is enabled in this module's configuration.</summary>
    protected abstract bool IsEnabled { get; }

    /// <summary>The Slack app-level token (<c>xapp-…</c>) for <c>apps.connections.open</c>.</summary>
    protected abstract string? AppToken { get; }

    /// <summary>
    /// Module-specific envelope dispatch. Called inside a fresh DI scope; route the envelope type to
    /// the same reusable handlers the HTTP webhook uses.
    /// </summary>
    protected abstract Task DispatchEnvelopeAsync(IServiceProvider scopedServices, string? type, JsonElement payload, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled)
            return;

        if (string.IsNullOrWhiteSpace(AppToken))
        {
            logger.LogWarning(
                "{Module} Slack Socket Mode is enabled but the AppToken is empty — " +
                "set an app-level token (xapp-…, scope connections:write). Socket Mode will stay off.",
                ModuleName);
            return;
        }

        logger.LogInformation("{Module} Slack Socket Mode ENABLED (dev-only inbound transport; no tunnel needed).", ModuleName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(AppToken!, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Socket Mode connection dropped; reconnecting in {Delay}s.", ReconnectDelay.TotalSeconds);
            }

            try
            {
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunConnectionAsync(string appToken, CancellationToken ct)
    {
        var wssUrl = await OpenConnectionAsync(appToken, ct);

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(wssUrl), ct);
        logger.LogInformation("Socket Mode WebSocket connected.");

        var buffer = new byte[64 * 1024];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var message = await ReceiveTextAsync(ws, buffer, ct);
            if (message is null)
                break;

            if (!await HandleEnvelopeAsync(ws, message, ct))
                break;
        }
    }

    private async Task<string> OpenConnectionAsync(string appToken, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/apps.connections.open")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>()),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appToken);

        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException($"apps.connections.open failed: {root.GetRawText()}");

        return root.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("apps.connections.open returned no WebSocket url.");
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket ws, byte[] buffer, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                try
                {
                    if (ws.State == WebSocketState.CloseReceived)
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                }
                catch
                {
                    // Shutting down / socket already gone — the reconnect loop handles it.
                }

                return null;
            }

            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private async Task<bool> HandleEnvelopeAsync(ClientWebSocket ws, string message, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

        switch (type)
        {
            case "hello":
                logger.LogInformation("Socket Mode: hello (connected).");
                return true;
            case "disconnect":
                logger.LogInformation(
                    "Socket Mode: disconnect ({Reason}) — reconnecting.",
                    root.TryGetProperty("reason", out var r) ? r.GetString() : "unknown");
                return false;
        }

        if (root.TryGetProperty("envelope_id", out var eidEl) && eidEl.GetString() is { } envelopeId)
        {
            var ack = JsonSerializer.SerializeToUtf8Bytes(new { envelope_id = envelopeId });
            await ws.SendAsync(ack, WebSocketMessageType.Text, endOfMessage: true, ct);
        }

        if (root.TryGetProperty("payload", out var payload))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await DispatchEnvelopeAsync(scope.ServiceProvider, type, payload, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Socket Mode dispatch failed for a {Type} envelope; skipping it.", type);
            }
        }

        return true;
    }
}
