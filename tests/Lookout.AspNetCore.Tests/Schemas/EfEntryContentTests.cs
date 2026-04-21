using System.Text.Json;
using FluentAssertions;
using Lookout.Core.Schemas;
using Xunit;

namespace Lookout.AspNetCore.Tests.Schemas;

public sealed class EfEntryContentTests
{
    private static EfEntryContent BuildFull() => new(
        CommandText: "SELECT * FROM Users WHERE Id = @id",
        Parameters: [new EfParameter("@id", "1", "Int32")],
        DurationMs: 12.5,
        RowsAffected: 3,
        DbContextType: "MyApp.Data.AppDbContext",
        CommandType: EfCommandType.Reader,
        Stack: [new EfStackFrame("MyApp.Controllers.UsersController.GetUser", "UsersController.cs", 42)]);

    [Fact]
    public void RoundTripsThroughSharedSerializer()
    {
        var original = BuildFull();

        var json = JsonSerializer.Serialize(original, LookoutJson.Options);
        var round = JsonSerializer.Deserialize<EfEntryContent>(json, LookoutJson.Options);

        round.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void CommandType_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(BuildFull(), LookoutJson.Options);

        json.Should().Contain("\"commandType\":\"Reader\"");
    }

    [Fact]
    public void Serializes_UsingCamelCasePropertyNames()
    {
        var json = JsonSerializer.Serialize(BuildFull(), LookoutJson.Options);

        json.Should().Contain("\"commandText\":");
        json.Should().Contain("\"durationMs\":");
        json.Should().Contain("\"rowsAffected\":");
        json.Should().Contain("\"dbContextType\":");
    }

    [Fact]
    public void NullableFields_OmittedWhenNull()
    {
        var content = new EfEntryContent(
            CommandText: "SELECT 1",
            Parameters: [],
            DurationMs: 1.0,
            RowsAffected: null,
            DbContextType: null,
            CommandType: EfCommandType.Scalar,
            Stack: []);

        var json = JsonSerializer.Serialize(content, LookoutJson.Options);

        json.Should().NotContain("\"rowsAffected\"");
        json.Should().NotContain("\"dbContextType\"");
    }

    [Fact]
    public void Parameters_WithNullValue_OmitsValueField()
    {
        var content = new EfEntryContent(
            CommandText: "SELECT 1",
            Parameters: [new EfParameter("@p", null, "String")],
            DurationMs: 1.0,
            RowsAffected: null,
            DbContextType: null,
            CommandType: EfCommandType.Query,
            Stack: []);

        var json = JsonSerializer.Serialize(content, LookoutJson.Options);

        // null value is omitted; name and dbType should still appear
        json.Should().Contain("\"name\":\"@p\"");
        json.Should().Contain("\"dbType\":\"String\"");
        json.Should().NotContain("\"value\":");
    }

    [Fact]
    public void Stack_WithNullFileAndLine_OmitsThoseFields()
    {
        var content = new EfEntryContent(
            CommandText: "SELECT 1",
            Parameters: [],
            DurationMs: 1.0,
            RowsAffected: null,
            DbContextType: null,
            CommandType: EfCommandType.Query,
            Stack: [new EfStackFrame("MyApp.Service.DoWork", null, null)]);

        var json = JsonSerializer.Serialize(content, LookoutJson.Options);

        json.Should().Contain("\"method\":\"MyApp.Service.DoWork\"");
        json.Should().NotContain("\"file\":");
        json.Should().NotContain("\"line\":");
    }

    [Fact]
    public void AllCommandTypes_RoundTripAsStrings()
    {
        foreach (var type in Enum.GetValues<EfCommandType>())
        {
            var content = new EfEntryContent("X", [], 0, null, null, type, []);
            var json = JsonSerializer.Serialize(content, LookoutJson.Options);
            var round = JsonSerializer.Deserialize<EfEntryContent>(json, LookoutJson.Options);
            round!.CommandType.Should().Be(type);
        }
    }
}
