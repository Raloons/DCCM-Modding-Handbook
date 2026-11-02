# Debugging and Testing (planned)

Diagnosing crashes, compatibility issues, and safe testing.

Planned pages:

- Reading `coremod/logs/` first; hook-attach lines as smoke signals.
- Isolating failures: disable-half, minimal repro mod, disposable fixture.
- Save backup / hash-verify / restore workflow before every modded launch.
- When NOT to test: production saves, Cloud sync on, no backup, no rollback plan.

> If a step can write to saves, Cloud, or the install tree, it needs a rollback section.
