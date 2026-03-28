using Nexus.Core.Agents;

namespace Nexus.Testing.Evaluation;

/// <summary>Defines a single test case for agent evaluation.</summary>
public record EvaluationCase
{
    public required string Name { get; init; }
    public required string Input { get; init; }
    public required Func<AgentResult, bool> Assertion { get; init; }
    public string? ExpectedOutput { get; init; }
    public TimeSpan? MaxDuration { get; init; }
    public decimal? MaxCost { get; init; }
}

/// <summary>Single case result.</summary>
public record EvaluationCaseResult
{
    public required string CaseName { get; init; }
    public required bool Passed { get; init; }
    public string? FailureReason { get; init; }
    public TimeSpan Duration { get; init; }
    public decimal EstimatedCost { get; init; }
}

/// <summary>Aggregate evaluation report.</summary>
public record EvaluationReport
{
    public int TotalCases { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public decimal TotalCost { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public IReadOnlyList<EvaluationCaseResult> Results { get; init; } = [];
}

/// <summary>Evaluates an agent against a set of test cases.</summary>
public interface IAgentEvaluator
{
    Task<EvaluationReport> EvaluateAsync(IAgent agent, IReadOnlyList<EvaluationCase> cases, CancellationToken ct = default);
}

/// <summary>Default evaluator that runs cases sequentially.</summary>
public sealed class DefaultAgentEvaluator : IAgentEvaluator
{
    private readonly IAgentContext _context;

    public DefaultAgentEvaluator(IAgentContext context)
    {
        _context = context;
    }

    public async Task<EvaluationReport> EvaluateAsync(
        IAgent agent, IReadOnlyList<EvaluationCase> cases, CancellationToken ct = default)
    {
        var results = new List<EvaluationCaseResult>();
        var totalCost = 0m;
        var totalDuration = TimeSpan.Zero;

        foreach (var testCase in cases)
        {
            ct.ThrowIfCancellationRequested();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string? failureReason = null;
            var passed = false;

            try
            {
                var task = new AgentTask { Id = TaskId.New(), Description = testCase.Input };
                var result = await agent.ExecuteAsync(task, _context, ct);
                sw.Stop();

                var cost = result.EstimatedCost ?? 0m;
                totalCost += cost;

                passed = testCase.Assertion(result);
                if (!passed)
                    failureReason = "Assertion returned false.";

                if (testCase.MaxDuration.HasValue && sw.Elapsed > testCase.MaxDuration.Value)
                {
                    passed = false;
                    failureReason = $"Duration {sw.Elapsed} exceeded max {testCase.MaxDuration.Value}.";
                }

                if (testCase.MaxCost.HasValue && cost > testCase.MaxCost.Value)
                {
                    passed = false;
                    failureReason = $"Cost {cost:F4} exceeded max {testCase.MaxCost.Value:F4}.";
                }

                results.Add(new EvaluationCaseResult
                {
                    CaseName = testCase.Name,
                    Passed = passed,
                    FailureReason = failureReason,
                    Duration = sw.Elapsed,
                    EstimatedCost = cost,
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add(new EvaluationCaseResult
                {
                    CaseName = testCase.Name,
                    Passed = false,
                    FailureReason = ex.Message,
                    Duration = sw.Elapsed,
                    EstimatedCost = 0m,
                });
            }

            totalDuration += sw.Elapsed;
        }

        return new EvaluationReport
        {
            TotalCases = cases.Count,
            Passed = results.Count(r => r.Passed),
            Failed = results.Count(r => !r.Passed),
            TotalCost = totalCost,
            TotalDuration = totalDuration,
            Results = results,
        };
    }
}
