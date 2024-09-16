using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{

    public static class StringSimilarityChecker
    {
        // Method 1: Word Overlap Similarity
        public static double WordOverlapSimilarity(string str1, string str2)
        {
            if (string.IsNullOrWhiteSpace(str1) || string.IsNullOrWhiteSpace(str2))
                return 0.0;

            var words1 = str1.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim()).ToArray();
            var words2 = str2.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim()).ToArray();

            int totalWords = words1.Length;
            if (totalWords == 0)
                return 0.0;

            int matchCount = words1.Count(word => words2.Contains(word, StringComparer.OrdinalIgnoreCase));

            return (double)matchCount / totalWords;
        }

        // Method 2: Levenshtein Distance
        public static double LevenshteinDistance(string str1, string str2)
        {
            if (string.IsNullOrEmpty(str1))
                return str2?.Length ?? 0;

            if (string.IsNullOrEmpty(str2))
                return str1.Length;

            int[,] d = new int[str1.Length + 1, str2.Length + 1];

            for (int i = 0; i <= str1.Length; i++)
                d[i, 0] = i;
            for (int j = 0; j <= str2.Length; j++)
                d[0, j] = j;

            for (int i = 1; i <= str1.Length; i++)
            {
                for (int j = 1; j <= str2.Length; j++)
                {
                    int cost = (str2[j - 1] == str1[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[str1.Length, str2.Length];
        }

        // Method 3: Jaccard Similarity
        public static double JaccardSimilarity(string str1, string str2)
        {
            if (string.IsNullOrWhiteSpace(str1) || string.IsNullOrWhiteSpace(str2))
                return 0.0;

            var words1 = new HashSet<string>(str1.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim()), StringComparer.OrdinalIgnoreCase);
            var words2 = new HashSet<string>(str2.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim()), StringComparer.OrdinalIgnoreCase);

            var intersection = words1.Intersect(words2).Count();
            var union = words1.Union(words2).Count();

            if (union == 0)
                return 0.0;

            return (double)intersection / union;
        }

        // Method to combine all three approaches
        public static bool AreStringsSimilar(string str1, string str2, double wordOverlapThreshold = 0.3, double levenshteinMaxDistance = 15, double jaccardThreshold = 0.4)
        {
            // Word Overlap Similarity (returns a value between 0 and 1)
            double wordOverlapSimilarity = WordOverlapSimilarity(str1, str2);

            // Levenshtein Distance (lower values are better)
            double levenshteinDistance = LevenshteinDistance(str1, str2);

            // Jaccard Similarity (returns a value between 0 and 1)
            double jaccardSimilarity = JaccardSimilarity(str1, str2);

            // Use thresholds for each metric and make a decision
            bool wordOverlapPasses = wordOverlapSimilarity >= wordOverlapThreshold;
            bool levenshteinPasses = levenshteinDistance <= levenshteinMaxDistance;
            bool jaccardPasses = jaccardSimilarity >= jaccardThreshold;

            // Return true if at least two of the methods agree that the strings are similar
            int passCount = (wordOverlapPasses ? 1 : 0) + (levenshteinPasses ? 1 : 0) + (jaccardPasses ? 1 : 0);

            return passCount >= 2;  // Requires at least two methods to indicate similarity
        }
    }

}
