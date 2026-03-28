using System.Text;

namespace Nexus.Cli;

/// <summary>
/// Represents a single chat session running against a Copilot model.
/// Captures streamed output and tracks conversation history.
/// </summary>
internal sealed class ChatSession : IDisposable
{
    private readonly CopilotChatClient _client;
    private readonly List<Microsoft.Extensions.AI.ChatMessage> _history = [];
    private readonly StringBuilder _lastResponse = new();
    private CancellationTokenSource? _cts;

    public string Key { get; }
    public string Model { get; }
    public ChatSessionState State { get; private set; } = ChatSessionState.Idle;
    public string LastOutput => _lastResponse.ToString();
    public int MessageCount => _history.Count;

    /// <summary>Raised for each streamed text chunk so the UI can update in real time.</summary>
    public event Action<string>? OnChunk;

    /// <summary>Raised when a run completes or fails.</summary>
    public event Action<ChatSession>? OnStateChanged;

    public ChatSession(string key, string model)
    {
        Key = key;
        Model = model;
        _client = new CopilotChatClient(model);
    }

    /// <summary>Sends a user message and starts streaming the response in the background.</summary>
    public void Send(string userMessage)
    {
        if (State == ChatSessionState.Running)
            return;

        _history.Add(new Microsoft.Extensions.AI.ChatMessage(
            Microsoft.Extensions.AI.ChatRole.User, userMessage));
        _lastResponse.Clear();

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        State = ChatSessionState.Running;
        OnStateChanged?.Invoke(this);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var update in _client.GetStreamingResponseAsync(
                    _history, null, token))
                {
                    var text = update.Text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        _lastResponse.Append(text);
                        OnChunk?.Invoke(text);
                    }
                }

                _history.Add(new Microsoft.Extensions.AI.ChatMessage(
                    Microsoft.Extensions.AI.ChatRole.Assistant, _lastResponse.ToString()));
                State = ChatSessionState.Idle;
            }
            catch (OperationCanceledException)
            {
                State = ChatSessionState.Idle;
            }
            catch (Exception ex)
            {
                _lastResponse.AppendLine();
                _lastResponse.Append(System.Globalization.CultureInfo.InvariantCulture, $"[ERROR] {ex.Message}");
                State = ChatSessionState.Failed;
            }

            OnStateChanged?.Invoke(this);
        }, CancellationToken.None);
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _client.Dispose();
    }
}

internal enum ChatSessionState
{
    Idle,
    Running,
    Failed,
}
