using System.Security.Cryptography;
using Clc.LinkShortener.Notes;

namespace LinkShortener.Tests;

public class NoteWordsTests
{
    [Fact]
    public void UsesAuthoritativeEmbeddedDictionary()
    {
        using var stream = typeof(NoteWords).Assembly
            .GetManifestResourceStream("Clc.LinkShortener.Resources.note-words.txt");
        Assert.NotNull(stream);
        Assert.Equal("BB7DDA84D5037F24D85A3788ADFD070E9CA716BC31409615DF5F2F5B79498818",
            Convert.ToHexString(SHA256.HashData(stream)));
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var dictionary = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(5521, dictionary.Length);
        Assert.Equal(dictionary.Length, dictionary.Distinct(StringComparer.Ordinal).Count());
        Assert.All(dictionary, word => Assert.Matches("^[a-z]{1,9}$", word));

        var words = NoteWords.Load();
        var allowed = dictionary.ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < 25; i++)
        {
            var generated = words.Generate().Split('-');
            Assert.Equal(4, generated.Length);
            Assert.All(generated, word => Assert.Contains(word, allowed));
        }
    }

    [Fact]
    public void SelectsFourTimesAndAllowsRepeatedWords()
    {
        var words = NoteWords.Load();
        var picks = 0;
        var generated = words.Generate(length =>
        {
            Assert.Equal(5521, length);
            picks++;
            return 0;
        }).Split('-');

        Assert.Equal(4, picks);
        Assert.Equal(4, generated.Length);
        Assert.All(generated, word => Assert.Equal(generated[0], word));
    }
}
