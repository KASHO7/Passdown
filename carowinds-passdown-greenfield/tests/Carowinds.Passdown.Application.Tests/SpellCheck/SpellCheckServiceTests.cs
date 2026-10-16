using Carowinds.Passdown.Application.SpellCheck;

namespace Carowinds.Passdown.Application.Tests.SpellCheck;

/// <summary>A tiny deterministic dictionary standing in for the Windows spell checker.</summary>
internal sealed class FakeSpellingBackend(params string[] words) : ISpellingBackend
{
    private readonly HashSet<string> _words = new(words, StringComparer.OrdinalIgnoreCase);

    public int IsCorrectCalls { get; private set; }

    public bool Throw { get; set; }

    public List<string> Added { get; } = [];

    public bool IsAvailable => true;

    public bool CanAddToDictionary => true;

    public bool IsCorrect(string word)
    {
        IsCorrectCalls++;
        if (Throw)
        {
            throw new InvalidOperationException("Backend failure");
        }

        return _words.Contains(word);
    }

    public IReadOnlyList<string> Suggest(string word) => word == "recieve" ? ["receive", "relieve"] : [];

    public void AddToDictionary(string word)
    {
        Added.Add(word);
        _words.Add(word);
    }
}

public class SpellCheckTokenizerTests
{
    private static string[] Words(string? text) => SpellCheckTokenizer.Tokenize(text).Select(t => t.Word).ToArray();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\r\n  ")]
    public void EmptyOrWhitespace_HasNoTokens(string? text) => Assert.Empty(Words(text));

    [Fact]
    public void Punctuation_IsNotPartOfTheWord()
    {
        var tokens = SpellCheckTokenizer.Tokenize("(Gate opens), then: \"rides!\"");

        Assert.Equal(["Gate", "opens", "then", "rides"], tokens.Select(t => t.Word));
        Assert.Equal(1, tokens[0].Start);
    }

    [Fact]
    public void Multiline_PositionsAreOffsetsIntoTheWholeText()
    {
        var text = "Line one\r\nSecond line";
        var tokens = SpellCheckTokenizer.Tokenize(text);

        Assert.Equal(["Line", "one", "Second", "line"], tokens.Select(t => t.Word));
        Assert.All(tokens, t => Assert.Equal(t.Word, text.Substring(t.Start, t.Length)));
    }

    [Fact]
    public void NumbersAcronymsAndAddresses_AreSkipped() =>
        Assert.Equal(["at", "and"], Words("Fury325 ERT at 6:00 $105.00 and https://sixflags.com ops@example.com 2027"));

    [Fact]
    public void HyphenatedWords_AreCheckedPartByPart() =>
        Assert.Equal(["Bring", "Friend"], Words("Bring-A-Friend"));

    [Fact]
    public void InternalApostrophes_StayInTheWord_TrailingOnesDoNot() =>
        Assert.Equal(["don't", "Carowinds", "guest’s"], Words("don't Carowinds' guest’s"));
}

public class SpellCheckServiceTests
{
    private static SpellCheckService Create(FakeSpellingBackend backend) => new(backend);

    [Fact]
    public void Misspelling_IsDetected_WithItsPosition()
    {
        var service = Create(new FakeSpellingBackend("the", "gate", "will"));

        var issue = Assert.Single(service.Check("the gate will opne"));

        Assert.Equal(("opne", 14, 4), (issue.Word, issue.Start, issue.Length));
    }

    [Fact]
    public void ValidWords_AreNotFlagged() =>
        Assert.Empty(Create(new FakeSpellingBackend("the", "gate", "opens")).Check("The gate opens."));

    [Fact]
    public void DomainTerms_AreNotFlagged_InAnyCase()
    {
        var service = Create(new FakeSpellingBackend("at", "and", "for", "Fast", "Lane"));

        Assert.Empty(service.Check("Carowinds SCarowinds WinterFest carowinds Dap Conjuring and Fast Lane for Copperhead at Carowinds's"));
    }

    [Fact]
    public void ExtraConfiguredTerms_AreAllowed() =>
        Assert.Empty(new SpellCheckService(new FakeSpellingBackend(), ["Zephyr Tower"]).Check("Zephyr Tower"));

    [Fact]
    public void MultilineText_FlagsEveryLine()
    {
        var issues = Create(new FakeSpellingBackend("line")).Check("line wrnog\nline speling");

        Assert.Equal(["wrnog", "speling"], issues.Select(i => i.Word));
    }

    [Fact]
    public void RepeatedWords_AreCheckedOnceByTheBackend()
    {
        var backend = new FakeSpellingBackend("open");
        var service = Create(backend);

        service.Check("open open open");
        service.Check("open open open open");

        Assert.Equal(1, backend.IsCorrectCalls);
    }

    [Fact]
    public void BackendFailure_TurnsSpellCheckOff_WithoutThrowing()
    {
        var backend = new FakeSpellingBackend { Throw = true };
        var service = Create(backend);

        var issues = service.Check("anything at all");

        Assert.Empty(issues);
        Assert.False(service.IsAvailable);
        Assert.Empty(service.Suggest("recieve"));
    }

    [Fact]
    public void UnavailableBackend_FlagsNothing()
    {
        var service = new SpellCheckService(UnavailableSpellingBackend.Instance);

        Assert.False(service.IsAvailable);
        Assert.Empty(service.Check("totaly wrnog"));
    }

    [Fact]
    public void Suggestions_ComeFromTheBackend()
    {
        Assert.Equal(["receive", "relieve"], Create(new FakeSpellingBackend()).Suggest("recieve"));
    }

    [Fact]
    public void Ignore_StopsFlaggingForTheSession_AndRaisesDictionaryChanged()
    {
        var service = Create(new FakeSpellingBackend());
        var raised = 0;
        service.DictionaryChanged += (_, _) => raised++;

        service.Ignore("Zamperini");

        Assert.Empty(service.Check("Zamperini"));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void AddToDictionary_PersistsThroughTheBackend()
    {
        var backend = new FakeSpellingBackend();
        var service = Create(backend);
        Assert.NotEmpty(service.Check("Zamperini"));

        service.AddToDictionary("Zamperini");

        Assert.Equal(["Zamperini"], backend.Added);
        Assert.Empty(service.Check("Zamperini"));
    }
}
