# CodingRules

Roslyn analyzers and code fixes for the C# coding standards in
[FindJobHelper's `AGENTS.md`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/AGENTS.md).

The [spec](docs/spec.md) records the complete set of rules. Implementation
will be split into tickets. Diagnostics are warnings by default, and consumers
can change severity through `.editorconfig`.

This repository does not have CI yet.
