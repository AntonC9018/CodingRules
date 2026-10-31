# Accept short conditions and name generated steps with placeholders

The ScheduleLib probe of issue #265 showed that CR02 and CR03 fixes made code
harder to read. They extracted generic helpers such as `CalculateValue`, copied
unchanged values into new locals, split short guards and added blank lines.
We therefore narrow the findings and make the fixes produce reviewable steps.

Findings:

- `&&`/`||` with two or three simple, side-effect-free operands is accepted.
- One simple call, interpolation or `new T(...)` with simple arguments is
  accepted per argument list.
- Method chains with lambdas are accepted. CR0404 reports a chain lambda whose
  expression body nests another lambda, except catalogued predicates.
- Index arithmetic such as `a[j - 1]` and identity aliases are accepted.
- CR0701 replaces `Encoding.UTF8.GetBytes("literal")` with a `u8` literal.
  A `ReadOnlySpan<byte>` target receives the literal itself. An array stored in
  a `byte[]` variable, field, property or element is not reported. A returned
  array is declared as `byte[] ret = "text"u8.ToArray();` before the return; any
  other array use reads a `TODO_VARIABLE` declared before its statement. The copy
  never appears inline, and the fix is declined when it would change
  caller-argument text or caller line numbers.
- Expression-tree lambdas and query clauses stay inline: CR02, CR03, CR0404 and
  CR0701 neither report nor rewrite code inside them.

Fixes:

- Only conditions with more than three operands, or with side effects, are
  extracted. The result is a guard-clause local function, without a Boolean
  result variable.
- An assignment inside a condition is hoisted into its own statement.
- Unchanged values are not aliased and simple arguments are not hoisted.
- Generated output keeps the source indentation, adds no blank lines and keeps
  a declaration's type and name on one line. Tests compare exact whitespace.

Generated names are placeholders from one shared helper: `TODO_CALCULATION`,
`TODO_VARIABLE` and `TODO_CONDITION`, with numeric suffixes on collision.
A fix cannot know the domain meaning, so a generic name that looks final is
worse than one that is obviously unfinished. CR1700 reports each declared
`TODO_` name as a warning, "Rename this extracted {kind} to a domain name".
Agents grep for `TODO_` after applying fixes and rename the results.

CR1700 is a warning, like the other rules; a consumer that treats warnings as
errors, such as ScheduleLib, cannot build a fixed file until the placeholders
are renamed. Expansion may still snapshot an earlier operand to preserve
evaluation order.
