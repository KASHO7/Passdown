using System.Globalization;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.UpdateSigning;

// Phase 23D release signing tool. See docs/phase23d-github-updater.md "Release preparation guide".
//
//   generate-key    --private-key-out <file outside the repo>
//   create-manifest --zip <release zip> --version 0.1.0-rc.8 --channel release-candidate --title <text> --notes <notes.md> [--published 2026-10-20T15:00:00Z]
//   sign            --manifest <file> --key-id <id> [--private-key <file>]   (or CAROWINDS_UPDATE_SIGNING_KEY_FILE)
//   verify          --manifest <file> --signature <file> --appsettings <src/Carowinds.Passdown.Desktop/appsettings.json>
try
{
    if (args.Length == 0)
    {
        return Usage();
    }

    var options = ParseOptions(args.Skip(1).ToArray());
    switch (args[0])
    {
        case "generate-key":
        {
            var publicKey = ReleaseSigning.GenerateKey(Required(options, "private-key-out"));
            Console.WriteLine("Private key written. Keep it offline and backed up; never commit or share it.");
            Console.WriteLine("Public key (add to Updates:TrustedSigningKeys in src/Carowinds.Passdown.Desktop/appsettings.json):");
            Console.WriteLine(publicKey);
            return 0;
        }

        case "create-manifest":
        {
            var zip = Required(options, "zip");
            var notes = File.ReadAllText(Required(options, "notes"));
            DateTimeOffset? published = options.TryGetValue("published", out var publishedText)
                ? DateTimeOffset.Parse(publishedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                : DateTimeOffset.UtcNow;
            var bytes = ReleaseSigning.CreateManifest(zip, Required(options, "version"), Required(options, "channel"), Required(options, "title"), notes, published);
            var version = Carowinds.Passdown.Domain.Updates.SemanticVersion.Parse(Required(options, "version"));
            var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(zip))!, UpdateAssetNames.Manifest(version));
            File.WriteAllBytes(output, bytes);
            Console.WriteLine($"Manifest written: {output}");
            return 0;
        }

        case "sign":
        {
            var manifestPath = Required(options, "manifest");
            options.TryGetValue("private-key", out var keyPath);
            var signature = ReleaseSigning.Sign(File.ReadAllBytes(manifestPath), keyPath, Required(options, "key-id"));
            var output = manifestPath + ".sig";
            File.WriteAllBytes(output, signature);
            Console.WriteLine($"Signature written and verified: {output}");
            return 0;
        }

        case "verify":
        {
            var keys = ReleaseSigning.ReadTrustedKeys(Required(options, "appsettings"));
            var result = ReleaseSigning.Verify(File.ReadAllBytes(Required(options, "manifest")), File.ReadAllBytes(Required(options, "signature")), keys);
            if (!result.IsVerified)
            {
                Console.Error.WriteLine($"NOT VERIFIED: {result.Rejection}{(result.Detail is null ? string.Empty : $" ({result.Detail})")}");
                return 1;
            }

            Console.WriteLine($"VERIFIED: {result.Manifest!.Version} ({result.Manifest.Channel}), key {result.KeyId}, {result.Manifest.Files.Count} files, package SHA-256 {result.Manifest.Package.Sha256}");
            return 0;
        }

        default:
            return Usage();
    }
}
catch (ReleaseSigningException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 2;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 2;
}

static int Usage()
{
    Console.Error.WriteLine("Usage: carowinds-update-signing <generate-key|create-manifest|sign|verify> [options]. See docs/phase23d-github-updater.md.");
    return 64;
}

static Dictionary<string, string> ParseOptions(string[] values)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < values.Length; i++)
    {
        if (!values[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= values.Length)
        {
            throw new ReleaseSigningException($"Unexpected argument '{values[i]}'.");
        }

        options[values[i][2..]] = values[++i];
    }

    return options;
}

static string Required(Dictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ReleaseSigningException($"Missing --{name}.");
