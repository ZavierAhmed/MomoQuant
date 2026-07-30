using System.Text.RegularExpressions;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Milestone 23.1B1B — prohibited vacuous parity assertion patterns must not regress.</summary>
public sealed class Milestone231B1BAssertionRegressionTests
{
    private static readonly string[] ParitySourceFiles =
    [
        "Milestone231BParityTests.cs",
        "Milestone231B1ATests.cs",
        "ParityAssertionHelper.cs"
    ];

    private static readonly Regex[] ProhibitedPatterns =
    [
        new(@"ExtractStrengthFromStructure\s*\([^)]*\)\s*\?\?", RegexOptions.CultureInvariant),
        new(@"actual\s*\?\?\s*expected", RegexOptions.CultureInvariant),
        new(@"missingValue\s*\?\?\s*directValue", RegexOptions.CultureInvariant),
        new(@"direct\.Strength\s*\?\?", RegexOptions.CultureInvariant)
    ];

    [Fact]
    public void ParityTests_DoNotUseProhibitedFallbackAssertions()
    {
        var baseDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Strategies");
        var violations = new List<string>();

        foreach (var file in ParitySourceFiles)
        {
            var path = Path.GetFullPath(Path.Combine(baseDir, file));
            Assert.True(File.Exists(path), $"Expected parity source file at {path}");
            var text = File.ReadAllText(path);
            foreach (var pattern in ProhibitedPatterns)
            {
                if (pattern.IsMatch(text))
                {
                    violations.Add($"{file}: matched {pattern}");
                }
            }
        }

        Assert.Empty(violations);
    }
}
