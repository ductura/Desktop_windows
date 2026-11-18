using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

// Cases copied from ductura/Institution_server server/contracts/work-id.test.ts at
// 7b78d9b071dc2cb54392840bcdf353e68be865e3 (S-1H.1-c), plus edge classes the
// port to .NET must get the same way as JS. Invented ids only.
public class WorkIdTests
{
    private static readonly string D256 = new('d', WorkId.Max);
    private static readonly string Emoji256 = string.Concat(Enumerable.Repeat("😀", WorkId.Max));

    [Fact]
    public void Accepts_a_valid_id_and_a_uuid()
    {
        Assert.True(WorkId.IsValid("doc-izmisljeni-1"));
        Assert.True(WorkId.IsValid("6f1c2a9e-0b7d-4c1e-9a55-3d2e8f0b1a77"));
    }

    [Theory]
    [InlineData(null)] // the only non-string a C# string? can be
    [InlineData("")]
    [InlineData(" ")]
    public void Refuses_null_empty_and_only_whitespace(string? value) => Assert.False(WorkId.IsValid(value));

    [Fact]
    public void Max_code_points_pass_and_one_more_is_refused()
    {
        Assert.True(WorkId.IsValid(D256));
        Assert.False(WorkId.IsValid(D256 + "d"));
        // 256 emoji are 512 UTF-16 units but 256 characters in length(id).
        Assert.True(WorkId.IsValid(Emoji256));
        Assert.False(WorkId.IsValid(Emoji256 + "😀"));
    }

    [Theory]
    [InlineData("doc'1")]
    [InlineData("doc';DROP TABLE x;--")]
    [InlineData("%00")]
    [InlineData("../doc")]
    [InlineData("../../etc")]
    [InlineData("rad-čćžšđ-ŽĆ")]
    [InlineData("문서-1")]
    [InlineData("toString")]
    [InlineData("__proto__")]
    [InlineData(@"C:\doc")]
    [InlineData("a b#c?d")]
    public void Passes_opaque_data(string value) => Assert.True(WorkId.IsValid(value));

    [Theory]
    // control characters (\p{Cc}): NUL, TAB, LF, CRLF, DEL, C0 and C1
    [InlineData("doc\0")]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\u007fb")]
    [InlineData("a\u0005b")]
    [InlineData("a\u001fb")]
    [InlineData("a\u0085b")]
    // edge whitespace as JS trim() sees it
    [InlineData(" doc")]
    [InlineData("doc ")]
    [InlineData("\u00A0doc")]
    [InlineData("doc\u2028")]
    [InlineData("\u3000doc")]
    [InlineData("\uFEFFdoc")]
    [InlineData("doc\u2029")]
    [InlineData("\u2000doc")]
    [InlineData("doc\u000B")]
    public void Refuses_control_characters_and_edge_whitespace(string value) => Assert.False(WorkId.IsValid(value));

    // Lone surrogates (\p{Cs}) in a Fact: xUnit turns them into U+FFFD in InlineData.
    [Fact]
    public void Refuses_a_lone_surrogate()
    {
        Assert.False(WorkId.IsValid("doc\uD800"));
        Assert.False(WorkId.IsValid("\uDC00doc"));
        Assert.False(WorkId.IsValid("a\uDC00\uD800b"));
        Assert.True(WorkId.IsValid("a\uD83D\uDE00b"));
    }

    [Theory]
    // not whitespace for JS trim(), so they stay part of the id
    [InlineData("\u200Bdoc")]
    [InlineData("\u180Edoc")]
    [InlineData("a\u00A0b")]
    public void Keeps_non_trim_characters_and_inner_spaces(string value) => Assert.True(WorkId.IsValid(value));

    [Theory]
    [InlineData("doc-izmisljeni-1", "doc-izmisljeni-1")]
    [InlineData("rad-%C4%8D", "rad-č")]
    [InlineData("rad-%c4%8d", "rad-č")]
    [InlineData("%2500", "%00")]
    [InlineData("doc%27%3B", "doc';")]
    [InlineData("..%2Fdoc", "../doc")]
    [InlineData("a+b", "a+b")]
    [InlineData("%F0%9F%98%80", "😀")]
    [InlineData("%00", null)]
    [InlineData("doc%00", null)]
    [InlineData("", null)]
    [InlineData("%", null)]
    [InlineData("doc%", null)]
    [InlineData("%zz", null)]
    [InlineData("%4", null)]
    [InlineData("%C4", null)]
    [InlineData("%C4a", null)]
    [InlineData("%C0%80", null)]
    [InlineData("%ED%A0%80", null)]
    [InlineData("%20doc", null)]
    [InlineData("%0D%0A", null)]
    [InlineData("%09doc", null)]
    [InlineData("%7F", null)]
    [InlineData("%EF%BB%BFdoc", null)]
    public void FromPathSegment_decodes_once(string segment, string? expected) =>
        Assert.Equal(expected, WorkId.FromPathSegment(segment));

    [Fact]
    public void FromPathSegment_refuses_an_over_long_id() =>
        Assert.Null(WorkId.FromPathSegment(D256 + "d"));
}
