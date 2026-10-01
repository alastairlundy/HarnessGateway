<!-- Thanks for the PR. Please complete the checklist below. -->

## What and why

<!-- What does this change do, and why is it needed? Link related issues. -->

## Checklist

- [ ] Tests added or updated, and the full suite passes
      (`dotnet test --solution src/HarnessGateway.slnx`)
- [ ] Exit-code impact considered. The exit-code table in the README is an
      append-only contract, so no existing code was renumbered or repurposed,
      and any new code gets a new row in that table
- [ ] README impact considered. User-facing changes are documented in this PR,
      or the PR has nothing a user can see or type. The README must match the
      shipped CLI surface

## AI-usage disclosure

Required. Select exactly one option:

- [ ] **No AI usage.** No AI tools were used for this pull request.
- [ ] **AI usage.** AI tools were used. Fill in both fields:
  - Model or tool used: <!-- e.g. GitHub Copilot, Claude, GPT-... -->
  - How it was used and what it produced: <!-- e.g. drafted the test scaffold;
        I reviewed and rewrote the assertions -->
