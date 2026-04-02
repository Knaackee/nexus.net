using FluentAssertions;
using Microsoft.Extensions.AI;
using Nexus.Core.Agents;
using Nexus.Testing.Mocks;
using Xunit;

namespace Nexus.Compaction.Tests;

public sealed class DefaultCompactionServiceTests
{
    [Fact]
    public async Task CompactAsync_Uses_Micro_Strategy_First_When_Older_Tool_Output_Is_Present()
    {
        var tokenCounter = new DefaultTokenCounter();
        var monitor = new DefaultContextWindowMonitor(tokenCounter);
        var options = new CompactionOptions { RecentMessagesToKeep = 2, MinimumToolContentLength = 40 };
        var service = new DefaultCompactionService(
            [new MicroCompactionStrategy(), new SummaryCompactionStrategy()],
            monitor,
            tokenCounter,
            options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Find the issue"),
            new(ChatRole.Tool, new string('x', 180)),
            new(ChatRole.Assistant, "I inspected the output."),
            new(ChatRole.User, "Continue"),
        };

        var result = await service.CompactAsync(
            messages,
            new ContextWindowOptions { MaxTokens = 80, TargetTokens = 40, ReservedForOutput = 8, ReservedForTools = 8 },
            new FakeChatClient("unused summary"));

        result.StrategyUsed.Should().Be("micro");
        result.TokensAfter.Should().BeLessThan(result.TokensBefore);
        result.CompactedMessages.Any(message => message.Text?.Contains("[Compacted tool output:") == true).Should().BeTrue();
    }

    [Fact]
    public async Task CompactAsync_Falls_Back_To_Summary_When_No_Tool_Output_Is_Compactable()
    {
        var tokenCounter = new DefaultTokenCounter();
        var monitor = new DefaultContextWindowMonitor(tokenCounter);
        var service = new DefaultCompactionService(
            [new MicroCompactionStrategy(), new SummaryCompactionStrategy()],
            monitor,
            tokenCounter,
            new CompactionOptions { RecentMessagesToKeep = 2 });

        var summarizer = new FakeChatClient("Summary of prior work");
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are helpful."),
            new(ChatRole.User, new string('a', 120)),
            new(ChatRole.Assistant, new string('b', 120)),
            new(ChatRole.User, "latest question"),
            new(ChatRole.Assistant, "latest answer"),
        };

        var result = await service.CompactAsync(
            messages,
            new ContextWindowOptions { MaxTokens = 100, TargetTokens = 50, ReservedForOutput = 8, ReservedForTools = 8 },
            summarizer);

        result.StrategyUsed.Should().Be("summary");
        result.CompactedMessages.Any(message => message.Text is not null && message.Text.Contains("[Conversation summary]"))
            .Should().BeTrue();
        result.TokensAfter.Should().BeLessThan(result.TokensBefore);
    }

    [Fact]
    public void ShouldCompact_Returns_True_When_Fill_Ratio_Exceeds_Threshold()
    {
        var tokenCounter = new DefaultTokenCounter();
        var monitor = new DefaultContextWindowMonitor(tokenCounter);
        var service = new DefaultCompactionService(
            [new MicroCompactionStrategy()],
            monitor,
            tokenCounter,
            new CompactionOptions { AutoCompactThreshold = 0.5 });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, new string('x', 220)),
            new(ChatRole.Assistant, new string('y', 220)),
        };

        service.ShouldCompact(messages, new ContextWindowOptions { MaxTokens = 120, TargetTokens = 100, ReservedForOutput = 8, ReservedForTools = 8 })
            .Should().BeTrue();
    }

    [Fact]
    public async Task RecallAsync_Applies_Providers_In_Priority_Order_And_Chains_Active_Messages()
    {
        var service = new DefaultCompactionRecallService(
        [
            new PrefixRecallProvider(2, "memory-b"),
            new PrefixRecallProvider(1, "memory-a"),
        ]);

        var result = await service.RecallAsync(new CompactionRecallContext
        {
            OriginalMessages =
            [
                new ChatMessage(ChatRole.User, "Original request"),
            ],
            ActiveMessages =
            [
                new ChatMessage(ChatRole.Assistant, "Compacted summary"),
            ],
            Compaction = new CompactionResult(
            [
                new ChatMessage(ChatRole.Assistant, "Compacted summary"),
            ],
            120,
            40,
            "summary"),
            WindowOptions = new ContextWindowOptions { MaxTokens = 200, TargetTokens = 100 },
        });

        result.ProvidersUsed.Should().ContainInOrder(nameof(PrefixRecallProvider), nameof(PrefixRecallProvider));
        result.Messages.Select(message => message.Text).Should().ContainInOrder("memory-b", "memory-a", "Compacted summary");
    }

    [Fact]
    public void ShouldCompact_Returns_False_When_Below_Threshold_And_TargetTokens()
    {
        var tokenCounter = new DefaultTokenCounter();
        var monitor = new DefaultContextWindowMonitor(tokenCounter);
        var service = new DefaultCompactionService(
            [new MicroCompactionStrategy()],
            monitor,
            tokenCounter,
            new CompactionOptions { AutoCompactThreshold = 0.8 });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "short"),
        };

        service.ShouldCompact(messages, new ContextWindowOptions { MaxTokens = 128_000, TargetTokens = 100_000, ReservedForOutput = 8_000, ReservedForTools = 4_000 })
            .Should().BeFalse();
    }

    [Fact]
    public async Task CompactAsync_Returns_None_When_No_Strategy_Applies()
    {
        var tokenCounter = new DefaultTokenCounter();
        var monitor = new DefaultContextWindowMonitor(tokenCounter);
        var service = new DefaultCompactionService(
            [new MicroCompactionStrategy()],
            monitor,
            tokenCounter,
            new CompactionOptions { RecentMessagesToKeep = 100 });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hello"),
            new(ChatRole.Assistant, "world"),
        };

        var result = await service.CompactAsync(
            messages,
            new ContextWindowOptions { MaxTokens = 128_000, TargetTokens = 100_000 },
            new FakeChatClient("unused"));

        result.StrategyUsed.Should().Be("none");
        result.TokensAfter.Should().Be(result.TokensBefore);
    }

    [Fact]
    public void TokenCounter_Empty_Messages_Returns_Zero()
    {
        var counter = new DefaultTokenCounter();
        counter.CountTokens([], systemPrompt: null).Should().Be(0);
    }

    [Fact]
    public void TokenCounter_Includes_SystemPrompt_In_Count()
    {
        var counter = new DefaultTokenCounter();
        var withoutPrompt = counter.CountTokens([new ChatMessage(ChatRole.User, "hi")]);
        var withPrompt = counter.CountTokens([new ChatMessage(ChatRole.User, "hi")], systemPrompt: "You are helpful.");
        withPrompt.Should().BeGreaterThan(withoutPrompt);
    }

    [Fact]
    public void TokenCounter_Single_Message_Matches_Collection_Count()
    {
        var counter = new DefaultTokenCounter();
        var message = new ChatMessage(ChatRole.User, "Test message content");
        counter.CountTokens(message).Should().Be(counter.CountTokens([message]));
    }

    [Fact]
    public void ContextWindowMonitor_FillRatio_Clamps_At_Zero_When_No_Tokens()
    {
        var counter = new DefaultTokenCounter();
        var monitor = new DefaultContextWindowMonitor(counter);

        var snapshot = monitor.Measure(
            [],
            new ContextWindowOptions { MaxTokens = 1000, ReservedForOutput = 100, ReservedForTools = 100 });

        snapshot.CurrentTokenCount.Should().Be(0);
        snapshot.FillRatio.Should().Be(0.0);
        snapshot.AvailableTokens.Should().Be(800);
    }

    private sealed class PrefixRecallProvider : ICompactionRecallProvider
    {
        private readonly string _message;

        public PrefixRecallProvider(int priority, string message)
        {
            Priority = priority;
            _message = message;
        }

        public int Priority { get; }

        public Task<IReadOnlyList<ChatMessage>> RecallAsync(CompactionRecallContext context, CancellationToken ct = default)
        {
            IReadOnlyList<ChatMessage> messages =
            [
                new ChatMessage(ChatRole.System, _message),
                .. context.ActiveMessages,
            ];

            return Task.FromResult(messages);
        }
    }
}