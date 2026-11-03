using System;

namespace CodingRules;

/// <summary>Identifies a delegate whose signature is imposed by an external member.</summary>
[AttributeUsage(AttributeTargets.Delegate, AllowMultiple = false, Inherited = false)]
public sealed class ExternalSignatureAttribute : Attribute
{
    public ExternalSignatureAttribute(Type declaringType, string memberName)
    {
        DeclaringType = declaringType;
        MemberName = memberName;
    }

    public Type DeclaringType { get; }
    public string MemberName { get; }
}
