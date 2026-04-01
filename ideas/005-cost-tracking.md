# 005 — Cost Tracking & Budget Enforcement

## Priorität: 🟠 Hoch

## Status

Stand: 2026-04-01

- Erledigt: `Nexus.CostTracking` Package existiert
- Erledigt: `ICostTracker`, `IModelPricingProvider`, `DefaultCostTracker`, `DefaultModelPricingProvider`
- Erledigt: `CostTrackingChatClient` extrahiert Usage aus buffered und streaming responses
- Erledigt: Budget-Enforcement fuer `MaxCostUsd` im Default-Orchestrator-Pfad
- Erledigt: `AgentResult.TokenUsage` und `AgentResult.EstimatedCost` werden im Runtime-Pfad befuellt
- Erledigt: Tests, Doku und Beispiele fuer Cost Tracking und Budget Enforcement
- Offen: Eine separate `IBudgetEnforcer`-Abstraktion existiert noch nicht; Enforcement laeuft derzeit ueber `IBudgetTracker` + Middleware

## Warum ist das sinnvoll?

**LLM-Calls kosten Geld. Ohne aktives Tracking läuft ein Agent-Loop unkontrolliert.**

Nexus hat `AgentBudget` mit `MaxCostUsd` und `MaxIterations`, aber:
- Es gibt **keinen Mechanismus** der tatsächliche Kosten von LLM-Calls trackt
- `MaxCostUsd` ist nur eine Deklaration — niemand enforced sie
- Es gibt keinen Zugriff auf Token-Usage-Daten der API-Responses
- Kein aggregiertes Cost-Reporting über eine Session

Claude Code trackt dagegen:
- Input/Output/Cache-Tokens pro Model-Call
- Kosten in USD pro Model (mit model-spezifischen Preisen)
- API-Duration (mit und ohne Retries)
- Session-übergreifende Persistenz der Kosten
- Budget-Enforcement direkt im Query Loop

## Was muss getan werden?

### Erweiterung von `Nexus.Core` oder neues Micro-Package `Nexus.CostTracking`

### 1. Usage Tracking Abstractions

```csharp
public interface ICostTracker
{
    /// Registriert einen LLM API Call mit Token-Verbrauch.
    void RecordUsage(LlmUsageRecord usage);

    /// Registriert einen Tool-Aufruf (Dauer, evtl. eigene Kosten für externe APIs).
    void RecordToolUsage(ToolUsageRecord usage);

    /// Gesamtkosten der aktuellen Session in USD.
    decimal TotalCostUsd { get; }

    /// Gesamte Input-Tokens.
    long TotalInputTokens { get; }

    /// Gesamte Output-Tokens.
    long TotalOutputTokens { get; }

    /// Gesamte Cache-Read-Tokens (prompt caching).
    long TotalCacheReadTokens { get; }

    /// Gesamte API-Dauer.
    TimeSpan TotalApiDuration { get; }

    /// Aufschlüsselung pro Model.
    IReadOnlyDictionary<string, ModelUsageSummary> UsageByModel { get; }

    /// Event das bei jedem recorded Usage feuert.
    event Action<UsageRecordedEventArgs>? OnUsageRecorded;

    /// Snapshot für Persistierung.
    CostSnapshot GetSnapshot();
}

public record LlmUsageRecord
{
    public required string ModelId { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public int CacheReadTokens { get; init; }
    public int CacheWriteTokens { get; init; }
    public TimeSpan ApiDuration { get; init; }
    public decimal? CostUsd { get; init; } // Wenn null → aus Preistabelle berechnen
}

public record ToolUsageRecord
{
    public required string ToolName { get; init; }
    public required TimeSpan Duration { get; init; }
    public decimal ExternalCostUsd { get; init; } // z.B. für externe API-Tools
}

public record ModelUsageSummary
{
    public string ModelId { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public decimal CostUsd { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public int CallCount { get; init; }
}
```

### 2. Model-Preistabelle

```csharp
public interface IModelPricingProvider
{
    /// Gibt den Preis pro Token für ein Model zurück.
    ModelPricing? GetPricing(string modelId);
}

public record ModelPricing
{
    public required string ModelId { get; init; }
    public required decimal InputPricePerMillionTokens { get; init; }
    public required decimal OutputPricePerMillionTokens { get; init; }
    public decimal CacheReadPricePerMillionTokens { get; init; }
    public decimal CacheWritePricePerMillionTokens { get; init; }
}

/// Eingebaute Preise (Stand 2026, aktualisierbar).
public class DefaultModelPricingProvider : IModelPricingProvider
{
    private static readonly Dictionary<string, ModelPricing> Prices = new()
    {
        ["claude-sonnet-4"] = new() { ModelId = "claude-sonnet-4", InputPricePerMillionTokens = 3m, OutputPricePerMillionTokens = 15m, CacheReadPricePerMillionTokens = 0.30m },
        ["claude-opus-4"] = new() { ModelId = "claude-opus-4", InputPricePerMillionTokens = 15m, OutputPricePerMillionTokens = 75m, CacheReadPricePerMillionTokens = 1.50m },
        ["gpt-4o"] = new() { ModelId = "gpt-4o", InputPricePerMillionTokens = 2.50m, OutputPricePerMillionTokens = 10m },
        ["gpt-4.1"] = new() { ModelId = "gpt-4.1", InputPricePerMillionTokens = 2m, OutputPricePerMillionTokens = 8m },
        // ... erweiterbar via DI
    };
}
```

### 3. Budget Enforcement

```csharp
public interface IBudgetEnforcer
{
    /// Prüft ob der nächste LLM-Call noch im Budget ist.
    BudgetCheckResult CheckBudget(AgentBudget budget, ICostTracker tracker);

    /// Prüft ob ein spezifischer LLM-Call das Budget überschreiten würde.
    BudgetCheckResult EstimateCallCost(
        AgentBudget budget, ICostTracker tracker,
        int estimatedInputTokens, string modelId);
}

public record BudgetCheckResult
{
    public bool IsWithinBudget { get; init; }
    public decimal CurrentCostUsd { get; init; }
    public decimal MaxCostUsd { get; init; }
    public decimal RemainingBudgetUsd { get; init; }
    public BudgetWarningLevel Warning { get; init; }
}

public enum BudgetWarningLevel
{
    None,          // < 70%
    Approaching,   // 70-90%
    Critical,      // 90-99%
    Exceeded       // >= 100%
}
```

### 4. IChatClient Wrapper für automatisches Tracking

```csharp
/// Wraps einen IChatClient und trackt automatisch Token-Usage.
public class CostTrackingChatClient : DelegatingChatClient
{
    private readonly ICostTracker _tracker;
    private readonly string _modelId;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var response = await base.GetResponseAsync(messages, options, ct);
        sw.Stop();

        // Usage aus Response extrahieren
        if (response.Usage is not null)
        {
            _tracker.RecordUsage(new LlmUsageRecord
            {
                ModelId = _modelId,
                InputTokens = response.Usage.InputTokenCount ?? 0,
                OutputTokens = response.Usage.OutputTokenCount ?? 0,
                ApiDuration = sw.Elapsed,
            });
        }

        return response;
    }

    // Gleiches für GetStreamingResponseAsync...
}
```

### 5. DX: Registration

```csharp
builder
    .AddCostTracking(pricing =>
    {
        // Optional: Custom Pricing hinzufügen/überschreiben
        pricing.AddModel("my-custom-model", input: 1.0m, output: 5.0m);
    })
    .AddAgent("coder", agent => agent
        .WithBudget(maxCost: 2.0m, maxIterations: 30)); // Wird jetzt tatsächlich enforced!

// Später: Kosten abfragen
var tracker = nexus.GetRequiredService<ICostTracker>();
Console.WriteLine($"Session cost: ${tracker.TotalCostUsd:F4}");
Console.WriteLine($"Tokens: {tracker.TotalInputTokens} in / {tracker.TotalOutputTokens} out");
```

## Detail-Informationen

### Wie Claude Code Cost Tracking implementiert

- **`cost-tracker.ts`**: Singleton das alle Usage-Records aggregiert
- **Token-Preise**: Hardcoded pro Model, aktualisiert bei Model-Migrationen
- **Cache-Tracking**: Unterscheidet `cache_read_input_tokens` und `cache_creation_input_tokens` (wichtig da Cache-Reads viel günstiger sind)
- **Session-Persistenz**: Kosten werden in `~/.claude/projects/{slug}/` gespeichert und bei Session-Resume geladen
- **Budget im Query Loop**: Vor jedem Turn wird `totalCost >= maxBudgetUsd` geprüft → Stop mit `budget_exhausted`
- **Analytics**: Lines added/removed, web search requests werden auch getrackt (für Nutzungsstatistiken)

### Warum das für Nexus-User wichtig ist

1. **Cost Governance**: Enterprise-User MÜSSEN wissen was ein Agent kostet
2. **Budget Protection**: Ohne Budget-Enforcement kann ein Loop mit falschen Prompts hunderte Dollar verbrennen
3. **Optimierung**: Nur was gemessen wird kann optimiert werden (Cache-Hit-Rate, Token-Verbrauch pro Task)
4. **Billing**: SaaS-Anwendungen die auf Nexus basieren müssen Kosten pro User/Session tracken

### Aufwand

- ICostTracker + Implementation: ~200 Zeilen
- IModelPricingProvider: ~150 Zeilen
- IBudgetEnforcer: ~100 Zeilen
- CostTrackingChatClient (DelegatingChatClient): ~150 Zeilen
- Integration mit AgentLoop: ~50 Zeilen
- Tests: ~400 Zeilen
- **Gesamt: ~1.5-2 Tage**
