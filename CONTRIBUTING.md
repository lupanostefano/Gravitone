# Contributing to Gravitone

Thanks for helping. Gravitone replaces the Windows taskbar, so the one rule above all others is: **a change must never
leave someone without a taskbar**. Keep that in mind when you touch `Core/TaskbarState.cs`, `Core/TaskbarReplacer.cs`,
`Core/Guard.cs` or anything that runs at exit, sign-out or start-up.

## Getting started

```bash
git clone https://github.com/lupanostefano/Gravitone.git
cd Gravitone
dotnet test Gravitone.slnx
```

You need Windows 11 and the .NET 9 SDK. If the taskbar ever stays hidden while you work: **Ctrl + Alt + Shift + B**, or
`bin\Debug\net9.0-windows\Gravitone.exe --restore-taskbar`.

## How a change gets in

1. Open an issue first for anything bigger than a small fix, so we agree on the approach.
2. Work on a branch; keep the pull request about one thing.
3. **Fixing a bug? Add a test that fails without your fix** (in `tests/Gravitone.Tests`) whenever the bug is in logic that can
   be tested without a window. For behaviour that needs the desktop, describe in the pull request how you checked it.
4. Before pushing:
   ```bash
   dotnet format Gravitone.slnx
   dotnet build Gravitone.slnx -warnaserror
   dotnet test Gravitone.slnx
   bin\Debug\net9.0-windows\Gravitone.exe --selftest
   ```
5. CI runs the same checks on every push and pull request, plus the installer build and CodeQL. `main` only accepts
   changes whose checks are green.

## Style

- Match the code around you: small classes, comments that say *why*, English identifiers and comments.
- Interface text goes through `Loc.T("English text")`; add the Italian line in `Core/Loc.cs` (the tests check that the
  placeholders match). Log messages are in English.
- No new dependency without a word in the pull request about its licence and why it is needed; list it in
  `THIRD-PARTY-NOTICES.md`.
- Graphics must be original: no Apple or Microsoft artwork.

## Commits

Clear, imperative subject lines ("Fix the dock jumping to another monitor after sleep"). Commits must be authored by
people: CI rejects commits that credit an AI assistant as author or co-author.

## Releases (maintainer)

Bump `<Version>` in `Gravitone.csproj`, then tag: `git tag v0.3.0 && git push --tags`. The release workflow builds and
tests again, then publishes the installer, the portable zip, `SHA256SUMS.txt` and build provenance.
