using System;

namespace CodingRules;

/// <summary>Allows positional arguments at calls to this exact method or constructor.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class AllowPositionalArgumentsAttribute : Attribute
{
    public AllowPositionalArgumentsAttribute() { }
}
