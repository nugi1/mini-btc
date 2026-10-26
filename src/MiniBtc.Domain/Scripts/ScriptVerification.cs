namespace MiniBtc.Domain.Scripts;

public enum ScriptStatus
{
    /// <summary>Our own code ran the script and it succeeded.</summary>
    Verified,

    /// <summary>A script form we don't verify; accepted on Core's word and flagged.</summary>
    NotVerified,

    /// <summary>The script fails: the block is invalid.</summary>
    Failed,
}

/// <param name="Status">The outcome.</param>
/// <param name="Detail">For Failed: Core's script error string. For NotVerified: why it was skipped.</param>
public readonly record struct ScriptVerification(ScriptStatus Status, string? Detail)
{
    public static ScriptVerification Verified => new(ScriptStatus.Verified, null);
    public static ScriptVerification NotVerified(string reason) => new(ScriptStatus.NotVerified, reason);
    public static ScriptVerification Failed(string scriptError) => new(ScriptStatus.Failed, scriptError);
}

/// <summary>Core's ScriptErrorString texts (script/script_error.cpp) for the failures we detect.</summary>
public static class ScriptErrors
{
    public const string EvalFalse = "Script evaluated without error but finished with a false/empty top stack element";
    public const string EqualVerify = "Script failed an OP_EQUALVERIFY operation";
    public const string SigDer = "Non-canonical DER signature";
    public const string WitnessUnexpected = "Witness provided for non-witness script";
}
