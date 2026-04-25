using System.Text.RegularExpressions;
using FluentAssertions;
using Lookout.Core.Diagnostics;
using Xunit;

namespace Lookout.AspNetCore.Tests.Diagnostics;

public sealed class N1DetectorTests
{
    private const string ShapeA = "SELECT * FROM ORDERS WHERE ID = ?";
    private const string ShapeB = "SELECT * FROM CUSTOMERS WHERE ID = ?";

    // ── Below threshold ────────────────────────────────────────────────────────

    [Fact]
    public void Detect_ReturnsNoGroups_WhenNothingTracked()
    {
        var detector = new N1Detector();

        detector.Detect().Should().BeEmpty();
    }

    [Fact]
    public void Detect_ReturnsNoGroups_WhenCountBelowDefaultThreshold()
    {
        var detector = new N1Detector(); // minOccurrences = 3

        detector.Track(ShapeA, 1);
        detector.Track(ShapeA, 2);

        detector.Detect().Should().BeEmpty();
    }

    [Fact]
    public void Detect_ReturnsNoGroups_WhenCountEqualsOneBelow_CustomThreshold()
    {
        var detector = new N1Detector(minOccurrences: 5);

        for (var i = 1; i <= 4; i++)
            detector.Track(ShapeA, i);

        detector.Detect().Should().BeEmpty();
    }

    // ── At and above threshold ─────────────────────────────────────────────────

    [Fact]
    public void Detect_ReturnsGroup_WhenCountMeetsDefaultThreshold()
    {
        var detector = new N1Detector(); // minOccurrences = 3

        detector.Track(ShapeA, 10);
        detector.Track(ShapeA, 11);
        detector.Track(ShapeA, 12);

        var groups = detector.Detect();

        groups.Should().ContainSingle()
              .Which.Should().Match<N1Group>(g =>
                  g.ShapeKey == ShapeA &&
                  g.Count == 3 &&
                  g.EntryIds.SequenceEqual(new[] { 10L, 11L, 12L }));
    }

    [Fact]
    public void Detect_ReturnsGroup_WithAllEntryIds_WhenAboveThreshold()
    {
        var detector = new N1Detector();

        for (long i = 1; i <= 5; i++)
            detector.Track(ShapeA, i);

        var group = detector.Detect().Should().ContainSingle().Subject;

        group.Count.Should().Be(5);
        group.EntryIds.Should().BeEquivalentTo(new[] { 1L, 2L, 3L, 4L, 5L });
    }

    [Fact]
    public void Detect_RespectsCustomMinOccurrences()
    {
        var detector = new N1Detector(minOccurrences: 2);

        detector.Track(ShapeA, 1);
        detector.Track(ShapeA, 2);

        detector.Detect().Should().ContainSingle()
                .Which.Count.Should().Be(2);
    }

    // ── Multiple groups ────────────────────────────────────────────────────────

    [Fact]
    public void Detect_ReturnsBothGroups_WhenTwoShapesExceedThreshold()
    {
        var detector = new N1Detector();

        for (long i = 1; i <= 3; i++) detector.Track(ShapeA, i);
        for (long i = 10; i <= 12; i++) detector.Track(ShapeB, i);

        var groups = detector.Detect();

        groups.Should().HaveCount(2);
        groups.Select(g => g.ShapeKey).Should().BeEquivalentTo(new[] { ShapeA, ShapeB });
    }

    [Fact]
    public void Detect_OnlyReturnsGroupsAboveThreshold_WhenMixed()
    {
        var detector = new N1Detector(); // threshold = 3

        // ShapeA: 5 occurrences — above threshold
        for (long i = 1; i <= 5; i++) detector.Track(ShapeA, i);

        // ShapeB: 2 occurrences — below threshold
        detector.Track(ShapeB, 10);
        detector.Track(ShapeB, 11);

        var groups = detector.Detect();

        groups.Should().ContainSingle().Which.ShapeKey.Should().Be(ShapeA);
    }

    // ── N1IgnorePatterns ───────────────────────────────────────────────────────

    [Fact]
    public void Detect_ExcludesGroups_WhenShapeMatchesIgnorePattern()
    {
        var ignore = new Regex("ORDERS", RegexOptions.IgnoreCase);
        var detector = new N1Detector(ignorePatterns: [ignore]);

        for (long i = 1; i <= 5; i++) detector.Track(ShapeA, i); // contains "ORDERS"

        detector.Detect().Should().BeEmpty();
    }

    [Fact]
    public void Detect_DoesNotExcludeGroups_WhenShapeDoesNotMatchIgnorePattern()
    {
        var ignore = new Regex("PRODUCTS");
        var detector = new N1Detector(ignorePatterns: [ignore]);

        for (long i = 1; i <= 3; i++) detector.Track(ShapeA, i); // "ORDERS" — not matched

        detector.Detect().Should().ContainSingle();
    }

    [Fact]
    public void Detect_HonoursMultipleIgnorePatterns()
    {
        var patterns = new List<Regex>
        {
            new("ORDERS"),
            new("CUSTOMERS"),
        };
        var detector = new N1Detector(ignorePatterns: patterns);

        for (long i = 1; i <= 5; i++) detector.Track(ShapeA, i); // ORDERS — ignored
        for (long i = 10; i <= 15; i++) detector.Track(ShapeB, i); // CUSTOMERS — ignored

        detector.Detect().Should().BeEmpty();
    }

    [Fact]
    public void Detect_OnlyIgnoresMatchingShapes_LeavingOthersIntact()
    {
        var ignore = new Regex("ORDERS");
        var detector = new N1Detector(ignorePatterns: [ignore]);

        for (long i = 1; i <= 5; i++) detector.Track(ShapeA, i); // ORDERS — ignored
        for (long i = 10; i <= 13; i++) detector.Track(ShapeB, i); // CUSTOMERS — kept

        var groups = detector.Detect();

        groups.Should().ContainSingle().Which.ShapeKey.Should().Be(ShapeB);
    }

    // ── EfOptions surface ──────────────────────────────────────────────────────

    [Fact]
    public void EfOptions_N1DetectionMinOccurrences_DefaultsTo3()
    {
        var options = new Lookout.Core.EfOptions();

        options.N1DetectionMinOccurrences.Should().Be(3);
    }

    [Fact]
    public void EfOptions_N1IgnorePatterns_DefaultsToEmpty()
    {
        var options = new Lookout.Core.EfOptions();

        options.N1IgnorePatterns.Should().NotBeNull().And.BeEmpty();
    }
}
