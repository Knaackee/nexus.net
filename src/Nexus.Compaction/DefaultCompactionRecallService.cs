using Microsoft.Extensions.AI;

namespace Nexus.Compaction;

public sealed class DefaultCompactionRecallService : ICompactionRecallService
{
    private readonly ICompactionRecallProvider[] _providers;

    public DefaultCompactionRecallService(IEnumerable<ICompactionRecallProvider> providers)
    {
        _providers = providers.OrderBy(provider => provider.Priority).ToArray();
    }

    public async Task<CompactionRecallResult> RecallAsync(CompactionRecallContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<ChatMessage> activeMessages = context.ActiveMessages.ToArray();
        var providersUsed = new List<string>(_providers.Length);

        foreach (var provider in _providers)
        {
            activeMessages = await provider.RecallAsync(context with
            {
                ActiveMessages = activeMessages,
            }, ct).ConfigureAwait(false);

            providersUsed.Add(provider.GetType().Name);
        }

        return new CompactionRecallResult(activeMessages, providersUsed);
    }
}