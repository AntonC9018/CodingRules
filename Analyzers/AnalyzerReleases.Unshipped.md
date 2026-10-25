### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
CR0001 | Readability | Warning | Make return decisions explicit after a guard clause
CR0003 | Readability | Warning | Make nested ternary return decisions explicit
CR0004 | Readability | Warning | Make nested null-coalescing return decisions explicit
CR0005 | Readability | Warning | Make nested nullable-returning call decisions explicit
CR0006 | Readability | Warning | Make nested boolean return decisions explicit
CR0200 | Readability | Warning | Make mixed Boolean conditions explicit
CR0201 | Readability | Warning | Split long Boolean conditions
CR0202 | Readability | Warning | Separate checks on different values, including simple type and single-constant typed property-pattern subjects
CR0203 | Readability | Warning | Separate producing, transforming and validating operations; preserve terminal throw guards, name the reviewed HTTP status filter and stage framework IServiceProvider.GetService null checks
CR0204 | Readability | Warning | Linearize three or more repeated alternatives
CR0300 | Readability | Warning | Calculate nested arguments in named steps, including resolved Type.GetGenericArguments metadata calculations
CR0301 | Readability | Warning | Assign conditional values to named variables, including composed LINQ branches (ScheduleLib A7)
CR0302 | Readability | Warning | Separate combined statement operations, including calculations consuming resolved Type.GetGenericArguments metadata
CR0303 | Readability | Warning | Name Boolean values assigned by exhaustive adjacent branches with a validated local-function extraction
CR0400 | Readability | Warning | Name structurally composed delegate projection stages
CR0401 | Readability | Warning | Extract whole delegate stages with several result-feeding temporaries or nested decisions
CR0402 | Readability | Warning | Make materializer key and element selectors explicit
CR0500 | Readability | Warning | Name arguments with ambiguous same-type roles, including ordinary and target-typed return construction (ScheduleLib A9 regression coverage)
CR0600 | Readability | Warning | Represent proven index-search absence in the return type
CR0601 | Readability | Warning | Check a private helper's proven index-search sentinel before forwarding
CR0700 | Readability | Warning | Use a span for transient text inspection
CR0800 | Formatting | Warning | Place wrapped Boolean operators at the start of the continuation line
CR0801 | Formatting | Warning | Place object initializer members on separate lines
CR0802 | Formatting | Warning | Keep a sole constructor argument adjacent to the call parenthesis
CR0805 | Formatting | Warning | Start every subsequent invocation in a fluent chain on a new line
CR0900 | Readability | Warning | Represent tuple method results, inputs and stored collection elements with a named record struct (manual migration)
CR1000 | Readability | Warning | Group more than three parameters on locally owned extensible contracts (manual migration)
CR1001 | Readability | Warning | Use a configuration builder for multi-parameter overloads differing in exactly one parameter type (manual migration)
CR1100 | Readability | Warning | Prefer interpolation for runtime built-in string-only concatenations
CR1200 | Readability | Warning | Make proven unchanged assignment branches explicit conditional updates
CR1300 | Readability | Warning | Review repeated timestamp formatting for shared meaning (manual duplication candidate)
CR1400 | Readability | Warning | Review concrete classes for sealing, including inherited test seams (manual architectural finding; adapted MA0053)

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------

### Changed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------

CR1500 | Readability | Warning | Implement non-generic collection members explicitly on generic collections
CR1600 | Readability | Warning | Expose interpolated exception context through typed properties
CR0803 | Readability | Warning | Require braces around embedded statements
CR0804 | Readability | Warning | Require spacing after casts

CR1000 | Readability | Warning | Accept explicitly marked reflection delegates whose signatures are imposed by external members
