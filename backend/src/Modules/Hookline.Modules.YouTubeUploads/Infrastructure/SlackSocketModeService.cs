using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using Hookline.Modules.YouTubeUploads.Endpoints;
using Hookline.Modules.YouTubeUploads.Jobs;
using Hookline.SharedKernel.Jobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

/// <summary>
/// DEV-ONLY inbound Slack transport for the YouTube Uploads module. When
/// <c>YouTubeUploads:Slack:SocketMode:Enabled</c> is true it opens a Slack Socket Mode WebSocket
/// (<c>apps.connections.open</c> with the app-level token) and forwards inbound <c>events_api</c> and
/// <c>interactive</c> envelopes to the SAME reusable handlers the HTTP webhook uses
/// (<see cref="YouTubeUploadsProviderEndpoints.ProcessEventCallbackAsync"/> /
/// <see cref="YouTubeUploadsProviderEndpoints.DispatchBlockActionsAsync"/>). This lets a developer test
/// inbound Slack with NO public tunnel.
/// <para>
/// Invariants honoured: it is OFF by default and REFUSED at boot in Production (see
/// <c>GuardSecurityConfig</c>), so the canonical production path stays the signature-verified HTTP webhook.
/// It handles ONLY this module — the envelope routing below is module-local, never a cross-module
/// dispatcher. The WebSocket transport here is intentionally self-contained (not shared with the Comments
/// module): a module sees only the SharedKernel, and a Slack-aware WS client does not belong in the kernel,
/// so duplicating ~a screen of dev-only transport beats leaking Slack into shared contracts.
/// </para>
/// <para>
/// Security note: Socket Mode envelopes are pre-authenticated by the WSS handshake (the app-level token),
/// so — unlike the HTTP path — there is no per-message <c>X-Slack-Signature</c> to verify. The HTTP
/// signature verification is untouched and remains the production guard.
/// </para>
/// </summary>
public sealed class SlackSocketModeService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<YouTubeUploadsOptions> options,
    ILogger<SlackSocketModeService> logger) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var slack = options.Value.Slack;
        if (!slack.SocketMode.Enabled)
        {
            return; // disabled — the canonical HTTP webhook path is the only inbound transport.
        }

        if (string.IsNullOrWhiteSpace(slack.AppToken))
        {
            logger.LogWarning(
                "YouTube Uploads Slack Socket Mode is enabled but YouTubeUploads:Slack:AppToken is empty — " +
                "set an app-level token (xapp-…, scope connections:write). Socket Mode will stay off.");
            return;
        }

        logger.LogInformation("YouTube Uploads Slack Socket Mode ENABLED (dev-only inbound transport; no tunnel needed).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(slack.AppToken!, stoppingToken);
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
            {
                break; // server closed the socket — reconnect.
            }

            if (!await HandleEnvelopeAsync(ws, message, ct))
            {
                break; // disconnect frame — reconnect.
            }
        }
    }

    /// <summary>Calls <c>apps.connections.open</c> with the app-level token and returns the one-shot WSS URL.</summary>
    private async Task<string> OpenConnectionAsync(string appToken, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/apps.connections.open")
        {
            // Slack expects a form POST; the app-level token rides in the Authorization header.
            Content = new FormUrlEncodedContent(new Dictionary<string, string>()),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appToken);

        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            throw new InvalidOperationException($"apps.connections.open failed: {root.GetRawText()}");
        }

        return root.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("apps.connections.open returned no WebSocket url.");
    }

    /// <summary>Reads one (possibly fragmented) text frame. Returns null when the peer sends a close frame.</summary>
    private static async Task<string?> ReceiveTextAsync(ClientWebSocket ws, byte[] buffer, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Politely complete the closing handshake before the caller reconnects (best-effort).
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

    /// <summary>
    /// ACKs the envelope (Slack expects an ack within 3s) and routes its payload. Returns false on a
    /// <c>disconnect</c> frame so the caller reconnects.
    /// </summary>
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
                await DispatchAsync(type, payload, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // host shutdown — let the connection unwind cleanly.
            }
            catch (Exception ex)
            {
                // One bad envelope must NOT drop the socket: the ACK is already sent (Slack won't
                // redeliver), so tearing down would lose every other in-flight interaction too. Log + skip.
                logger.LogError(ex, "Socket Mode dispatch failed for a {Type} envelope; skipping it.", type);
            }
        }

        return true;
    }

    /// <summary>
    /// Module-LOCAL routing of a Socket Mode envelope into the reusable handlers — the exact same business
    /// entry points the HTTP webhook calls. This is per-module dispatch, never a cross-module dispatcher.
    /// </summary>
    private async Task DispatchAsync(string? type, JsonElement payload, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        switch (type)
        {
            case "events_api":
                // payload is the events_api wrapper — the same shape as the parsed HTTP body root.
                var payloadType = payload.TryGetProperty("type", out var pt) ? pt.GetString() : null;
                if (payloadType == "event_callback")
                {
                    await YouTubeUploadsProviderEndpoints.ProcessEventCallbackAsync(
                        payload,
                        sp.GetRequiredService<IDedupService>(),
                        sp.GetRequiredService<IJobScheduler>());
                }
                break;

            case "interactive":
                // payload is the block_actions object (Socket Mode delivers it as JSON, not form-encoded).
                await YouTubeUploadsProviderEndpoints.DispatchBlockActionsAsync(
                    payload,
                    sp.GetRequiredService<IJobService>(),
                    sp.GetRequiredService<ICancellationFlags>(),
                    sp.GetRequiredService<ISlackStatusService>(),
                    sp.GetRequiredService<SlackClient>(),
                    sp.GetRequiredService<SlackIngestService>(),
                    ct);
                break;
        }
    }
}
