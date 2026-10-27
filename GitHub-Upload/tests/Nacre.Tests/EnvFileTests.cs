using Nacre.Core.Models;
using Nacre.Core.Services;

namespace Nacre.Tests;

public sealed class EnvFileTests
{
    [Fact]
    public void ParsesCommonDotenvFormsWithoutExpandingOrOverwriting()
    {
        var input = "\uFEFF# comment\r\nexport TOKEN = fictional-value # comment\r\nEMPTY=\r\nURL=https://example.invalid/#fragment\r\nQUOTED=\"line1\\nline2\\t\\\"quoted\\\"\"\r\nSINGLE='literal \\n and \u0024{TOKEN}'\r\nMULTI=\"first\r\nsecond\"\r\nTOKEN=another-fictional-value\r\nREFERENCE=\u0024{TOKEN}\nCOMMAND=\u0024(echo nope)";
        var parsed = EnvFile.Parse(input);
        Assert.Empty(parsed.Issues); Assert.Equal(9, parsed.Entries.Count);
        Assert.Equal(2, parsed.Entries.Count(e => e.Name == "TOKEN"));
        Assert.Equal("", parsed.Entries.Single(e => e.Name == "EMPTY").Value);
        Assert.Equal("https://example.invalid/#fragment", parsed.Entries.Single(e => e.Name == "URL").Value);
        Assert.Equal("line1\nline2\t\"quoted\"", parsed.Entries.Single(e => e.Name == "QUOTED").Value);
        Assert.Equal("literal \\n and \u0024{TOKEN}", parsed.Entries.Single(e => e.Name == "SINGLE").Value);
        Assert.Equal("first\nsecond", parsed.Entries.Single(e => e.Name == "MULTI").Value);
        Assert.Equal("\u0024{TOKEN}", parsed.Entries.Single(e => e.Name == "REFERENCE").Value);
        Assert.Equal("\u0024(echo nope)", parsed.Entries.Single(e => e.Name == "COMMAND").Value);
    }

    [Theory]
    [InlineData("broken", 1)]
    [InlineData("9INVALID=fictional", 1)]
    [InlineData("A=\"unclosed", 1)]
    [InlineData("A=\"ok\" trailing", 1)]
    public void InvalidLinesAreReportedWithoutIncludingValues(string content, int issues)
    {
        var parsed = EnvFile.Parse(content);
        Assert.Empty(parsed.Entries); Assert.Equal(issues, parsed.Issues.Count);
        Assert.All(parsed.Issues, i => Assert.DoesNotContain("fictional", i.Message));
    }

    [Fact]
    public void ExportRoundTripsEscapesUnicodeAndJson()
    {
        var fields = new Dictionary<string, string> { ["TEST_TOKEN"] = "fictional \"value\"\\\r\n\t\u0024{PATH} 中文 # ' end", ["EMPTY"] = "" };
        var env = EnvFile.Export(fields, false);
        Assert.Equal(fields.ToArray(), EnvFile.Parse(env).Entries.Select(e => new KeyValuePair<string, string>(e.Name, e.Value)).ToArray());
        var json = EnvFile.Export(fields, true);
        Assert.Equal(fields, System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json));
        Assert.Throws<ArgumentException>(() => EnvFile.Export([], false));
        Assert.Throws<ArgumentException>(() => EnvFile.Export([new("BAD NAME", "fictional")], false));
        Assert.Throws<ArgumentException>(() => EnvFile.Export([new("A", "fictional1"), new("A", "fictional2")], true));
    }

    [Fact]
    public void TaxonomyRenameAndDeleteApplyToBothModules()
    {
        var data = Fixtures.Data();
        data.Texts[0].Category = "私有分类"; data.Texts[0].Tags.Add("private-tag");
        TaxonomyService.Rename(data, false, "私有分类", "New category");
        TaxonomyService.Rename(data, true, "private-tag", "renamed-tag");
        Assert.All(data.AllRecords(), r => { Assert.Equal("New category", r.Category); Assert.Contains("renamed-tag", r.Tags); });
        TaxonomyService.Delete(data, false, "New category"); TaxonomyService.Delete(data, true, "renamed-tag");
        Assert.All(data.AllRecords(), r => { Assert.Empty(r.Category); Assert.Empty(r.Tags); });
        Assert.Equal(2, data.AllRecords().Count());
        Assert.Equal(new[] { "one", "two", "三" }, TaxonomyService.ParseTags("one, two，三; ONE；two"));
        Assert.False(TaxonomyService.IsTagName("one,"));
        Assert.False(TaxonomyService.IsTagName(";one"));
        Assert.Throws<ArgumentException>(() => TaxonomyService.Rename(new VaultData { Tags = ["old"] }, true, "old", "one,"));
    }
}
