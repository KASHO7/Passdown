namespace Carowinds.Passdown.Application.Updates;

/// <summary>
/// Phase 23D: checks a release's manifest bytes against its detached signature using only the public keys
/// bundled in this build, then parses and validates the manifest. Fails closed: any doubt is a rejection.
/// </summary>
public interface IUpdateManifestVerifier
{
    /// <summary>True when at least one trusted key is configured; otherwise every manifest is rejected.</summary>
    bool HasTrustedKeys { get; }

    ManifestVerificationResult Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signatureBytes);
}

public enum ManifestRejection
{
    None,
    NoTrustedKeys,
    MalformedSignature,
    UnknownKey,
    InvalidSignature,
    MalformedManifest,
    UnsupportedFormat,
    WrongProduct,
    WrongArchitecture,
    InconsistentChannel,
}

public sealed record ManifestVerificationResult(UpdateManifest? Manifest, string? KeyId, ManifestRejection Rejection, string? Detail = null)
{
    public bool IsVerified => Manifest is not null && Rejection == ManifestRejection.None;

    public static ManifestVerificationResult Verified(UpdateManifest manifest, string keyId) => new(manifest, keyId, ManifestRejection.None);

    public static ManifestVerificationResult Rejected(ManifestRejection rejection, string? detail = null) => new(null, null, rejection, detail);
}
