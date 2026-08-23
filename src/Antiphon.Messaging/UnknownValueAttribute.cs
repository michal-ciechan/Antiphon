namespace Antiphon.Messaging;

/// <summary>
/// Names the enum member a tolerant reader should use when the wire carries an unknown name.
/// Declared next to the enum so adding a member stays a minor change instead of dropping the message.
/// </summary>
[AttributeUsage(AttributeTargets.Enum, Inherited = false)]
public sealed class UnknownValueAttribute : Attribute
{
    public UnknownValueAttribute(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        MemberName = memberName;
    }

    public string MemberName { get; }
}
