# Meziantou.Analyzer source reuse

`Analyzers/Sealing/SealConcreteClassAnalyzer.cs` adapts the candidate selection
and framework exclusions of [MA0053's ClassMustBeSealedAnalyzer.cs](https://github.com/meziantou/Meziantou.Analyzer/blob/decb1e62ae7205db62d8b6225220e108d05f52d4/src/Meziantou.Analyzer/Rules/ClassMustBeSealedAnalyzer.cs).
Pinned revision: `decb1e62ae7205db62d8b6225220e108d05f52d4`.
Copyright (c) Gérald Barré; MIT, reproduced in `LICENSE.txt`.

Changes: use CR1400 and repository warning/help-link conventions; analyze
hand-written source only; include public classes, exceptions, virtual members
and classes with inheritors; retain COM-import and externally defined benchmark
constraints with semantic identity checks; add the generic attribute-forwarding
exception. Reporting uses a symbol action because existing inheritance no longer
removes candidates. The separate forwarding proof is original CodingRules code.

No upstream binary, configuration surface or fixer is bundled. The original
fixer's unconditional modifier insertion is unsuitable for the added inherited
cases. CR1400 is manual, including leaf classes: absence of known inheritors in
one compilation does not prove that external subclasses, proxies or reflection
contracts can be changed safely. This is source reuse, not enabling MA0053 or
claiming its existing inheritance policy covers ScheduleLib A16.

The package includes this provenance and the MIT copyright/permission notice.
