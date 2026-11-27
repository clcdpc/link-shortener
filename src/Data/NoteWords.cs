using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Clc.LinkShortener.Data
{
    public sealed class NoteWords
    {
        private const string ResourceName = "Clc.LinkShortener.Resources.note-words.txt";
        private const int WordCount = 5521;
        private readonly string[] words;

        private NoteWords(string[] words)
        {
            this.words = words;
        }

        internal static NoteWords Load()
        {
            using var stream = typeof(NoteWords).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException("The embedded one-time note dictionary is missing.");
            using var reader = new StreamReader(stream);
            var words = new List<string>(WordCount);
            var uniqueWords = new HashSet<string>(StringComparer.Ordinal);
            while (reader.ReadLine() is { } word)
            {
                if (word.Length is < 1 or > 9 || word.Any(c => c is < 'a' or > 'z') || !uniqueWords.Add(word))
                {
                    throw new InvalidOperationException("The embedded one-time note dictionary contains an invalid or duplicate word.");
                }

                words.Add(word);
            }

            if (words.Count != WordCount)
            {
                throw new InvalidOperationException($"The embedded one-time note dictionary must contain exactly {WordCount} words.");
            }

            return new NoteWords(words.ToArray());
        }

        internal string Generate() => Generate(RandomNumberGenerator.GetInt32);

        internal string Generate(Func<int, int> selectIndex) => string.Join('-',
            words[selectIndex(words.Length)],
            words[selectIndex(words.Length)],
            words[selectIndex(words.Length)],
            words[selectIndex(words.Length)]);
    }
}
