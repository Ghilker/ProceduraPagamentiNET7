using System.Text;

public static class AcademicYearProcessor
{
    // Valid range for the start of an academic year
    private const int MinYear = 1900;
    private const int MaxYear = 2100;

    // Main method to process the academic year input
    public static bool ProcessAcademicYear(string academicYear, out string processedAcademicYear)
    {
        // Step 1: Clean the input by removing any non-numeric characters
        string cleanedAcademicYear = RemoveNonNumeric(academicYear);

        // Step 2: Handle single year or two-digit formats
        cleanedAcademicYear = HandleSingleOrTwoDigitYear(cleanedAcademicYear);

        // Step 3: Fix malformed cases
        cleanedAcademicYear = FixMalformedYears(cleanedAcademicYear);

        // Step 4: Validate length
        if (cleanedAcademicYear.Length != 8)
        {
            processedAcademicYear = cleanedAcademicYear; // Return early if invalid
            return false;
        }

        // Step 5: Extract start and end years
        if (!TryParseYears(cleanedAcademicYear, out int startYear, out int endYear))
        {
            processedAcademicYear = cleanedAcademicYear;
            return false;
        }

        // Step 6: Validate year range and consecutive years
        if (IsYearInRange(startYear) && endYear == startYear + 1)
        {
            processedAcademicYear = $"{startYear}{endYear}";
            return true;
        }

        processedAcademicYear = cleanedAcademicYear;
        return false;
    }

    // Handle single or two-digit year cases
    private static string HandleSingleOrTwoDigitYear(string academicYear)
    {
        if (academicYear.Length == 4 && academicYear.StartsWith("20"))
        {
            int endYear = int.Parse(academicYear);
            return $"{endYear - 1}{endYear}";
        }
        else if (academicYear.Length == 4)
        {
            string startYear = "20" + academicYear.Substring(0, 2);
            string endYear = "20" + academicYear.Substring(2, 2);
            return $"{startYear}{endYear}";
        }

        return academicYear;
    }

    // Fix common malformed academic years
    private static string FixMalformedYears(string academicYear)
    {
        switch (academicYear.Length)
        {
            case 7: return $"{academicYear.Substring(0, 4)}0{academicYear.Substring(4)}"; // Add a missing zero
            case 6: return $"{academicYear.Substring(0, 4)}20{academicYear.Substring(4)}"; // Prefix missing '20'
            default: return academicYear;
        }
    }

    // Attempt to parse start and end years
    private static bool TryParseYears(string academicYear, out int startYear, out int endYear)
    {
        startYear = endYear = 0;

        if (academicYear.Length != 8) return false;

        if (int.TryParse(academicYear.Substring(0, 4), out startYear) && int.TryParse(academicYear.Substring(4, 4), out endYear))
        {
            return true;
        }

        return false;
    }

    // Removes all non-numeric characters from the string
    private static string RemoveNonNumeric(string input)
    {
        var result = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (char.IsDigit(c))
                result.Append(c);
        }
        return result.ToString();
    }

    // Ensures that the year is within the valid range
    private static bool IsYearInRange(int year)
    {
        return year >= MinYear && year <= MaxYear;
    }
}
