using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaticBit.Xrpl.Mcp.Server.Configuration;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace StaticBit.Xrpl.Mcp.Server.Services;

/// <summary>
/// Background sink that delivers operational alerts (auth failures, rate-limit hits,
/// tool errors, lifecycle events) to the admin Telegram chat via a SEPARATE bot
/// from any other Telegram MCP that may run on the same host.
///
/// Behaviour:
/// <list type="bullet">
/// <item>Bounded channel with DropOldest — never blocks the caller.</item>
/// <item>Dedup window: same <c>kind + tags</c> collapsed to a single message per window.</item>
/// <item>Hard cap per minute — excess alerts dropped (still written to local logs).</item>
/// </list>
/// </summary>
public sealed class AdminAlerter : IAdminAlerter, IHostedService, IAsyncDisposable
{
    private readonly Channel<AlertEnvelope> _queue;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly ILogger<AdminAlerter> _logger;
    private readonly TimeProvider _clock;

    private readonly ConcurrentDictionary<string, DedupEntry> _dedup =
        new ConcurrentDictionary<string, DedupEntry>(StringComparer.Ordinal);
    private readonly Lock _rateLock = new Lock();
    private DateTimeOffset _rateWindowStart;
    private int _rateWindowCount;

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private ITelegramBotClient? _botClient;
    private string? _botClientToken;

    public AdminAlerter(
        IOptionsMonitor<ServerOptions> options,
        ILogger<AdminAlerter> logger,
        TimeProvider? clock = null)
    {
        _options = options;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;

        AdminAlertsOptions opts = options.CurrentValue.AdminAlerts;
        int capacity = Math.Max(16, opts.Throttling.QueueCapacity);
        _queue = Channel.CreateBounded<AlertEnvelope>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public void Alert(AlertKind kind, string summary, IReadOnlyDictionary<string, string>? tags = null)
    {
        AdminAlertsOptions opts = _options.CurrentValue.AdminAlerts;
        if (!opts.Enabled)
        {
            return;
        }
        if (!IsEventEnabled(opts.Events, kind))
        {
            return;
        }

        string dedupKey = BuildDedupKey(kind, tags);
        DateTimeOffset now = _clock.GetUtcNow();

        DedupEntry entry = _dedup.AddOrUpdate(
            dedupKey,
            _ => new DedupEntry(now, 1),
            (_, existing) =>
            {
                if (now - existing.WindowStart > TimeSpan.FromMinutes(opts.Throttling.DedupWindowMinutes))
                {
                    return new DedupEntry(now, 1);
                }
                return existing with { Count = existing.Count + 1 };
            });

        // Send only on the first hit in the window. Subsequent hits just bump the count
        // and will be reported when the window expires (sent at the next first-hit).
        if (entry.Count != 1)
        {
            return;
        }

        if (!TryReserveRate(opts.Throttling.MaxAlertsPerMinute, now))
        {
            _logger.LogWarning(
                "AdminAlert dropped due to MaxAlertsPerMinute: kind={Kind} key={Key}",
                kind, dedupKey);
            return;
        }

        AlertEnvelope env = new AlertEnvelope(
            kind,
            summary,
            tags?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, string>(),
            now);
        _queue.Writer.TryWrite(env);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => ProcessQueueAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        if (_worker is not null)
        {
            try
            {
                await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            _cts.Dispose();
            _cts = null;
        }
        return ValueTask.CompletedTask;
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (AlertEnvelope env in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await DispatchAsync(env, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AdminAlert dispatch failed for kind={Kind}", env.Kind);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DispatchAsync(AlertEnvelope env, CancellationToken cancellationToken)
    {
        AdminAlertsOptions opts = _options.CurrentValue.AdminAlerts;
        if (string.IsNullOrWhiteSpace(opts.BotToken) || string.IsNullOrWhiteSpace(opts.ChatId))
        {
            _logger.LogWarning("AdminAlerts enabled but BotToken/ChatId not configured; dropping {Kind}", env.Kind);
            return;
        }

        ITelegramBotClient bot = GetOrCreateBotClient(opts.BotToken!);
        ChatId chatId = ParseChatId(opts.ChatId!);
        string body = Format(env);

        await bot.SendMessage(
            chatId: chatId,
            text: body,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private ITelegramBotClient GetOrCreateBotClient(string token)
    {
        if (_botClient is not null && string.Equals(_botClientToken, token, StringComparison.Ordinal))
        {
            return _botClient;
        }

        _botClient = new TelegramBotClient(token);
        _botClientToken = token;
        return _botClient;
    }

    private static ChatId ParseChatId(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.StartsWith('@'))
        {
            return new ChatId(trimmed);
        }
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
        {
            return new ChatId(id);
        }
        throw new InvalidOperationException(
            $"Server:AdminAlerts:ChatId='{value}' is not a numeric chat_id or @username.");
    }

    private bool TryReserveRate(int maxPerMinute, DateTimeOffset now)
    {
        if (maxPerMinute <= 0)
        {
            return true;
        }
        lock (_rateLock)
        {
            if (now - _rateWindowStart > TimeSpan.FromMinutes(1))
            {
                _rateWindowStart = now;
                _rateWindowCount = 0;
            }
            if (_rateWindowCount >= maxPerMinute)
            {
                return false;
            }
            _rateWindowCount++;
            return true;
        }
    }

    private static bool IsEventEnabled(AlertEventsOptions events, AlertKind kind) => kind switch
    {
        AlertKind.StartUp or AlertKind.ShutDown => events.Lifecycle,
        AlertKind.AuthFailure => events.AuthFailure,
        AlertKind.RateLimit => events.RateLimit,
        AlertKind.ToolError => events.ToolError,
        _ => true,
    };

    private static string BuildDedupKey(AlertKind kind, IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return kind.ToString();
        }
        StringBuilder sb = new StringBuilder();
        sb.Append(kind);
        foreach (KeyValuePair<string, string> pair in tags.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.Append('|').Append(pair.Key).Append('=').Append(pair.Value);
        }
        return sb.ToString();
    }

    private static string Format(AlertEnvelope env)
    {
        string icon = env.Kind switch
        {
            AlertKind.StartUp => "🟢",
            AlertKind.ShutDown => "🔴",
            AlertKind.AuthFailure => "🔒",
            AlertKind.RateLimit => "⚠️",
            AlertKind.ToolError => "❌",
            _ => "ℹ️",
        };

        StringBuilder sb = new StringBuilder();
        sb.Append(icon).Append(' ').Append("<b>").Append(HtmlEscape(env.Kind.ToString())).Append("</b>\n");
        sb.Append(HtmlEscape(env.Summary));

        if (env.Tags.Count > 0)
        {
            sb.Append("\n\n");
            foreach (KeyValuePair<string, string> pair in env.Tags)
            {
                sb.Append("• ").Append(HtmlEscape(pair.Key)).Append(": <code>")
                  .Append(HtmlEscape(pair.Value)).Append("</code>\n");
            }
        }

        sb.Append("\n<i>").Append(env.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
          .Append("  •  StaticBitXrplMcp admin-alerts</i>");

        return sb.ToString();
    }

    private static string HtmlEscape(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private sealed record AlertEnvelope(
        AlertKind Kind,
        string Summary,
        Dictionary<string, string> Tags,
        DateTimeOffset CreatedAt);

    private sealed record DedupEntry(DateTimeOffset WindowStart, int Count);
}
