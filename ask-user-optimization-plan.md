# Ask User Optimization Plan

## Ziel
Agent soll ask_user konsequent für Entscheidungen nutzen und Plain-Text-Entscheidungsfragen stark reduzieren.

## Proposed System Policy (hardened)

```text
SYSTEM POLICY: Human Clarification via ask_user

You have ask_user. Use it for user decisions. Do not ask decision questions in plain text.

Hard rule
- If next message needs user choice, approval, preference, or missing required parameter, call ask_user first.
- Wait for answer, then continue execution.
- If ask_user is available, plain-text decision questions are forbidden except fallback.

ask_user is mandatory when
- Two or more plausible interpretations exist.
- Multiple execution paths produce different outcomes.
- Required parameter for next action is missing.
- User must choose priority, trade-off, or strategy.
- Action is risky, expensive, irreversible, or user-visible.
- You would otherwise write: "Would you like...", "Should I...", "Which option...", "Proceed with...".

Decision vs info
- Decision changes outcome -> ask_user.
- Pure informational clarification with no outcome change -> plain text allowed.

Confidence gate
- If uncertainty is meaningful and more than one valid next action exists, use ask_user.
- Do not guess when user preference changes execution.

Question typing
- type=confirm: yes/no.
- type=select: exactly one option.
- type=multiSelect: multiple options.
- type=freeText: only if options cannot be enumerated.
- type=secret: sensitive input.
- Use type as canonical field. Do not use inputType in policy/examples.

Question format
- Keep question short, specific, action-oriented.
- For select/multiSelect always include concrete options.
- Keep options clear and actionable.
- One ask_user call = one decision topic.

Forbidden
- Plain-text decision menus while ask_user available.

Recovery
- If ask_user fails or unavailable: state fallback explicitly, ask one concise plain-text question.
- If ask_user answer is ambiguous: do one follow-up ask_user clarification, then fallback if still unclear.

After answer
- Restate chosen option in one short sentence.
- Continue execution immediately.

Examples
- Need yes/no -> ask_user { type: confirm, question: "Proceed with migration now?" }
- Need one of many -> ask_user { type: select, question: "Which prioritization mode should I use?", options: ["Eisenhower Matrix", "MoSCoW Method", "Standard order"] }
- Need many -> ask_user { type: multiSelect, question: "Which priorities should I optimize first?", options: ["Security", "Performance", "User Experience", "Delivery Speed"] }
```

## Implementation Plan

1. Prompt policy update
- Replace current ask_user policy text with hardened policy above.
- Ensure policy injected only when ask_user tool available.

2. Add execution-level guardrails
- Add lightweight decision detector in orchestration loop/prompt assembly stage.
- If decision cue present and ask_user available, bias strongly toward tool call.

3. Telemetry additions
- ask_user.policy_triggered
- ask_user.policy_bypassed_plaintext_question
- ask_user.followup_clarification_count
- ask_user.fallback_plaintext_count

4. Validation and compatibility checks
- Enforce canonical field type in examples and docs.
- Keep inputType compatibility in parser, but never recommend in policy docs.

5. Testing plan
- Unit tests:
  - Decision cue -> ask_user required behavior in prompt policy tests.
  - Confirm/select/multiSelect/freeText/secret recommendation coverage.
  - Ambiguous reply -> one follow-up ask_user.
- Integration tests:
  - End-to-end conversation where agent previously asked plain-text menu now emits ask_user select.
  - Risky action always emits ask_user confirm before tool call.
- Regression tests:
  - Non-decision informational questions remain plain text.
  - ask_user unavailable path uses explicit fallback text question.

6. Rollout plan
- Stage in development branch.
- Canary with telemetry monitoring for 24-48h.
- Check drop in plain-text decision questions.
- Full rollout after stable metrics.

## Success Criteria
- Significant reduction of plain-text decision questions when ask_user is available.
- No increase in dead-end interactions.
- Higher completion speed in guided flows.
- Stable fallback behavior when ask_user unavailable.
