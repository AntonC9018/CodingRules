namespace CodingRules;

public static class DiagnosticIds
{
    public const string ExplicitReturnDecision = "CR0001";
    public const string NestedTernaryReturn = "CR0003";
    public const string NestedCoalesceReturn = "CR0004";
    public const string NestedNullableCallReturn = "CR0005";
    public const string NestedBooleanReturn = "CR0006";
    public const string MixedConditionOperators = "CR0200";
    public const string ExcessConditionChecks = "CR0201";
    public const string IndependentConditionChecks = "CR0202";
    public const string CombinedConditionOperations = "CR0203";
    public const string RepeatedConditionAlternatives = "CR0204";
    public const string NestedArgumentOperation = "CR0300";
    public const string UnnamedConditionalValue = "CR0301";
    public const string CombinedStatementOperations = "CR0302";
    public const string ComposedProjection = "CR0400";
    public const string StageImplementation = "CR0401";
    public const string ExplicitCollectionSelectors = "CR0402";
    public const string NamedArgumentRoles = "CR0500";
    public const string PrimitiveSentinelReturn = "CR0600";
    public const string UncheckedHelperSentinel = "CR0601";
    public const string TransientTextInspection = "CR0700";
}
