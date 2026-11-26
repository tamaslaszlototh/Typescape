# Contributing to Typescape

Thanks for your interest. Typescape is maintained by one person, so this page keeps things simple.

## Before you start

- **Small fixes** (a typo, an obvious bug, a clearer message) can come straight as a pull request.
- **Anything larger** (a new feature, a new rule or metric, a change to the Snapshot format, a refactoring) needs an issue first, and the maintainer's agreement before you start. That saves you from writing code that cannot be merged.
- Security problems go through [private vulnerability reporting](https://github.com/tamaslaszlototh/Typescape/security/advisories/new), never a public issue. See [SECURITY.md](SECURITY.md).

## Building and testing

You need the .NET SDK 10 and Node (LTS), on macOS or Windows. Linux is not supported.

```
dotnet run build.cs -- test           # the full test suite
dotnet run build.cs -- install-hooks  # optional: run the fast tests before every push
```

Golden Snapshot tests compare the Analyzer's output with checked-in `*.verified.*` files. When a change is meant to alter the output, rerun the tests with `TYPESCAPE_ACCEPT_GOLDEN=1` to update them, and review the diff of the golden files as part of your change. The goldens must be identical on macOS and Windows.

## Pull requests

- CI must be green. It runs on macOS and Windows.
- A new or changed Dependency rule or metric needs rule tests, a golden update, or both.
- **Never copy or port code from SonarAnalyzer** (or Sonar's test files). Its licence is not compatible. Cognitive complexity here is implemented clean-room from the published whitepaper.
- Fill in the pull request template: the linked issue, what changed, and which tests you ran.
- The maintainer squash-merges every pull request, so the PR title and description become the commit message. Your name stays on the commit.

## Licence

There is no CLA and no DCO. By opening a pull request, you agree that your contribution is licensed under the project's [MIT licence](LICENSE), as the GitHub Terms of Service provide.

## Code of conduct

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
