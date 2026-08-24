using System.Security.Cryptography;

namespace Qasedak.BuildingBlocks.Application.Auditing;

/// <summary>
/// Deterministic, salted fingerprints for audit records: the same input always maps to
/// the same short token (correlatable across entries) without storing or revealing the
/// original value. Application-level so every module can emit privacy-safe audits.
/// </summary>
public static class AuditRedaction
{
    public static string Fingerprint(string secret)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("qasedak::" + secret));
        return "fp_" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
