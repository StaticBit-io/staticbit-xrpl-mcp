using System;
using System.Collections.Generic;
using StaticBit.Xrpl.Mcp.Server.Configuration;
using StaticBit.Xrpl.Mcp.Server.Services;

namespace StaticBit.Xrpl.Mcp.Server.Tests;

[TestClass]
public class AdminAlerterTestsU
{
    private static ServerOptions BuildOptions(
        bool enabled = true,
        int dedupMinutes = 5,
        int maxPerMinute = 10,
        int queueCapacity = 1000,
        bool authFailureEvent = true)
    {
        return new ServerOptions
        {
            AdminAlerts = new AdminAlertsOptions
            {
                Enabled = enabled,
                BotToken = "fake-token-not-actually-sent",
                ChatId = "@fakechat",
                Events = new AlertEventsOptions
                {
                    AuthFailure = authFailureEvent,
                    RateLimit = true,
                    ToolError = true,
                    Lifecycle = true,
                },
                Throttling = new AlertThrottlingOptions
                {
                    DedupWindowMinutes = dedupMinutes,
                    MaxAlertsPerMinute = maxPerMinute,
                    QueueCapacity = queueCapacity,
                },
            },
        };
    }

    private static AdminAlerter BuildAlerter(ServerOptions options, FakeTimeProvider? clock = null)
    {
        return new AdminAlerter(
            new StaticOptionsMonitor<ServerOptions>(options),
            TestLoggers.Null<AdminAlerter>(),
            clock ?? new FakeTimeProvider(DateTimeOffset.UtcNow));
    }

    // --- Disabled / disabled-by-event ---

    [TestMethod]
    public void TestU_Disabled_DoesNotEnqueue()
    {
        AdminAlerter alerter = BuildAlerter(BuildOptions(enabled: false));
        alerter.Alert(AlertKind.AuthFailure, "test", null);
        Assert.AreEqual(0, alerter.PendingCount);
    }

    [TestMethod]
    public void TestU_EventDisabled_DoesNotEnqueue()
    {
        AdminAlerter alerter = BuildAlerter(BuildOptions(authFailureEvent: false));
        alerter.Alert(AlertKind.AuthFailure, "test", null);
        Assert.AreEqual(0, alerter.PendingCount);
    }

    // --- Dedup ---

    [TestMethod]
    public void TestU_Dedup_SameKindAndTagsWithinWindow_OnlyFirstEnqueued()
    {
        FakeTimeProvider clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        AdminAlerter alerter = BuildAlerter(BuildOptions(dedupMinutes: 5), clock);
        Dictionary<string, string> tags = new Dictionary<string, string> { ["ip"] = "1.1.1.1" };

        alerter.Alert(AlertKind.AuthFailure, "first", tags);
        alerter.Alert(AlertKind.AuthFailure, "second", tags);
        alerter.Alert(AlertKind.AuthFailure, "third", tags);

        Assert.AreEqual(1, alerter.PendingCount);
    }

    [TestMethod]
    public void TestU_Dedup_AfterWindow_ReenqueueAllowed()
    {
        FakeTimeProvider clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        AdminAlerter alerter = BuildAlerter(BuildOptions(dedupMinutes: 5), clock);
        Dictionary<string, string> tags = new Dictionary<string, string> { ["ip"] = "1.1.1.1" };

        alerter.Alert(AlertKind.AuthFailure, "first", tags);
        Assert.AreEqual(1, alerter.PendingCount);

        clock.Advance(TimeSpan.FromMinutes(6));
        alerter.Alert(AlertKind.AuthFailure, "after window", tags);

        Assert.AreEqual(2, alerter.PendingCount);
    }

    [TestMethod]
    public void TestU_Dedup_DifferentTags_NotCollapsed()
    {
        AdminAlerter alerter = BuildAlerter(BuildOptions());

        alerter.Alert(AlertKind.AuthFailure, "a", new Dictionary<string, string> { ["ip"] = "1.1.1.1" });
        alerter.Alert(AlertKind.AuthFailure, "b", new Dictionary<string, string> { ["ip"] = "2.2.2.2" });

        Assert.AreEqual(2, alerter.PendingCount);
    }

    [TestMethod]
    public void TestU_Dedup_NoTags_KindCollapses()
    {
        AdminAlerter alerter = BuildAlerter(BuildOptions());

        alerter.Alert(AlertKind.StartUp, "a");
        alerter.Alert(AlertKind.StartUp, "b");

        Assert.AreEqual(1, alerter.PendingCount);
    }

    // --- Per-minute rate cap ---

    [TestMethod]
    public void TestU_RateCap_ExceedsLimit_FurtherDropped()
    {
        FakeTimeProvider clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        AdminAlerter alerter = BuildAlerter(BuildOptions(dedupMinutes: 0, maxPerMinute: 3), clock);

        // Use unique tags to bypass dedup and exercise the rate cap.
        for (int i = 0; i < 5; i++)
        {
            alerter.Alert(AlertKind.AuthFailure, "x",
                new Dictionary<string, string> { ["ip"] = $"10.0.0.{i}" });
        }

        Assert.AreEqual(3, alerter.PendingCount);
    }

    [TestMethod]
    public void TestU_RateCap_ResetsAfterOneMinute()
    {
        FakeTimeProvider clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        AdminAlerter alerter = BuildAlerter(BuildOptions(dedupMinutes: 0, maxPerMinute: 2), clock);

        alerter.Alert(AlertKind.AuthFailure, "1", new Dictionary<string, string> { ["ip"] = "1" });
        alerter.Alert(AlertKind.AuthFailure, "2", new Dictionary<string, string> { ["ip"] = "2" });
        alerter.Alert(AlertKind.AuthFailure, "3", new Dictionary<string, string> { ["ip"] = "3" }); // dropped
        Assert.AreEqual(2, alerter.PendingCount);

        clock.Advance(TimeSpan.FromMinutes(2));
        alerter.Alert(AlertKind.AuthFailure, "4", new Dictionary<string, string> { ["ip"] = "4" });

        Assert.AreEqual(3, alerter.PendingCount);
    }

    [TestMethod]
    public void TestU_RateCap_DisabledWhenZero()
    {
        FakeTimeProvider clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        AdminAlerter alerter = BuildAlerter(BuildOptions(dedupMinutes: 0, maxPerMinute: 0), clock);

        for (int i = 0; i < 50; i++)
        {
            alerter.Alert(AlertKind.AuthFailure, "x",
                new Dictionary<string, string> { ["ip"] = i.ToString() });
        }

        Assert.AreEqual(50, alerter.PendingCount);
    }

    // --- Queue bounding ---

    [TestMethod]
    public void TestU_QueueCapacity_DropOldest_NotBlocking()
    {
        FakeTimeProvider clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        AdminAlerter alerter = BuildAlerter(
            BuildOptions(dedupMinutes: 0, maxPerMinute: 0, queueCapacity: 16),
            clock);

        // Enqueue beyond capacity. Bounded channel with DropOldest never grows past capacity.
        for (int i = 0; i < 50; i++)
        {
            alerter.Alert(AlertKind.AuthFailure, "x",
                new Dictionary<string, string> { ["ip"] = i.ToString() });
        }

        Assert.AreEqual(16, alerter.PendingCount);
    }

    // --- NullAdminAlerter ---

    [TestMethod]
    public void TestU_NullAdminAlerter_DoesNothing()
    {
        IAdminAlerter nullAlerter = new NullAdminAlerter();
        // Should not throw, should not crash with null tags.
        nullAlerter.Alert(AlertKind.AuthFailure, "x", null);
        nullAlerter.Alert(AlertKind.AuthFailure, "x",
            new Dictionary<string, string> { ["k"] = "v" });
    }
}
