using System.Text;
using System.Text.RegularExpressions;

namespace Aras.SreHub.Learning;

public static partial class FlashcardParser
{
    public static IEnumerable<Flashcard> Parse(string subjectId, string relativePath, string markdown)
    {
        var cards = new List<Flashcard>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        string? question = null;
        var answer = new StringBuilder();
        var inQuestionSection = false;

        void Flush()
        {
            if (question is null)
            {
                return;
            }

            cards.Add(new Flashcard(subjectId, relativePath, question, answer.ToString().Trim()));
            question = null;
            answer.Clear();
        }

        foreach (var line in lines)
        {
            var text = line.TrimEnd();

            if (text.StartsWith('#'))
            {
                Flush();
                inQuestionSection = QuestionHeadingRegex().IsMatch(text);
                continue;
            }

            var bold = BoldQuestionRegex().Match(text);
            if (bold.Success)
            {
                Flush();
                question = bold.Groups["question"].Value.Trim();
                continue;
            }

            if (question is not null)
            {
                answer.AppendLine(text);
                continue;
            }

            if (!inQuestionSection)
            {
                continue;
            }

            var numbered = NumberedQuestionRegex().Match(text);
            if (numbered.Success)
            {
                cards.Add(new Flashcard(subjectId, relativePath, numbered.Groups["question"].Value.Trim(), string.Empty));
            }
        }

        Flush();

        return cards.Where(card => card.Question.Length > 3);
    }

    [GeneratedRegex(@"^#{1,6}\s.*interview\s+question", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionHeadingRegex();

    [GeneratedRegex(@"^\*\*Q[:.]?\s*(?<question>.+?)\*\*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BoldQuestionRegex();

    [GeneratedRegex(@"^\s*\d+\.\s+(?<question>.+)$")]
    private static partial Regex NumberedQuestionRegex();
}
