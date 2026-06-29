using System.Security.Cryptography;
using System.Text;

namespace DCS.Core.IR;

public static class RegistrationStatementFingerprint
{
    public static string Compute(string methodName, Lifetime lifetime, string typeArgumentSyntax)
    {
        var normalized = $"{NormalizeMethod(methodName)}|{lifetime}|{NormalizeSyntax(typeArgumentSyntax)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private static string NormalizeMethod(string methodName) =>
        methodName.Replace("TryAdd", "Add", StringComparison.Ordinal);

    private static string NormalizeSyntax(string syntax) =>
        string.Concat(syntax.Where(c => !char.IsWhiteSpace(c)));
}
