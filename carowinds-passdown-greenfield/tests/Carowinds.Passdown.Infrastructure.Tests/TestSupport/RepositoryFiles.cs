using System.Security.Cryptography;
using System.Text;

namespace Carowinds.Passdown.Infrastructure.Tests.TestSupport;

/// <summary>
/// Locates the application directory (the folder holding <c>CarowindsPassdown.slnx</c>) by walking up
/// from the test output directory, the same way <see cref="FixtureFile"/> does, so repository-guard
/// tests read committed source and documentation rather than build output.
/// </summary>
public static class RepositoryFiles
{
    private static readonly Lazy<string> Root = new(ResolveRoot);

    public static string ApplicationRoot => Root.Value;

    public static string PathOf(string relativePath) =>
        Path.Combine(ApplicationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public static string ReadText(string relativePath) => File.ReadAllText(PathOf(relativePath));

    public static string RelativeOf(string fullPath) =>
        Path.GetRelativePath(ApplicationRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// The Git blob id (SHA-1 of <c>"blob {length}\0" + bytes</c>) of a file. CRLF is normalized to LF
    /// first, as Git's own <c>core.autocrlf</c> clean filter does, so a Windows checkout of an LF-only
    /// blob still hashes to the committed id. Only use it for files whose committed blob has no CR bytes.
    /// </summary>
    public static string GitBlobId(string relativePath)
    {
        var content = NormalizeLineEndings(File.ReadAllBytes(PathOf(relativePath)));
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        var buffer = new byte[header.Length + content.Length];
        header.CopyTo(buffer, 0);
        content.CopyTo(buffer, header.Length);
#pragma warning disable CA5350 // Git object ids are SHA-1 by definition; this is an identity check, not security.
        return Convert.ToHexStringLower(SHA1.HashData(buffer));
#pragma warning restore CA5350
    }

    private static byte[] NormalizeLineEndings(byte[] content)
    {
        var output = new List<byte>(content.Length);
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == (byte)'\r' && i + 1 < content.Length && content[i + 1] == (byte)'\n')
            {
                continue;
            }

            output.Add(content[i]);
        }

        return [.. output];
    }

    private static string ResolveRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the application directory (via CarowindsPassdown.slnx) above '{AppContext.BaseDirectory}'.");
    }
}
