# Contributing

Thanks for helping grow the handbook.

## Rules

1. **Educational examples only.** Write fresh, minimal examples. Do not paste game source, decompiled output, extracted assets, CDB rows, or save bytes.
2. **Generalize, don't leak.** You may describe workflow patterns learned from research (hook order, fail-open guards, rollback checks), but rewrite all code from scratch.
3. **Label verification.** Mark each tutorial as `concept`, `offline-checked`, `boot-verified`, `live-observed`, or `director-accepted` so readers know what is proven.
4. **Safety first.** Any tutorial that launches the game must include: back up saves, launch only via the modded launcher, and full rollback steps.
5. **One topic per page.** Keep pages focused; link to DCCM docs for framework API truth.

## Page template

```markdown
# Title (status: concept)

## Goal
## Prerequisites
## Steps
## Why this works
## Common failures
## Safety / rollback
## Further reading
```
