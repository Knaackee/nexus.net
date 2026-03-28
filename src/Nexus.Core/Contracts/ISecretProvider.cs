namespace Nexus.Core.Contracts;

public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
}

public class EnvironmentSecretProvider : ISecretProvider
{
    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
        => Task.FromResult(Environment.GetEnvironmentVariable(key));
}
