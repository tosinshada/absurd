using Absurd.Dashboard.Internal;
using Xunit;

namespace Absurd.Dashboard.Tests;

/// <summary>
/// Unit tests for QueueHelpers SQL identifier quoting and validation logic.
/// Task 13.2
/// </summary>
public sealed class QueueHelpersTests
{
    // ── QuoteIdentifier ───────────────────────────────────────────────────────

    [Fact]
    public void QuoteIdentifier_SimpleName_WrapsInDoubleQuotes()
    {
        var result = QueueHelpers.QuoteIdentifier("orders");
        Assert.Equal("\"orders\"", result);
    }

    [Fact]
    public void QuoteIdentifier_NameWithDoubleQuote_EscapesQuote()
    {
        var result = QueueHelpers.QuoteIdentifier("bad\"name");
        Assert.Equal("\"bad\"\"name\"", result);
    }

    [Fact]
    public void QuoteIdentifier_NameWithSpaces_WrapsCorrectly()
    {
        var result = QueueHelpers.QuoteIdentifier("my queue");
        Assert.Equal("\"my queue\"", result);
    }

    [Fact]
    public void QuoteIdentifier_EmptyString_WrapsEmptyInDoubleQuotes()
    {
        var result = QueueHelpers.QuoteIdentifier("");
        Assert.Equal("\"\"", result);
    }

    // ── QuoteLiteral ─────────────────────────────────────────────────────────

    [Fact]
    public void QuoteLiteral_SimpleValue_WrapsInSingleQuotes()
    {
        var result = QueueHelpers.QuoteLiteral("orders");
        Assert.Equal("'orders'", result);
    }

    [Fact]
    public void QuoteLiteral_ValueWithSingleQuote_EscapesQuote()
    {
        var result = QueueHelpers.QuoteLiteral("it's");
        Assert.Equal("'it''s'", result);
    }

    [Fact]
    public void QuoteLiteral_ValueWithBackslash_EscapesBackslash()
    {
        var result = QueueHelpers.QuoteLiteral("path\\value");
        Assert.Equal("'path\\\\value'", result);
    }

    [Fact]
    public void QuoteLiteral_ValueWithBothEscapeChars_EscapesBoth()
    {
        // Input: a\'b (4 chars: a, \, ', b)
        // After backslash escape: a\\'b (5 chars: a, \, \, ', b)
        // After single-quote escape: a\\''b (6 chars: a, \, \, ', ', b)
        // Wrapped: 'a\\''b' (8 chars)
        var result = QueueHelpers.QuoteLiteral("a\\'b");
        Assert.Equal("'a\\\\''b'", result);
    }

    // ── QueueTableIdentifier ─────────────────────────────────────────────────

    [Fact]
    public void QueueTableIdentifier_TasksPrefix_BuildsCorrectIdentifier()
    {
        var result = QueueHelpers.QueueTableIdentifier("t", "orders");
        Assert.Equal("\"t_orders\"", result);
    }

    [Fact]
    public void QueueTableIdentifier_RunsPrefix_BuildsCorrectIdentifier()
    {
        var result = QueueHelpers.QueueTableIdentifier("r", "billing");
        Assert.Equal("\"r_billing\"", result);
    }

    [Fact]
    public void QueueTableIdentifier_QueueNameWithDoubleQuote_EscapesIt()
    {
        var result = QueueHelpers.QueueTableIdentifier("t", "bad\"queue");
        Assert.Equal("\"t_bad\"\"queue\"", result);
    }

    // ── NormalizeTaskStatusFilter ─────────────────────────────────────────────

    [Theory]
    [InlineData("pending",   "pending",   true)]
    [InlineData("running",   "running",   true)]
    [InlineData("sleeping",  "sleeping",  true)]
    [InlineData("completed", "completed", true)]
    [InlineData("failed",    "failed",    true)]
    [InlineData("cancelled", "cancelled", true)]
    public void NormalizeTaskStatusFilter_KnownStatus_ReturnsValidWithStatus(
        string input, string expectedStatus, bool expectedValid)
    {
        var (status, valid) = QueueHelpers.NormalizeTaskStatusFilter(input);
        Assert.Equal(expectedValid, valid);
        Assert.Equal(expectedStatus, status);
    }

    [Theory]
    [InlineData("PENDING")]   // case-insensitive
    [InlineData("  running  ")] // whitespace-trimmed
    public void NormalizeTaskStatusFilter_NormalizedKnownStatus_ReturnsValid(string input)
    {
        var (_, valid) = QueueHelpers.NormalizeTaskStatusFilter(input);
        Assert.True(valid);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("done")]
    [InlineData("active")]
    public void NormalizeTaskStatusFilter_UnknownStatus_ReturnsInvalid(string input)
    {
        var (status, valid) = QueueHelpers.NormalizeTaskStatusFilter(input);
        Assert.False(valid);
        Assert.Null(status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeTaskStatusFilter_EmptyOrNull_ReturnsValidEmptyStatus(string? input)
    {
        var (status, valid) = QueueHelpers.NormalizeTaskStatusFilter(input);
        Assert.True(valid);
        Assert.Equal("", status);
    }

    // ── ParsePositiveInt ──────────────────────────────────────────────────────

    [Fact]
    public void ParsePositiveInt_ValidPositive_ReturnsParsed()
    {
        Assert.Equal(42, QueueHelpers.ParsePositiveInt("42", 10));
    }

    [Fact]
    public void ParsePositiveInt_Zero_ReturnsFallback()
    {
        Assert.Equal(10, QueueHelpers.ParsePositiveInt("0", 10));
    }

    [Fact]
    public void ParsePositiveInt_Negative_ReturnsFallback()
    {
        Assert.Equal(10, QueueHelpers.ParsePositiveInt("-5", 10));
    }

    [Fact]
    public void ParsePositiveInt_NonNumeric_ReturnsFallback()
    {
        Assert.Equal(10, QueueHelpers.ParsePositiveInt("abc", 10));
    }

    [Fact]
    public void ParsePositiveInt_Null_ReturnsFallback()
    {
        Assert.Equal(10, QueueHelpers.ParsePositiveInt(null, 10));
    }

    [Fact]
    public void ParsePositiveInt_Empty_ReturnsFallback()
    {
        Assert.Equal(10, QueueHelpers.ParsePositiveInt("", 10));
    }

    // ── ParseOptionalTime ─────────────────────────────────────────────────────

    [Fact]
    public void ParseOptionalTime_NullInput_ReturnsNull()
    {
        Assert.Null(QueueHelpers.ParseOptionalTime(null));
    }

    [Fact]
    public void ParseOptionalTime_EmptyString_ReturnsNull()
    {
        Assert.Null(QueueHelpers.ParseOptionalTime(""));
    }

    [Fact]
    public void ParseOptionalTime_Iso8601_ReturnsDateTime()
    {
        var result = QueueHelpers.ParseOptionalTime("2025-01-15T12:00:00Z");
        Assert.NotNull(result);
        Assert.Equal(2025, result!.Value.Year);
        Assert.Equal(1, result.Value.Month);
        Assert.Equal(15, result.Value.Day);
    }

    [Fact]
    public void ParseOptionalTime_InvalidString_ReturnsNull()
    {
        Assert.Null(QueueHelpers.ParseOptionalTime("not-a-date"));
    }
}
