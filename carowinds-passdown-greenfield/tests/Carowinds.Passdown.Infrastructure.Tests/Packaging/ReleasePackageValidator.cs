using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Infrastructure.Tests.Packaging;

/// <summary>
/// Phase 18B PKG-002: C# mirror of packaging/windows/Test-ReleasePackage.ps1, driven by the same
/// packaging/windows/release-content-rules.json. Lets the rules themselves be tested on any host
/// (the PowerShell validator only runs on the Windows packaging machine) and validates a real
/// release ZIP when one is supplied.
/// </summary>
internal sealed class ReleasePackageValidator
{
    private readonly Rules _rules;
    private readonly IReadOnlyList<string> _extraForbiddenTokens;

    private ReleasePackageValidator(Rules rules, IReadOnlyList<string> extraForbiddenTokens)
    {
        _rules = rules;
        _extraForbiddenTokens = extraForbiddenTokens;
    }

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string RulesPath => Path.Combine(RepositoryRoot, "packaging", "windows", "release-content-rules.json");

    /// <param name="extraForbiddenTokens">Machine-specific tokens (the checkout path, the user name) searched in <b>every</b> file, third-party binaries included.</param>
    public static ReleasePackageValidator Load(IReadOnlyList<string>? extraForbiddenTokens = null)
    {
        var rules = JsonSerializer.Deserialize<Rules>(File.ReadAllText(RulesPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Empty release rules.");
        return new ReleasePackageValidator(rules, extraForbiddenTokens ?? []);
    }

    public IReadOnlyList<string> RequiredFiles => _rules.RequiredFiles;

    public IReadOnlyList<string> NoticeFilePatterns => _rules.NoticeFilePatterns;

    public int MinimumNoticeFileBytes => _rules.MinimumNoticeFileBytes;

    public bool IsNoticeFile(string path) => _rules.NoticeFilePatterns.Any(p => Glob(p).IsMatch(Normalize(path)));

    /// <summary>Phase 18C PKG-003: a license/notice file must be real, complete text — not a stub or a template.</summary>
    public IEnumerable<string> ValidateNoticeContent(string path, byte[] bytes)
    {
        if (bytes.Length < _rules.MinimumNoticeFileBytes)
        {
            yield return $"notice file too short ({bytes.Length} bytes): {path}";
        }

        var text = Encoding.UTF8.GetString(bytes);
        foreach (var placeholder in _rules.NoticePlaceholderTokens.Where(t => text.Contains(t, StringComparison.Ordinal)))
        {
            yield return $"unresolved placeholder '{placeholder}' in notice file {path}";
        }
    }

    /// <summary>Validates a release given as relative path → content reader (a folder or a ZIP).</summary>
    public IReadOnlyList<string> Validate(IReadOnlyDictionary<string, Func<byte[]>> files, IReadOnlyList<string>? duplicateEntries = null)
    {
        var violations = new List<string>(duplicateEntries ?? []);
        var normalized = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawPath, read) in files)
        {
            normalized[Normalize(rawPath)] = read;
        }

        foreach (var required in _rules.RequiredFiles)
        {
            if (!normalized.TryGetValue(required, out var readRequired))
            {
                violations.Add($"missing required file: {required}");
            }
            else if (readRequired().Length == 0)
            {
                violations.Add($"empty required file: {required}");
            }
        }

        foreach (var (rawPath, read) in files)
        {
            var path = Normalize(rawPath);
            var segments = path.Split('/');
            var fileName = segments[^1];

            foreach (var pattern in _rules.ForbiddenFileNamePatterns.Where(p => Glob(p).IsMatch(fileName)))
            {
                violations.Add($"forbidden file ({pattern}): {path}");
            }

            foreach (var directory in segments[..^1])
            {
                if (_rules.ForbiddenDirectoryNames.Contains(directory, StringComparer.OrdinalIgnoreCase))
                {
                    violations.Add($"forbidden directory ({directory}): {path}");
                }
            }

            var runtimesIndex = Array.FindIndex(segments, s => s.Equals("runtimes", StringComparison.OrdinalIgnoreCase));
            if (runtimesIndex >= 0 && runtimesIndex + 1 < segments.Length - 1)
            {
                var rid = segments[runtimesIndex + 1];
                if (_rules.ForbiddenRuntimeIdentifierPrefixes.Any(prefix => rid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    violations.Add($"non-win-x64 native runtime ({rid}): {path}");
                }
            }

            var isNotice = IsNoticeFile(path);
            var scanGeneric = _rules.OwnBinaryPatterns.Concat(_rules.TextFilePatterns).Any(p => Glob(p).IsMatch(fileName));
            if (!isNotice && !scanGeneric && _extraForbiddenTokens.Count == 0)
            {
                continue;
            }

            var bytes = read();
            if (isNotice)
            {
                violations.AddRange(ValidateNoticeContent(path, bytes));
            }

            var tokens = (scanGeneric ? _rules.ForbiddenPathTokens : []).Concat(_extraForbiddenTokens).Where(t => t.Length > 0);
            foreach (var token in tokens.Where(token => ContainsToken(bytes, token)))
            {
                violations.Add($"absolute developer path token '{token}' in {path}");
            }
        }

        return violations;
    }

    public IReadOnlyList<string> ValidateDirectory(string root) =>
        Validate(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(root, file), file => (Func<byte[]>)(() => File.ReadAllBytes(file))));

    /// <summary>A release ZIP's entries, with its single top-level release folder stripped.</summary>
    public IReadOnlyList<string> ValidateZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var files = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();
        foreach (var entry in entries)
        {
            var path = Normalize(entry.FullName);
            var slash = path.IndexOf('/');
            var relative = slash >= 0 ? path[(slash + 1)..] : path;
            if (files.ContainsKey(relative))
            {
                duplicates.Add($"duplicate ZIP entry: {relative}");
            }

            var captured = entry;
            files[relative] = () =>
            {
                using var stream = captured.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            };
        }

        return Validate(files, duplicates);
    }

    /// <summary>Case-insensitive search in both UTF-8/ASCII and UTF-16LE — .NET user strings are UTF-16, PDB paths and text files UTF-8.</summary>
    internal static bool ContainsToken(byte[] bytes, string token)
    {
        foreach (var encoding in new Encoding[] { Encoding.UTF8, Encoding.Unicode })
        {
            var needle = encoding.GetBytes(token.ToLowerInvariant());
            var upper = encoding.GetBytes(token.ToUpperInvariant());
            if (needle.Length != upper.Length)
            {
                continue;
            }

            for (var i = 0; i + needle.Length <= bytes.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    var b = bytes[i + j];
                    if (b != needle[j] && b != upper[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static Regex Glob(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class Rules
    {
        public List<string> RequiredFiles { get; set; } = [];
        public List<string> ForbiddenFileNamePatterns { get; set; } = [];
        public List<string> ForbiddenDirectoryNames { get; set; } = [];
        public List<string> ForbiddenRuntimeIdentifierPrefixes { get; set; } = [];
        public List<string> OwnBinaryPatterns { get; set; } = [];
        public List<string> TextFilePatterns { get; set; } = [];
        public List<string> ForbiddenPathTokens { get; set; } = [];
        public List<string> NoticeFilePatterns { get; set; } = [];
        public int MinimumNoticeFileBytes { get; set; }
        public List<string> NoticePlaceholderTokens { get; set; } = [];
    }
}
