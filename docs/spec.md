# Coding rules analyzer spec

Status: accepted for implementation.

## Source and scope

The source policy is the coding standards section of
[FindJobHelper's `AGENTS.md` at `0f579ef`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/AGENTS.md#coding-standards).
This spec records every rule in that section, including rules that need
judgment and may require a later implementation ticket. The analyzer applies
to hand-written C# in production code, tests, and code generators. It skips
generated code. Diagnostics are warnings by default and may be reconfigured
through `.editorconfig`.

## Rule catalogue

### Compatibility and API design

- Do not preserve backward compatibility by default. Breaking an API and
  updating callers is acceptable unless compatibility is requested. This is a
  decision rule for maintainers; it does not identify a syntax pattern by
  itself.
- Use named arguments when a call has multiple arguments of the same type and
  positional order does not make their roles clear.
- Do not introduce constructor overloads solely to accommodate dependency
  injection or tests. Update the existing constructor and callers, or use a
  builder when several optional configurations justify one.

### Linear operations and conditions

- Keep each statement focused on one conceptual operation.
- An inline condition may have at most two simple checks when both concern the
  same value and express one invariant, such as a range.
- Split checks about different values into separate guards. Do not combine
  `&&` and `||` in one inline condition.
- Do not combine parsing, lookup, conversion, validation, or mutation inside
  one condition. Move repeated alternatives into a helper, loop, or another
  linear form.
- Do not nest one conceptual operation inside the arguments of another.
  Calculate, convert, look up, validate, format, and join before a method or
  constructor call. Direct values and expressions with at most one simple
  calculation operator may remain inline. Assign conditional expressions to
  named variables first.

### Pipelines and lambdas

- Linear LINQ and similar declarative pipelines are allowed. Prefer simple
  lambdas for their stages.
- A projection requiring several operations should use a block lambda with
  named intermediate values.
- Move logic with nested conditionals or several temporary values unused by
  the containing method to a local function. One intermediate value may stay
  in the containing method. Move reusable logic to a private method.
- Shape final collections explicitly, including named key and value
  selectors when relevant.

### Return decisions

- After a conditional early return, later decisions among return outcomes
  should use `if` or `switch` statements with explicit returns. A final
  predicate, nullable expression, or ternary that still chooses among outcomes
  violates the rule, regardless of return type.
- Returning a value already known to represent one outcome is allowed. A lone
  conditional return with no preceding conditional return is also allowed.
- For a final lookup that may be absent, test the result and return the found
  value in its own branch, then explicitly return the fallback. Match the
  direction of earlier branches that return found values.

### Sentinel returns

- Application-owned functions beyond private helpers should express absence
  or failure in the return type rather than returning a primitive sentinel
  such as `-1`.
- A private helper may use a sentinel. A primitive-returning caller must check
  the helper's result before forwarding its own sentinel explicitly.
- Signatures imposed by dependencies are exempt.

### Text handling

- Prefer spans for text inspection, slicing, and trimming. Accept and return
  spans when the string need not be owned and the lifetime permits it. Create
  a string when ownership is needed.

## Reference cases to verify

These examples come from the linked FindJobHelper branch and serve as
acceptance cases for the return rule.

- [`ApplicationIndexStore.Field`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/ApplicationIndexStore.cs#L200-L210)
  and [`GetField`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/ApplicationIndexStore.cs#L349-L359)
  return a ternary after an earlier guard. Both should warn.
- [`FolderOpener.ParseWslPathOutput`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/FolderOpener.cs#L194-L203)
  has the same shape and should warn.
- [`Program.IsUnsafeFileName`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/Program.cs#L419-L432)
  returns a predicate after two guards and should warn.
- The lone ternary in [`Program.cs`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/Program.cs#L313-L317)
  should not warn. The explicit final `if` and fallback in
  [`ExperienceDatabaseShadow`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/ExperienceDatabaseShadow.cs#L81-L105)
  should not warn either.
- The private `IndexFor` helper's `-1` fallback in
  [`ApplicationIndexStore`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/ApplicationIndexStore.cs#L363-L373)
  is allowed by the sentinel exception.

## First implementation: explicit return decisions

Diagnostic `CR0001` is a warning by default. It checks block-bodied methods,
accessors, local functions, and lambdas independently. A preceding top-level
`if` guard in the same body must have a branch that returns unconditionally.
The analyzer does not treat a return inside a nested function or deeper control
flow as a guard for the containing body. Generated code is excluded.

After such a guard, the final return is a candidate when it contains a
ternary, null-coalescing expression, boolean predicate or boolean-returning
call, or nullable-returning call. A plain `return value;` and a lone ternary
without an earlier guard are allowed. Switch expressions and more complex
control flow are deferred to later tickets. The analyzer reports a candidate
only when the code fix can rewrite it without changing evaluation order or
the number of times a subexpression runs.

The fix replaces the final decision with explicit `if` branches and returns.
It must preserve the original values, side effects, and comments. Analyzer
and code-fix tests cover positive cases, negative cases, generated code,
nested function boundaries, and fixes. The local package will also be tested
against the linked FindJobHelper branch without modifying that checkout.

Acceptance tests include:

- A final ternary returning a found value or `null` after a guard reports a
  warning and fixes to an explicit found-value branch and fallback.
- A final boolean predicate or boolean-returning call after a guard reports a
  warning and fixes to explicit `true` and `false` returns.
- A final `??` or nullable-returning call after a guard reports when its
  expression can be evaluated once and rewritten with an explicit fallback.
- The same patterns work in a block-bodied local function, accessor, and
  lambda, without a nested function's guard affecting its parent.
- A lone conditional return, a plain value return, generated code, and a
  switch expression do not report in this ticket. A conditional return inside
  deeper control flow also does not report in this ticket; it reports from
  ticket #2 onward through the nested return decision rules below.
- Fixes preserve comments and do not change the number or order of evaluations
  of calls and property accesses.

For nullable fallbacks, the fix returns the found value from an `if` branch and
leaves the fallback as the final return. For example, after an earlier guard,
`return value.Length == 0 ? null : value;` becomes
`if (value.Length != 0) return value; return null;` with normal C# block
formatting. The fix may invert the condition to keep this branch direction.

## Nested return decisions (ticket 2)

Diagnostics `CR0003` through `CR0006` report a return whose expression decides
among outcomes when it sits deeper than the function body's top level, for
example inside a `lock`, an `if` or `else` branch, a loop, `try`/`catch`/
`finally`, a `switch` statement case arm, or a nested block. Nesting alone
triggers the warning; a preceding guard is not required. `CR0003` covers
ternaries, `CR0004` null-coalescing expressions, `CR0005` nullable-returning
calls, and `CR0006` boolean predicates and boolean-returning calls. Returns
inside nested function bodies (lambdas, local functions, and anonymous
methods) are not affected by the containing body's scope, and switch-expression
returns are allowed style.

Each rule offers one canonical fix that rewrites the return in place into
explicit `if` statements and returns. The fix preserves evaluation order and
the number of times every subexpression runs, and all generated code is
formatted through the document's `.editorconfig` options. A ternary whose
branch throws, and a null-coalescing expression whose right side throws, are
reported and fix to a guard whose branch throws, followed by a return of the
remaining arm or the saved value. Generated local declarations inserted into
a switch section are wrapped in a block, because all sections of a switch
share one declaration space. Final-position decision returns stay governed by
`CR0001` alone; the nested rules never re-report them. Generated code and the
conservative skips of the first implementation (comments or directives inside
the decision, ref returns, and non-identity return conversions) still apply.

## Delivery decisions

- Publish the source in the public `AntonC9018/CodingRules` repository.
- Package normal analyzer and code fix consumers, and test diagnostics and
  fixes. The first implementation can use direct package references.
- Implement explicit return decisions and their code fix in the first ticket.
  Track the remaining rule families as GitHub issues linked to this spec.
- After the first implementation passes its tests, identify the dependencies
  needed by a normal analyzer, add the reusable ones to the local
  `Anton.SourceGeneration.Sdk`, and consume that SDK from a local NuGet feed.
- Do not add CI to this repository in this phase. Do not push changes to the
  SourceGenerators repository in this phase.

## Implementation tickets

[Ticket 1](https://github.com/AntonC9018/CodingRules/issues/1) implements
`CR0001` as described above. Follow-up tickets cover
[conditional returns nested beyond the top level](https://github.com/AntonC9018/CodingRules/issues/2),
[inline conditions](https://github.com/AntonC9018/CodingRules/issues/3),
[statement and argument structure](https://github.com/AntonC9018/CodingRules/issues/4),
[pipelines and lambdas](https://github.com/AntonC9018/CodingRules/issues/5),
[named arguments](https://github.com/AntonC9018/CodingRules/issues/6),
[constructor overloads](https://github.com/AntonC9018/CodingRules/issues/7),
[sentinels](https://github.com/AntonC9018/CodingRules/issues/8), and
[spans](https://github.com/AntonC9018/CodingRules/issues/9). The
compatibility policy stays documented without a diagnostic because no syntax
pattern can establish whether a change needed backward compatibility.
