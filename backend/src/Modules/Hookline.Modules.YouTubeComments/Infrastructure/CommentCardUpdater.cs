using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Rewrites the original Block Kit comment card after a moderation action: strips the "Reject" button
/// (leaving the "open on YouTube" link), drops now-empty action rows, and appends a context line such
/// as "🚫 Removed by @user". Pure transform over the message blocks Slack echoes back in the
/// interaction payload, so the card keeps its content instead of being replaced by a bare line.
/// </summary>
public static class CommentCardUpdater
{
    /// <summary>
    /// Returns a new blocks array with the reject button removed and <paramref name="statusLine"/>
    /// appended as a context block. <paramref name="originalBlocks"/> is the interaction payload's
    /// <c>message.blocks</c>; when it is null/absent, returns a single context block with the status.
    /// </summary>
    public static JsonArray MarkActioned(JsonElement? originalBlocks, string statusLine)
    {
        var result = new JsonArray();

        if (originalBlocks is { ValueKind: JsonValueKind.Array } blocks)
        {
            foreach (var block in blocks.EnumerateArray())
            {
                var node = JsonNode.Parse(block.GetRawText())!;

                // Strip the reject button from any actions block; keep other elements (e.g. the link button).
                if (node["type"]?.GetValue<string>() == "actions" && node["elements"] is JsonArray elements)
                {
                    var kept = new JsonArray();
                    foreach (var el in elements)
                    {
                        if (el?["action_id"]?.GetValue<string>() == SlackActions.RejectComment)
                            continue;
                        kept.Add(el!.DeepClone());
                    }

                    if (kept.Count == 0)
                        continue; // drop a now-empty actions row entirely

                    node["elements"] = kept;
                }

                result.Add(node);
            }
        }

        result.Add(new JsonObject
        {
            ["type"] = "context",
            ["elements"] = new JsonArray
            {
                new JsonObject { ["type"] = "mrkdwn", ["text"] = statusLine },
            },
        });

        return result;
    }
}
