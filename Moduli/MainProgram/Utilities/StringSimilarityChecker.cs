using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    public static class StringSimilarityChecker
    {
        // Method to normalize text
        private static string NormalizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            // Remove numbers
            input = Regex.Replace(input, @"\d+", "");

            // Remove punctuation
            input = Regex.Replace(input, @"[^\w\s]", "");

            // Replace multiple spaces with a single space
            input = Regex.Replace(input, @"\s+", " ");

            // Remove accents and diacritics
            input = RemoveDiacritics(input);

            // Convert to lowercase
            input = input.ToLowerInvariant();

            return input.Trim();
        }

        // Helper method to remove accents and diacritics
        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        // Method 1: Word Overlap Similarity
        public static double WordOverlapSimilarity(string str1, string str2)
        {
            if (string.IsNullOrWhiteSpace(str1) || string.IsNullOrWhiteSpace(str2))
                return 0.0;

            var words1 = str1.Split(' ').ToArray();
            var words2 = str2.Split(' ').ToArray();

            int totalWords = words1.Length;
            if (totalWords == 0)
                return 0.0;

            int matchCount = words1.Count(word => words2.Contains(word));

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

            var words1 = new HashSet<string>(str1.Split(' '));
            var words2 = new HashSet<string>(str2.Split(' '));

            var intersection = words1.Intersect(words2).Count();
            var union = words1.Union(words2).Count();

            if (union == 0)
                return 0.0;

            return (double)intersection / union;
        }

        // Method 4: Dice's Coefficient (Sørensen–Dice index)
        public static double DiceCoefficient(string str1, string str2)
        {
            var bigrams1 = GetBigrams(str1);
            var bigrams2 = GetBigrams(str2);

            int intersection = bigrams1.Intersect(bigrams2).Count();
            int totalBigrams = bigrams1.Count + bigrams2.Count;

            if (totalBigrams == 0)
                return 0.0;

            return (2.0 * intersection) / totalBigrams;
        }

        // Helper method to get bigrams from a string
        private static List<string> GetBigrams(string input)
        {
            var bigrams = new List<string>();
            for (int i = 0; i < input.Length - 1; i++)
            {
                bigrams.Add(input.Substring(i, 2));
            }
            return bigrams;
        }

        /// <summary>
        /// Determines if two strings are similar based on multiple similarity metrics with dynamic thresholds.
        /// </summary>
        /// <param name="str1">The first string to compare (e.g., university course name).</param>
        /// <param name="str2">The second string to compare (e.g., student-declared course name).</param>
        /// <param name="distanceRatio">
        /// The proportion of the longer string's length to set as the maximum allowable Levenshtein distance.
        /// Default is 0.25 (25%).
        /// </param>
        /// <param name="overlapRatio">
        /// The proportion of overlapping words required in Word Overlap Similarity.
        /// Default is 0.5 (50%).
        /// </param>
        /// <param name="jaccardRatio">
        /// The proportion of the Jaccard Similarity index required.
        /// Default is 0.5 (50%).
        /// </param>
        /// <param name="diceThreshold">
        /// The minimum Dice's Coefficient required.
        /// Default is 0.5 (50%).
        /// </param>
        /// <param name="preCheckThreshold">
        /// The minimum proportion of words from <paramref name="str2"/> that must be present in <paramref name="str1"/> for the pre-check to pass.
        /// Default is 0.65 (65%).
        /// </param>
        /// <param name="methodsToUse">
        /// The number of methods to use for similarity checking. Default is 4.
        /// </param>
        /// <returns>
        /// Returns true if at least two methods indicate similarity, or if all used methods (e.g., 2 out of 2) indicate similarity.
        /// </returns>
        public static bool AreStringsSimilar(
            string str1,
            string str2,
            double distanceRatio = 0.25,
            double overlapRatio = 0.5,
            double jaccardRatio = 0.5,
            double diceThreshold = 0.5,
            double preCheckThreshold = 0.45,
            int methodsToUse = 4)
        {
            // Normalize both strings
            str1 = NormalizeText(str1);
            str2 = NormalizeText(str2);

            // Prepare for word comparison
            var words1 = new HashSet<string>(str1.Split(' '));
            var words2 = new HashSet<string>(str2.Split(' '));

            // Calculate the proportion of words from str2 that are in str1
            int totalWordsInStr2 = words2.Count;
            if (totalWordsInStr2 == 0)
            {
                return false; // Avoid division by zero
            }

            int wordsInBoth = words2.Count(word => words1.Contains(word));

            double proportion = (double)wordsInBoth / totalWordsInStr2;

            // If the proportion meets or exceeds the threshold, return true
            if (proportion >= preCheckThreshold)
            {
                return true;
            }

            // Dynamic thresholds based on string lengths
            int maxLength = Math.Max(str1.Length, str2.Length);
            int levenshteinMaxDistance = (int)(maxLength * distanceRatio);

            // Word Overlap Similarity (returns a value between 0 and 1)
            double wordOverlapSimilarity = WordOverlapSimilarity(str1, str2);

            // Levenshtein Distance (lower values are better)
            double levenshteinDistance = LevenshteinDistance(str1, str2);

            // Jaccard Similarity (returns a value between 0 and 1)
            double jaccardSimilarity = JaccardSimilarity(str1, str2);

            // Dice's Coefficient (returns a value between 0 and 1)
            double diceCoefficient = DiceCoefficient(str1, str2);

            // Use dynamic thresholds for each metric and make a decision
            bool wordOverlapPasses = wordOverlapSimilarity >= overlapRatio;
            bool levenshteinPasses = levenshteinDistance <= levenshteinMaxDistance;
            bool jaccardPasses = jaccardSimilarity >= jaccardRatio;
            bool dicePasses = diceCoefficient >= diceThreshold;

            // List of methods used and their results
            var methodResults = new List<bool>();

            // Add method results based on the number of methods to use
            if (methodsToUse >= 1)
                methodResults.Add(wordOverlapPasses);
            if (methodsToUse >= 2)
                methodResults.Add(levenshteinPasses);
            if (methodsToUse >= 3)
                methodResults.Add(jaccardPasses);
            if (methodsToUse >= 4)
                methodResults.Add(dicePasses);

            // Count how many methods passed
            int passCount = methodResults.Count(result => result);

            // Determine if the strings are similar
            if (methodResults.Count == 2)
            {
                // If using only 2 methods, both must pass
                return passCount == 2;
            }
            else
            {
                // Otherwise, at least 2 methods must pass
                return passCount >= 2;
            }
        }
    }
}
