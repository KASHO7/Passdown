using System.Security.Cryptography;
using System.Text.Json;
using Carowinds.Passdown.Application.Updates;

namespace Carowinds.Passdown.Infrastructure.Updates;

/// <summary>
/// Phase 23D: verifies a detached manifest signature with ECDSA P-256 / SHA-256 against the public keys
/// bundled in this build (appsettings.json "Updates:TrustedSigningKeys"). The private key never exists in
/// the application, the repository or the release ZIP — only the release signer holds it.
/// The signature file is a small JSON document:
/// <c>{ "formatVersion": 1, "algorithm": "ECDSA-P256-SHA256", "keyId": "…", "signature": "&lt;Base64, IEEE P1363 r||s&gt;" }</c>.
/// Key rotation: ship a build that trusts both the old and the new key, then sign with the new key.
/// Revocation: ship a build that no longer lists the compromised key (see the phase document).
/// </summary>
public sealed class EcdsaUpdateManifestVerifier : IUpdateManifestVerifier
{
    public const string Algorithm = "ECDSA-P256-SHA256";

    public const int SignatureFormatVersion = 1;

    /// <summary>IEEE P1363 r||s for P-256.</summary>
    private const int SignatureLength = 64;

    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    public EcdsaUpdateManifestVerifier(IEnumerable<TrustedUpdateKey> trustedKeys)
    {
        foreach (var key in trustedKeys)
        {
            // An unusable configured key is ignored rather than trusted; a build with only bad keys has none.
            if (TryDecodeP256PublicKey(key.PublicKeySpkiBase64, out var spki) && IsValidKeyId(key.KeyId))
            {
                _keys[key.KeyId] = spki;
            }
        }
    }

    public bool HasTrustedKeys => _keys.Count > 0;

    public ManifestVerificationResult Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signatureBytes)
    {
        if (!HasTrustedKeys)
        {
            return ManifestVerificationResult.Rejected(ManifestRejection.NoTrustedKeys);
        }

        if (!TryReadSignature(signatureBytes, out var keyId, out var signature))
        {
            return ManifestVerificationResult.Rejected(ManifestRejection.MalformedSignature);
        }

        if (!_keys.TryGetValue(keyId, out var spki))
        {
            return ManifestVerificationResult.Rejected(ManifestRejection.UnknownKey);
        }

        using (var ecdsa = ECDsa.Create())
        {
            ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
            if (!ecdsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return ManifestVerificationResult.Rejected(ManifestRejection.InvalidSignature);
            }
        }

        // Only now — after authenticity is established — is the manifest content parsed and trusted.
        var manifest = UpdateManifestJson.TryParse(manifestBytes, out var rejection, out var detail);
        return manifest is null
            ? ManifestVerificationResult.Rejected(rejection, detail)
            : ManifestVerificationResult.Verified(manifest, keyId);
    }

    /// <summary>Key ids are short labels such as "carowinds-release-2026"; restricted so they are safe to log.</summary>
    public static bool IsValidKeyId(string? keyId) =>
        keyId is { Length: >= 1 and <= 64 } && keyId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    public static bool TryDecodeP256PublicKey(string? base64, out byte[] spki)
    {
        spki = [];
        if (string.IsNullOrWhiteSpace(base64) || base64.Length > 512)
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(bytes, out var read);
            var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
            if (read != bytes.Length || !parameters.Curve.IsNamed || parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            {
                return false;
            }

            spki = bytes;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static bool TryReadSignature(ReadOnlySpan<byte> bytes, out string keyId, out byte[] signature)
    {
        keyId = string.Empty;
        signature = [];
        if (bytes.Length == 0 || bytes.Length > UpdateDiscoveryService.MaximumSignatureBytes)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 4, AllowDuplicateProperties = false });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("formatVersion", out var format) || format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out var formatVersion) || formatVersion != SignatureFormatVersion
                || !root.TryGetProperty("algorithm", out var algorithm) || algorithm.ValueKind != JsonValueKind.String || algorithm.GetString() != Algorithm
                || !root.TryGetProperty("keyId", out var key) || key.ValueKind != JsonValueKind.String || !IsValidKeyId(key.GetString())
                || !root.TryGetProperty("signature", out var value) || value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var decoded = Convert.FromBase64String(value.GetString()!);
            if (decoded.Length != SignatureLength)
            {
                return false;
            }

            keyId = key.GetString()!;
            signature = decoded;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }
}
