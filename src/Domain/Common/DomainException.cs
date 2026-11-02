namespace PharmaTrack.Domain.Common;

/// <summary>A business rule was broken. Code is a stable identifier clients can rely on.</summary>
public sealed class DomainException(string code, params object[] args) : Exception(code)
{
    public string Code { get; } = code;
    public object[] Args { get; } = args;
}
