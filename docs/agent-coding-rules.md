# CodingRules guidance for agents

This file complements analyzer diagnostics with judgments that syntax analysis
cannot establish. It is source guidance for inclusion in the CodingRules
consumer offering; a package reference alone does not make an agent read it.

## Review duplication by meaning

A duplication finding identifies a candidate for reuse, not proof that the
matched expressions represent the same domain concept. Before extracting a
common helper, compare what each expression means, which rules govern it,
and why each is likely to change.

- Share a helper when the sites express the same operation and policy, such
  as a deliberately common timestamp representation.
- Keep sites separate when they express semantically distinct operations,
  even if their current implementations happen to match. Independent naming
  or helpers may make that distinction clear without coupling their policies.
- Do not merge separately meaningful helpers merely to silence a structural
  duplication diagnostic. Use a narrowly scoped suppression with an explanation
  when needed to preserve an intentional distinction.
- Preserve receiver evaluation, formatting, culture, exceptions, and branch
  timing. Sharing an operation does not mean eagerly computing all of its
  results before their original use sites.

Apply this review to each candidate before a batch refactor. Infer the domain
meaning from the code and its documented model; ask the maintainer only when
that meaning remains uncertain. An analyzer's inability to prove a semantic
distinction does not make the distinction unimportant.
