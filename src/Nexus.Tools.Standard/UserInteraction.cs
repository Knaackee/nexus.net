namespace Nexus.Tools.Standard;

public interface IUserInteraction
{
    Task<UserResponse> AskAsync(
        UserQuestion question,
        UserInteractionOptions? options = null,
        CancellationToken ct = default);
}

public abstract record UserQuestion(string Question);

public sealed record FreeTextQuestion(string Question, string? Placeholder = null)
    : UserQuestion(Question);

public sealed record ConfirmQuestion(string Question, bool? DefaultValue = null)
    : UserQuestion(Question);

public sealed record SelectQuestion(string Question, IReadOnlyList<string> Options, int? DefaultIndex = null)
    : UserQuestion(Question);

public sealed record MultiSelectQuestion(string Question, IReadOnlyList<string> Options, IReadOnlyList<int>? DefaultSelected = null)
    : UserQuestion(Question);

public sealed record SecretQuestion(string Question)
    : UserQuestion(Question);

public sealed record UserResponse(string Answer, UserResponseStatus Status = UserResponseStatus.Answered);

public enum UserResponseStatus
{
    Answered,
    Cancelled,
    TimedOut,
    Deferred,
}

public sealed record UserInteractionOptions
{
    public TimeSpan? Timeout { get; init; }

    public string? DefaultOnTimeout { get; init; }

    public bool IsOptional { get; init; }

    public InteractionContext? Context { get; init; }
}

public sealed record InteractionContext(
    string AgentId,
    string? Reason = null,
    InteractionUrgency Urgency = InteractionUrgency.Normal);

public enum InteractionUrgency
{
    Low,
    Normal,
    High,
}

public sealed class ConsoleUserInteraction : IUserInteraction
{
    public Task<UserResponse> AskAsync(UserQuestion question, UserInteractionOptions? options = null, CancellationToken ct = default)
    {
        Console.WriteLine(question.Question);
        if (question is SelectQuestion select)
        {
            for (int i = 0; i < select.Options.Count; i++)
                Console.WriteLine($"[{i}] {select.Options[i]}");
        }
        else if (question is MultiSelectQuestion multi)
        {
            for (int i = 0; i < multi.Options.Count; i++)
                Console.WriteLine($"[{i}] {multi.Options[i]}");
        }

        var answer = Console.ReadLine();
        if (answer is null)
            return Task.FromResult(new UserResponse(options?.DefaultOnTimeout ?? string.Empty, UserResponseStatus.Cancelled));

        return Task.FromResult(new UserResponse(answer));
    }
}