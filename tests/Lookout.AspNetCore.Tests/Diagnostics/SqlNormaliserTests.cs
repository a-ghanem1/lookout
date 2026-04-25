using FluentAssertions;
using Lookout.Core.Diagnostics;
using Xunit;

namespace Lookout.AspNetCore.Tests.Diagnostics;

public sealed class SqlNormaliserTests
{
    // ── Parameter placeholders ─────────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM Orders WHERE Id = @p0")]
    [InlineData("SELECT * FROM Orders WHERE Id = @p1")]
    [InlineData("SELECT * FROM Orders WHERE Id = @id")]
    public void Normalise_AtNamedParams_ProduceSameShapeKey(string sql)
    {
        SqlNormaliser.Normalise(sql).Should().Be("SELECT * FROM ORDERS WHERE ID = ?");
    }

    [Fact]
    public void Normalise_DollarNumberedParams_AreReplaced()
    {
        var key1 = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Id = $1");
        var key2 = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Id = $2");

        key1.Should().Be(key2).And.Be("SELECT * FROM ORDERS WHERE ID = ?");
    }

    [Fact]
    public void Normalise_QuestionMarkPositional_IsReplaced()
    {
        var result = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Id = ?");

        result.Should().Be("SELECT * FROM ORDERS WHERE ID = ?");
    }

    [Fact]
    public void Normalise_ColonNamedParams_AreReplaced()
    {
        var key1 = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Id = :id");
        var key2 = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Id = :orderId");

        key1.Should().Be(key2).And.Be("SELECT * FROM ORDERS WHERE ID = ?");
    }

    [Fact]
    public void Normalise_MultipleParams_AllReplaced()
    {
        var sql = "INSERT INTO Orders (Name, Amount) VALUES (@name, @amount)";

        var result = SqlNormaliser.Normalise(sql);

        result.Should().Be("INSERT INTO ORDERS (NAME, AMOUNT) VALUES (?, ?)");
    }

    // ── String literals ────────────────────────────────────────────────────────

    [Fact]
    public void Normalise_StringLiterals_AreReplacedWithPlaceholder()
    {
        var result = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Status = 'active'");

        result.Should().Be("SELECT * FROM ORDERS WHERE STATUS = ?");
    }

    [Fact]
    public void Normalise_StringLiteralWithEscapedQuote_IsReplaced()
    {
        // SQL escapes single quotes by doubling them: 'it''s fine'
        var result = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Note = 'it''s fine'");

        result.Should().Be("SELECT * FROM ORDERS WHERE NOTE = ?");
    }

    [Fact]
    public void Normalise_StringLiteralContainingNumbers_IsReplacedAsSingleToken()
    {
        // The numbers inside the string should not produce separate '?' tokens
        var result = SqlNormaliser.Normalise("SELECT * FROM Tbl WHERE Code = 'abc123'");

        result.Should().Be("SELECT * FROM TBL WHERE CODE = ?");
    }

    // ── Numeric literals ───────────────────────────────────────────────────────

    [Fact]
    public void Normalise_IntegerLiterals_AreReplaced()
    {
        var result = SqlNormaliser.Normalise("SELECT TOP 10 * FROM Orders WHERE Age > 18");

        result.Should().Be("SELECT TOP ? * FROM ORDERS WHERE AGE > ?");
    }

    [Fact]
    public void Normalise_DecimalLiterals_AreReplaced()
    {
        var result = SqlNormaliser.Normalise("SELECT * FROM Products WHERE Price > 9.99");

        result.Should().Be("SELECT * FROM PRODUCTS WHERE PRICE > ?");
    }

    // ── Case-folding ───────────────────────────────────────────────────────────

    [Fact]
    public void Normalise_CaseFoldsToUppercase()
    {
        var lower = SqlNormaliser.Normalise("select * from orders");
        var upper = SqlNormaliser.Normalise("SELECT * FROM ORDERS");
        var mixed = SqlNormaliser.Normalise("Select * From Orders");

        lower.Should().Be(upper).And.Be(mixed);
    }

    // ── Whitespace ─────────────────────────────────────────────────────────────

    [Fact]
    public void Normalise_CollapsesWhitespaceToSingleSpace()
    {
        var result = SqlNormaliser.Normalise("SELECT  *   FROM\tOrders\nWHERE   Id = @p0");

        result.Should().Be("SELECT * FROM ORDERS WHERE ID = ?");
    }

    [Fact]
    public void Normalise_TrimsLeadingAndTrailingWhitespace()
    {
        var result = SqlNormaliser.Normalise("  SELECT * FROM Orders  ");

        result.Should().Be("SELECT * FROM ORDERS");
    }

    // ── Structural preservation ────────────────────────────────────────────────

    [Fact]
    public void Normalise_PreservesKeywordsAndOperators()
    {
        var sql = "SELECT Id, Name FROM Orders WHERE Id = @id AND Status != 'deleted' ORDER BY Name ASC";

        var result = SqlNormaliser.Normalise(sql);

        result.Should().Contain("SELECT")
              .And.Contain("FROM")
              .And.Contain("WHERE")
              .And.Contain("AND")
              .And.Contain("ORDER BY")
              .And.Contain("ASC")
              .And.Contain("!=");
    }

    [Fact]
    public void Normalise_IsDeterministic_ForSameInput()
    {
        const string sql = "SELECT * FROM Orders WHERE Id = @p0 AND Amount > 100";

        var first = SqlNormaliser.Normalise(sql);
        var second = SqlNormaliser.Normalise(sql);

        first.Should().Be(second);
    }

    [Fact]
    public void Normalise_EmptyString_ReturnsEmpty()
    {
        SqlNormaliser.Normalise(string.Empty).Should().BeEmpty();
    }

    // ── Shape-key equality: structurally identical queries ─────────────────────

    [Fact]
    public void Normalise_IdenticalStructure_DifferentParamNames_ProduceSameKey()
    {
        var a = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Id = @p0");
        var b = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Id = @p1");

        a.Should().Be(b);
    }

    [Fact]
    public void Normalise_IdenticalStructure_DifferentValues_ProduceSameKey()
    {
        var a = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Id = 1");
        var b = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Id = 42");

        a.Should().Be(b);
    }

    [Fact]
    public void Normalise_DifferentStructure_ProducesDifferentKeys()
    {
        var a = SqlNormaliser.Normalise("SELECT * FROM Orders WHERE Id = @p0");
        var b = SqlNormaliser.Normalise("SELECT * FROM Customers WHERE Id = @p0");

        a.Should().NotBe(b);
    }
}
