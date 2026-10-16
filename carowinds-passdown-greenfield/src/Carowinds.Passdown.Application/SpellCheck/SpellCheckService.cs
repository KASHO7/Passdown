using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Application.SpellCheck;

/// <summary>
/// A local, offline spelling engine (Phase 20A) — on Windows the operating system's own spell checker.
/// Implementations may throw; <see cref="SpellCheckService"/> contains every failure.
/// </summary>
public interface ISpellingBackend
{
    /// <summary>False when no dictionary is available on this machine/platform (spell check then simply shows nothing).</summary>
    bool IsAvailable { get; }

    bool IsCorrect(string word);

    IReadOnlyList<string> Suggest(string word);

    /// <summary>True when <see cref="AddToDictionary"/> persists a word locally (the Windows user dictionary).</summary>
    bool CanAddToDictionary { get; }

    void AddToDictionary(string word);
}

/// <summary>A backend for platforms without a supported local spell checker: never flags anything.</summary>
public sealed class UnavailableSpellingBackend : ISpellingBackend
{
    public static UnavailableSpellingBackend Instance { get; } = new();

    public bool IsAvailable => false;

    public bool IsCorrect(string word) => true;

    public IReadOnlyList<string> Suggest(string word) => [];

    public bool CanAddToDictionary => false;

    public void AddToDictionary(string word)
    {
    }
}

/// <summary>A misspelled word found in a piece of text.</summary>
public sealed record SpellingIssue(int Start, int Length, string Word)
{
    public int End => Start + Length;
}

/// <summary>
/// Phase 20A: advisory spell checking for free-text fields. Never blocks anything — it only reports
/// ranges for the UI to underline and suggestions for its context menu. Every method is safe to call
/// from the UI thread and never throws: a backend failure is logged once (without any user text) and
/// the service then behaves as "no misspellings".
/// </summary>
public interface ISpellCheckService
{
    bool IsAvailable { get; }

    bool CanAddToDictionary { get; }

    /// <summary>Misspelled words in <paramref name="text"/>, in order. Empty for empty text or an unavailable backend.</summary>
    IReadOnlyList<SpellingIssue> Check(string? text);

    /// <summary>At most five replacement suggestions.</summary>
    IReadOnlyList<string> Suggest(string word);

    /// <summary>Stops flagging <paramref name="word"/> for the rest of this app session (not persisted).</summary>
    void Ignore(string word);

    /// <summary>Adds <paramref name="word"/> to the local Windows user dictionary when supported.</summary>
    void AddToDictionary(string word);

    /// <summary>Raised after Ignore/Add so open fields can re-check.</summary>
    event EventHandler? DictionaryChanged;
}

/// <summary>
/// <see cref="ISpellCheckService"/> over an <see cref="ISpellingBackend"/>, with the shipped domain
/// allow-list (<see cref="DomainSpellingTerms"/>) applied first and a per-word result cache so a large
/// note only consults the backend for words it has not seen before (typing stays responsive).
/// Text is never logged.
/// </summary>
public sealed class SpellCheckService : ISpellCheckService
{
    private const int MaxSuggestions = 5;
    private const int MaxCacheEntries = 5000;

    private readonly ISpellingBackend _backend;
    private readonly ILogger _logger;
    private readonly HashSet<string> _allowed;
    private readonly HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _failed;

    public SpellCheckService(ISpellingBackend backend, IEnumerable<string>? additionalTerms = null, ILogger<SpellCheckService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _allowed = DomainSpellingTerms.CreateAllowList(additionalTerms);
    }

    public event EventHandler? DictionaryChanged;

    public bool IsAvailable
    {
        get
        {
            if (_failed)
            {
                return false;
            }

            try
            {
                return _backend.IsAvailable;
            }
            catch (Exception ex)
            {
                Fail(ex);
                return false;
            }
        }
    }

    public bool CanAddToDictionary
    {
        get
        {
            try
            {
                return IsAvailable && _backend.CanAddToDictionary;
            }
            catch (Exception ex)
            {
                Fail(ex);
                return false;
            }
        }
    }

    public IReadOnlyList<SpellingIssue> Check(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsAvailable)
        {
            return [];
        }

        var issues = new List<SpellingIssue>();
        foreach (var token in SpellCheckTokenizer.Tokenize(text))
        {
            if (!IsWordCorrect(token.Word))
            {
                issues.Add(new SpellingIssue(token.Start, token.Length, token.Word));
            }

            if (_failed)
            {
                return [];
            }
        }

        return issues;
    }

    public IReadOnlyList<string> Suggest(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || !IsAvailable)
        {
            return [];
        }

        try
        {
            return _backend.Suggest(word)
                .Where(s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, word, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Take(MaxSuggestions)
                .ToList();
        }
        catch (Exception ex)
        {
            Fail(ex);
            return [];
        }
    }

    public void Ignore(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        lock (_gate)
        {
            _ignored.Add(word);
        }

        DictionaryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddToDictionary(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || !CanAddToDictionary)
        {
            return;
        }

        try
        {
            _backend.AddToDictionary(word);
            lock (_gate)
            {
                _cache[word] = true;
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
            return;
        }

        DictionaryChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsWordCorrect(string word)
    {
        var normalized = DomainSpellingTerms.Normalize(word);
        lock (_gate)
        {
            if (_allowed.Contains(normalized) || _ignored.Contains(word) || _ignored.Contains(normalized))
            {
                return true;
            }

            if (_cache.TryGetValue(word, out var cached))
            {
                return cached;
            }
        }

        bool correct;
        try
        {
            correct = _backend.IsCorrect(word);
        }
        catch (Exception ex)
        {
            Fail(ex);
            return true;
        }

        lock (_gate)
        {
            if (_cache.Count >= MaxCacheEntries)
            {
                _cache.Clear();
            }

            _cache[word] = correct;
        }

        return correct;
    }

    private void Fail(Exception ex)
    {
        if (_failed)
        {
            return;
        }

        _failed = true;
        // The exception type/message comes from the platform spell checker, never from user text.
        _logger.LogWarning("Spell checking was turned off for this session because the local spell checker failed ({ExceptionType}).", ex.GetType().Name);
    }
}
