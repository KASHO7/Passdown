using System.Security.Cryptography;
using System.Text;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Infrastructure.Updates;
using Carowinds.Passdown.UpdateSigning;

namespace Carowinds.Passdown.Infrastructure.Tests.Updates;

/// <summary>Phase 23D: authenticity is checked before any manifest content is trusted; every doubt is a rejection.</summary>
public sealed class UpdateSignatureVerificationTests : IDisposable
{
    private readonly UpdateTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private byte[] Manifest(string version = "0.1.0-rc.8") => UpdateTestKit.CreateManifest(_kit.CreateZip(version), version);

    [Fact]
    public void ValidSignature_Verifies_AndManifestDescribesThePackage()
    {
        var manifest = Manifest();
        var result = _kit.Verifier().Verify(manifest, _kit.Sign(manifest));

        Assert.True(result.IsVerified);
        Assert.Equal(UpdateTestKit.KeyId, result.KeyId);
        Assert.Equal("0.1.0-rc.8", result.Manifest!.Version.Normalized);
        Assert.Equal(UpdateTestKit.AppFiles("0.1.0-rc.8").Count, result.Manifest.Files.Count);
    }

    [Fact]
    public void TamperedManifest_IsRejected()
    {
        var manifest = Manifest();
        var signature = _kit.Sign(manifest);
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("0.1.0-rc.8", "0.1.0-rc.9", StringComparison.Ordinal));

        Assert.Equal(ManifestRejection.InvalidSignature, _kit.Verifier().Verify(tampered, signature).Rejection);
    }

    [Fact]
    public void SignatureFromAnUntrustedKey_IsRejected()
    {
        var manifest = Manifest();
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal(ManifestRejection.InvalidSignature, _kit.Verifier().Verify(manifest, UpdateTestKit.SignWith(attacker, manifest, UpdateTestKit.KeyId)).Rejection);
        Assert.Equal(ManifestRejection.UnknownKey, _kit.Verifier().Verify(manifest, UpdateTestKit.SignWith(attacker, manifest, "attacker-key")).Rejection);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"formatVersion\":1,\"algorithm\":\"RSA\",\"keyId\":\"test-key-1\",\"signature\":\"AAAA\"}")]
    [InlineData("{\"formatVersion\":2,\"algorithm\":\"ECDSA-P256-SHA256\",\"keyId\":\"test-key-1\",\"signature\":\"AAAA\"}")]
    [InlineData("{\"formatVersion\":1,\"algorithm\":\"ECDSA-P256-SHA256\",\"keyId\":\"test-key-1\",\"signature\":\"AAAA\"}")]
    [InlineData("{\"formatVersion\":1,\"algorithm\":\"ECDSA-P256-SHA256\",\"keyId\":\"../../etc\",\"signature\":\"AAAA\"}")]
    public void MalformedSignatureFile_IsRejected(string signature) =>
        Assert.Equal(ManifestRejection.MalformedSignature, _kit.Verifier().Verify(Manifest(), Encoding.UTF8.GetBytes(signature)).Rejection);

    [Fact]
    public void NoTrustedKeys_RejectsEverything()
    {
        var manifest = Manifest();
        var verifier = new EcdsaUpdateManifestVerifier([]);
        Assert.False(verifier.HasTrustedKeys);
        Assert.Equal(ManifestRejection.NoTrustedKeys, verifier.Verify(manifest, _kit.Sign(manifest)).Rejection);
    }

    [Fact]
    public void InvalidConfiguredKeys_AreIgnored_NotTrusted()
    {
        using var rsa = RSA.Create();
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var verifier = new EcdsaUpdateManifestVerifier([
            new TrustedUpdateKey("garbage", "not-base64!"),
            new TrustedUpdateKey("rsa", Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())),
            new TrustedUpdateKey("p384", Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo())),
            new TrustedUpdateKey("bad id!", _kit.PublicKey),
        ]);
        Assert.False(verifier.HasTrustedKeys);
    }

    [Fact]
    public void KeyRotation_BothKeysTrusted_EitherSignatureVerifies()
    {
        var manifest = Manifest();
        using var next = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new EcdsaUpdateManifestVerifier([_kit.TrustedKey, new TrustedUpdateKey("next-key", Convert.ToBase64String(next.ExportSubjectPublicKeyInfo()))]);

        Assert.True(verifier.Verify(manifest, _kit.Sign(manifest)).IsVerified);
        Assert.True(verifier.Verify(manifest, UpdateTestKit.SignWith(next, manifest, "next-key")).IsVerified);

        // Revocation: a build that no longer lists the old key rejects its signatures.
        var revoked = new EcdsaUpdateManifestVerifier([new TrustedUpdateKey("next-key", Convert.ToBase64String(next.ExportSubjectPublicKeyInfo()))]);
        Assert.Equal(ManifestRejection.UnknownKey, revoked.Verify(manifest, _kit.Sign(manifest)).Rejection);
    }

    private ManifestRejection VerifyEdited(Func<string, string> edit)
    {
        var json = edit(Encoding.UTF8.GetString(Manifest()));
        var bytes = Encoding.UTF8.GetBytes(json);
        return _kit.Verifier().Verify(bytes, _kit.Sign(bytes)).Rejection;
    }

    [Fact]
    public void SignedManifestForAnotherProduct_IsRejected() =>
        Assert.Equal(ManifestRejection.WrongProduct, VerifyEdited(j => j.Replace("\"carowinds-operations\"", "\"other-product\"", StringComparison.Ordinal)));

    [Theory]
    [InlineData("win-arm64")]
    [InlineData("win-x86")]
    [InlineData("osx-arm64")]
    public void SignedManifestForAnotherArchitecture_IsRejected(string rid) =>
        Assert.Equal(ManifestRejection.WrongArchitecture, VerifyEdited(j => j.Replace("\"win-x64\"", $"\"{rid}\"", StringComparison.Ordinal)));

    [Fact]
    public void ReleaseCandidateVersionClaimingStable_IsRejected() =>
        Assert.Equal(ManifestRejection.InconsistentChannel, VerifyEdited(j => j.Replace("\"release-candidate\"", "\"stable\"", StringComparison.Ordinal)));

    [Fact]
    public void UnsupportedManifestFormat_IsRejected() =>
        Assert.Equal(ManifestRejection.UnsupportedFormat, VerifyEdited(j => j.Replace("\"formatVersion\": 1", "\"formatVersion\": 2", StringComparison.Ordinal)));

    [Theory]
    [InlineData("\"Carowinds.Passdown.Desktop.exe\"", "\"../Carowinds.Passdown.Desktop.exe\"")]
    [InlineData("\"LatoFont/OFL.txt\"", "\"C:/Windows/OFL.txt\"")]
    [InlineData("\"LatoFont/OFL.txt\"", "\"/etc/OFL.txt\"")]
    [InlineData("\"LatoFont/OFL.txt\"", "\"LatoFont\\\\OFL.txt\"")]
    [InlineData("\"LatoFont/OFL.txt\"", "\"OFL.txt:stream\"")]
    [InlineData("\"LatoFont/OFL.txt\"", "\"CON.txt\"")]
    [InlineData("\"LatoFont/OFL.txt\"", "\".carowinds-update/x.txt\"")]
    [InlineData("\"LatoFont/OFL.txt\"", "\"coreclr.dll\"")]
    public void SignedManifestWithUnsafeOrDuplicatePath_IsRejected(string from, string to) =>
        Assert.Equal(ManifestRejection.MalformedManifest, VerifyEdited(j => j.Replace(from, to, StringComparison.Ordinal)));

    [Fact]
    public void ManifestWithoutTheUpdater_IsRejected()
    {
        var files = UpdateTestKit.AppFiles("0.1.0-rc.8");
        files.Remove("Carowinds.Passdown.Updater.exe");
        var zip = _kit.CreateZip("0.1.0-rc.8", files);
        Assert.Throws<ReleaseSigningException>(() => UpdateTestKit.CreateManifest(zip, "0.1.0-rc.8"));
    }
}
