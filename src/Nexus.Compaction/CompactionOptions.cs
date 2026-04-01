namespace Nexus.Compaction;

public sealed class CompactionOptions
{
    public double AutoCompactThreshold { get; set; } = 0.80;
    public int RecentMessagesToKeep { get; set; } = 4;
    public int MinimumToolContentLength { get; set; } = 120;
    public int MinimumSummaryCandidateMessages { get; set; } = 2;
    public string SummaryInstruction { get; set; } =
        "Summarize the earlier conversation so an agent can continue the work. Preserve requirements, constraints, decisions, tool findings, and unresolved issues.";
}