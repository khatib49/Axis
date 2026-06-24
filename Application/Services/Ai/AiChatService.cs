using System.Text.Json;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Ai
{
    /// <summary>
    /// Orchestrates a multi-turn chat with Claude, including the tool-use
    /// loop. Each user message triggers a small loop:
    ///   1. Send conversation to Claude
    ///   2. If Claude wants to use a tool → run the tool, append result, repeat
    ///   3. If Claude returned plain text → stop, that's the answer
    /// Cap iterations at MaxToolHops to prevent runaway loops.
    /// </summary>
    public class AiChatService : IAiChatService
    {
        private const int MaxToolHops = 8;
        private const string DefaultModel = "claude-sonnet-4-6";

        private readonly ApplicationDbContext _db;
        private readonly ClaudeApiClient _claude;
        private readonly AiToolExecutor _executor;
        private readonly IIntegrationSettingsService _settings;

        public AiChatService(
            ApplicationDbContext db,
            ClaudeApiClient claude,
            AiToolExecutor executor,
            IIntegrationSettingsService settings)
        {
            _db = db;
            _claude = claude;
            _executor = executor;
            _settings = settings;
        }

        public async Task<IReadOnlyList<AiConversationSummaryDto>> ListConversationsAsync(int take = 50, CancellationToken ct = default)
        {
            return await _db.AiConversations.AsNoTracking()
                .OrderByDescending(c => c.LastMessageOn)
                .Take(Math.Clamp(take, 1, 200))
                .Select(c => new AiConversationSummaryDto(c.Id, c.Title, c.CreatedOn, c.LastMessageOn, c.CreatedBy))
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<AiMessageDto>> GetMessagesAsync(int conversationId, CancellationToken ct = default)
        {
            return await _db.AiMessages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Id)
                .Select(m => new AiMessageDto(m.Id, m.Role, m.Content, m.ToolCalls, m.ToolCallId, m.ToolName, m.CreatedOn))
                .ToListAsync(ct);
        }

        public async Task DeleteConversationAsync(int conversationId, CancellationToken ct = default)
        {
            var conv = await _db.AiConversations.FindAsync(new object[] { conversationId }, ct);
            if (conv != null)
            {
                _db.AiConversations.Remove(conv);   // cascades to messages
                await _db.SaveChangesAsync(ct);
            }
        }

        public async Task<AiSendMessageResponse> SendAsync(AiSendMessageRequest req, string actor, CancellationToken ct = default)
        {
            var apiKey = await _settings.GetRawAsync("Anthropic.ApiKey", ct)
                ?? throw new InvalidOperationException("Anthropic API key not configured. Set Anthropic.ApiKey in /admin/integrations.");
            var model = await _settings.GetRawAsync("Anthropic.Model", ct) ?? DefaultModel;

            // 1) Resolve / create conversation
            AiConversation conv;
            if (req.ConversationId is int id && id > 0)
            {
                conv = await _db.AiConversations.FirstAsync(c => c.Id == id, ct);
            }
            else
            {
                conv = new AiConversation
                {
                    Title = TitleFromFirstMessage(req.Message),
                    CreatedBy = actor,
                    CreatedOn = DateTime.UtcNow,
                    LastMessageOn = DateTime.UtcNow
                };
                _db.AiConversations.Add(conv);
                await _db.SaveChangesAsync(ct);
            }

            // 2) Persist the user message and capture id of the cursor
            var userMsg = new AiMessage
            {
                ConversationId = conv.Id,
                Role = "user",
                Content = req.Message,
                CreatedOn = DateTime.UtcNow
            };
            _db.AiMessages.Add(userMsg);
            await _db.SaveChangesAsync(ct);
            var cursorId = userMsg.Id;

            // 3) Build the Claude-shaped conversation from history
            var history = await _db.AiMessages.AsNoTracking()
                .Where(m => m.ConversationId == conv.Id)
                .OrderBy(m => m.Id)
                .ToListAsync(ct);

            var claudeMessages = BuildClaudeMessages(history);

            // 4) Tool-use loop
            var tools = AiToolRegistry.All();
            var newMsgs = new List<AiMessage>();
            var proposalIds = new List<int>();

            for (int hop = 0; hop < MaxToolHops; hop++)
            {
                var resp = await _claude.CreateMessageAsync(
                    apiKey, model, SystemPrompt(), claudeMessages, tools, maxTokens: 2048, ct);

                // 4a) Persist assistant turn (always).
                var assistantText = string.Join("\n",
                    resp.Content.Where(b => b.Type == "text" && !string.IsNullOrEmpty(b.Text)).Select(b => b.Text!));
                var toolUses = resp.Content.Where(b => b.Type == "tool_use").ToList();
                var toolCallsJson = toolUses.Count > 0
                    ? JsonSerializer.Serialize(toolUses.Select(tu => new {
                        id = tu.Id, name = tu.Name, input = tu.Input
                    }))
                    : null;

                var asstMsg = new AiMessage
                {
                    ConversationId = conv.Id,
                    Role = "assistant",
                    Content = string.IsNullOrEmpty(assistantText) ? null : assistantText,
                    ToolCalls = toolCallsJson,
                    CreatedOn = DateTime.UtcNow
                };
                _db.AiMessages.Add(asstMsg);
                await _db.SaveChangesAsync(ct);
                newMsgs.Add(asstMsg);

                // Mirror into claudeMessages so next turn sees what just happened.
                claudeMessages.Add(AssistantBlockMessage(resp.Content));

                if (toolUses.Count == 0 || resp.StopReason == "end_turn")
                    break;

                // 4b) Execute each tool, persist a tool-role message per result.
                var toolResults = new List<object>();
                foreach (var tu in toolUses)
                {
                    var result = await _executor.ExecuteAsync(
                        tu.Name!, tu.Input ?? default, conv.Id, actor, ct);

                    var toolMsg = new AiMessage
                    {
                        ConversationId = conv.Id,
                        Role = "tool",
                        Content = result,
                        ToolCallId = tu.Id,
                        ToolName = tu.Name,
                        CreatedOn = DateTime.UtcNow
                    };
                    _db.AiMessages.Add(toolMsg);
                    await _db.SaveChangesAsync(ct);
                    newMsgs.Add(toolMsg);

                    toolResults.Add(new {
                        type = "tool_result",
                        tool_use_id = tu.Id,
                        content = result
                    });

                    // If this was a proposal tool, surface the new pending action id.
                    if (tu.Name == "propose_flash_tournament" || tu.Name == "propose_customer_ping")
                    {
                        try
                        {
                            using var jdoc = JsonDocument.Parse(result);
                            if (jdoc.RootElement.TryGetProperty("proposal_id", out var pid))
                                proposalIds.Add(pid.GetInt32());
                        }
                        catch { /* ignore */ }
                    }
                }

                // 4c) Append tool results as the next "user" message (per Anthropic spec).
                claudeMessages.Add(new ClaudeMessage
                {
                    Role = "user",
                    Content = JsonDocument.Parse(JsonSerializer.Serialize(toolResults)).RootElement.Clone()
                });
            }

            // 5) Bump conversation timestamp + title (if first turn)
            conv.LastMessageOn = DateTime.UtcNow;
            if (conv.Messages.Count == 0)
                conv.Title = TitleFromFirstMessage(req.Message);
            _db.AiConversations.Update(conv);
            await _db.SaveChangesAsync(ct);

            var newDtos = newMsgs.Select(m => new AiMessageDto(
                m.Id, m.Role, m.Content, m.ToolCalls, m.ToolCallId, m.ToolName, m.CreatedOn)).ToList();

            return new AiSendMessageResponse(conv.Id, newDtos, proposalIds);
        }

        // ── helpers ────────────────────────────────────────────────────
        private static string SystemPrompt() => @"
You are the AI assistant for Axis, a gaming lounge POS + accounting app. You help the owner/admin understand the business and act on it.

You have two kinds of tools:

1. READ-ONLY data tools — these return JSON about the lounge (revenue, items, occupancy, ingredients, players, expenses). Use them freely. When the predefined tools don't fit, use run_select_query with a SELECT statement to fetch anything you need from the database.

2. PROPOSAL tools (propose_flash_tournament, propose_customer_ping) — these DO NOT execute the action. They create a pending proposal that the admin must review and approve. ALWAYS explain to the admin what you're proposing and why before calling a propose_* tool. After calling one, tell the admin a proposal card has been added and they can approve or reject it.

Rules:
- Never claim to have sent a message or created anything — only a proposal until the admin approves.
- When asked for ideas to drive revenue (especially during slow hours), use get_occupancy_now and get_recent_players, then build a personalised tournament proposal grounded in real data.
- For ""we saved your spot"" pings, use get_due_regulars then propose_customer_ping.
- Be concise. Show numbers, currencies, dates clearly. Tables are fine.
- All prices are in USD unless stated otherwise.
- When uncertain about a fact, run a tool rather than guessing.
";

        private static List<ClaudeMessage> BuildClaudeMessages(List<AiMessage> history)
        {
            // Convert our flat history into Anthropic's role-blocked format.
            // Each user/assistant/tool row becomes a content block; consecutive
            // tool messages get grouped into a single user message with
            // tool_result blocks (Anthropic requires this).
            //
            // CRITICAL invariant Anthropic enforces: every assistant tool_use
            // must be paired with a tool_result before the next user/assistant
            // turn. If a previous request crashed between persisting the
            // assistant tool_use and the matching tool message, history is
            // orphaned and Anthropic rejects the whole conversation with
            // HTTP 400. We defend against this by tracking each tool_use_id
            // an assistant emits and synthesising a "previous run was
            // interrupted" tool_result for any id we never saw a tool row for.
            var list = new List<ClaudeMessage>();
            var pendingToolResults = new List<object>();
            var awaitingToolIds = new HashSet<string>(StringComparer.Ordinal);

            void FlushToolResults()
            {
                // Fill in any tool_use that never got a real result.
                foreach (var orphan in awaitingToolIds)
                {
                    pendingToolResults.Add(new
                    {
                        type = "tool_result",
                        tool_use_id = orphan,
                        content = "{\"error\":\"previous run was interrupted; no result captured\"}",
                        is_error = true
                    });
                }
                awaitingToolIds.Clear();

                if (pendingToolResults.Count == 0) return;
                list.Add(new ClaudeMessage
                {
                    Role = "user",
                    Content = JsonDocument.Parse(JsonSerializer.Serialize(pendingToolResults)).RootElement.Clone()
                });
                pendingToolResults.Clear();
            }

            foreach (var m in history)
            {
                if (m.Role == "tool")
                {
                    if (!string.IsNullOrEmpty(m.ToolCallId))
                        awaitingToolIds.Remove(m.ToolCallId);

                    pendingToolResults.Add(new {
                        type = "tool_result",
                        tool_use_id = m.ToolCallId,
                        content = m.Content ?? ""
                    });
                    continue;
                }

                FlushToolResults();

                if (m.Role == "user")
                {
                    var text = m.Content ?? "";
                    // Anthropic rejects empty user content. A space is the
                    // cheapest harmless placeholder.
                    if (string.IsNullOrWhiteSpace(text)) text = " ";
                    list.Add(new ClaudeMessage
                    {
                        Role = "user",
                        Content = JsonDocument.Parse(JsonSerializer.Serialize(text)).RootElement.Clone()
                    });
                }
                else if (m.Role == "assistant")
                {
                    var blocks = new List<object>();
                    if (!string.IsNullOrEmpty(m.Content))
                        blocks.Add(new { type = "text", text = m.Content });

                    if (!string.IsNullOrEmpty(m.ToolCalls))
                    {
                        try
                        {
                            using var jd = JsonDocument.Parse(m.ToolCalls);
                            foreach (var tu in jd.RootElement.EnumerateArray())
                            {
                                // .Clone() is critical: without it, the
                                // JsonElement points into 'jd''s buffer,
                                // and the using-block dispose at the end
                                // of this iteration invalidates it. When
                                // the outer JsonSerializer.Serialize(blocks)
                                // later tries to read it → ObjectDisposed.
                                var id = tu.GetProperty("id").GetString();
                                if (!string.IsNullOrEmpty(id)) awaitingToolIds.Add(id);

                                blocks.Add(new {
                                    type = "tool_use",
                                    id,
                                    name = tu.GetProperty("name").GetString(),
                                    input = tu.GetProperty("input").Clone()
                                });
                            }
                        }
                        catch { /* skip malformed historical row */ }
                    }

                    // Anthropic requires assistant content non-empty. If the
                    // saved row had neither text nor tool calls (shouldn't
                    // happen normally), put a single-space text block.
                    if (blocks.Count == 0)
                        blocks.Add(new { type = "text", text = " " });

                    list.Add(new ClaudeMessage
                    {
                        Role = "assistant",
                        Content = JsonDocument.Parse(JsonSerializer.Serialize(blocks)).RootElement.Clone()
                    });
                }
            }

            FlushToolResults();
            return list;
        }

        private static ClaudeMessage AssistantBlockMessage(List<ClaudeContentBlock> content)
        {
            // Re-serialize the just-returned content blocks as the assistant's history entry.
            // Clone JsonElements defensively so the resulting blocks don't carry references
            // into the HTTP response buffer.
            var blocks = content.Select<ClaudeContentBlock, object>(b =>
                b.Type == "tool_use"
                    ? new {
                        type = "tool_use",
                        id = b.Id,
                        name = b.Name,
                        input = b.Input.HasValue ? b.Input.Value.Clone() : (object?)new { }
                      }
                    : new { type = "text", text = b.Text ?? "" }
            ).ToList();

            return new ClaudeMessage
            {
                Role = "assistant",
                Content = JsonDocument.Parse(JsonSerializer.Serialize(blocks)).RootElement.Clone()
            };
        }

        private static string TitleFromFirstMessage(string msg)
        {
            var t = (msg ?? "").Trim();
            if (string.IsNullOrEmpty(t)) return "New chat";
            return t.Length <= 60 ? t : t[..60] + "…";
        }
    }
}
