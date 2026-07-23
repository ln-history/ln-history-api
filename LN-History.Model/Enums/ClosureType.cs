namespace LN_History.Model.Enums;

/// <summary>
/// Reason a channel was closed. Mirrors the <c>closure_reason</c> Postgres enum
/// (<c>mutual | force | breach | unknown</c>) on <c>channel_closures.type</c>.
/// </summary>
public enum ClosureType
{
    Mutual,
    Force,
    Breach,
    Unknown
}
