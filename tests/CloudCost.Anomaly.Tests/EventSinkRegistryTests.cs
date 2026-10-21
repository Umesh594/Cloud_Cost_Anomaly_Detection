using CloudCost.Anomaly.Core.Publishing;
using CloudCost.Anomaly.Worker.Services.Publishing;
using Microsoft.Extensions.Options;
using Xunit;

namespace CloudCost.Anomaly.Tests;

public sealed class EventSinkRegistryTests
{
    [Fact]
    public void ActiveSinks_OnlyIncludesSinksListedInConfiguration()
    {
        var sinks = new IEventSink[] { new FakeEventSink("adx"), new FakeEventSink("webhook"), new FakeEventSink("archive") };
        var options = Options.Create(new EventPublishingOptions { EnabledSinks = ["adx", "archive"] });

        var registry = new EventSinkRegistry(sinks, options);

        Assert.Equal(2, registry.ActiveSinks.Count);
        Assert.Contains(registry.ActiveSinks, s => s.Name == "adx");
        Assert.Contains(registry.ActiveSinks, s => s.Name == "archive");
        Assert.DoesNotContain(registry.ActiveSinks, s => s.Name == "webhook");
    }

    [Fact]
    public void Constructor_UnknownSinkNameInConfiguration_Throws()
    {
        var sinks = new IEventSink[] { new FakeEventSink("adx") };
        var options = Options.Create(new EventPublishingOptions { EnabledSinks = ["does-not-exist"] });

        Assert.Throws<InvalidOperationException>(() => new EventSinkRegistry(sinks, options));
    }

    [Fact]
    public void Constructor_DuplicateSinkNames_Throws()
    {
        var sinks = new IEventSink[] { new FakeEventSink("adx"), new FakeEventSink("adx") };
        var options = Options.Create(new EventPublishingOptions { EnabledSinks = ["adx"] });

        Assert.Throws<InvalidOperationException>(() => new EventSinkRegistry(sinks, options));
    }

    [Fact]
    public void ActiveSinks_NewSinkOnboardedPurelyThroughConfiguration_RequiresNoCodeChange()
    {
        // Demonstrates the onboarding contract: adding a brand-new IEventSink implementation and
        // listing its name is sufficient for it to join the fan-out - the registry never switches on
        // concrete sink types.
        var sinks = new IEventSink[] { new FakeEventSink("adx"), new FakeEventSink("brand-new-destination") };
        var options = Options.Create(new EventPublishingOptions { EnabledSinks = ["adx", "brand-new-destination"] });

        var registry = new EventSinkRegistry(sinks, options);

        Assert.Contains(registry.ActiveSinks, s => s.Name == "brand-new-destination");
    }
}
