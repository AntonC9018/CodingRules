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
}
