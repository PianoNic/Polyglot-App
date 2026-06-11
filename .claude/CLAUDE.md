# Workflow rules (enforced)

- **Never work on `main`.** Create an issue (labeled) → branch `feature/<issue#>_PascalCase` or `fix/<issue#>_PascalCase` → PR (labeled) with `Closes #<issue>` → squash-merge + delete branch.
- **Use CLI generators whenever one exists.** `ng generate`, `dotnet new`, `gh issue create`, `gh pr create`, etc.
- **No AI / Claude attribution in commits or PRs. Ever.**
- **No test plans in PRs.** PR body is Summary + `Closes #<issue>` only.
- **Commit subject:** short imperative.
- **PR labels:** `bug`, `enhancement`, `refactor`, `stale`.
