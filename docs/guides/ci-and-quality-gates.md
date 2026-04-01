# CI And Quality Gates

CI for Nexus should verify that control-flow-heavy runtime code remains correct under change.

## Recommended Gates

- full solution build
- full solution test run
- coverage collection with enforced thresholds after stabilization
- benchmark execution on a controlled cadence
- docs index validation when new guides, recipes, or API docs are added

## Practical Threshold Strategy

- start with realistic line and branch thresholds for core runtime packages
- raise thresholds per package once flaky edges are removed
- prefer package-specific thresholds over one global vanity number

## Pull Request Expectations

- new runtime branches should come with tests
- new docs should be linked from the main indexes
- source-backed recipes should include a runnable example and a test path
- benchmark changes should report impact when hot paths are affected

## Related Docs

- [Testing](testing.md)
- [Performance And Benchmarking](performance-and-benchmarking.md)