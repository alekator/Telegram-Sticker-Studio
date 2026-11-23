using StickerForge.Telegram;

namespace StickerForge.Tests;

public sealed class EmojiValidatorTests
{
    [Theory]
    [InlineData("😀")]
    [InlineData("👩🏽‍💻")]
    [InlineData("👨‍👩‍👧‍👦")]
    [InlineData("🇺🇦")]
    [InlineData("1️⃣")]
    [InlineData("❤️ 🎉")]
    public void AcceptsUnicodeEmojiSequences(string text) => Assert.True(EmojiValidator.IsValid(text));
    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("123")]
    [InlineData("😀hello")]
    [InlineData("‍")]
    [InlineData("🏽")]
    public void RejectsNonEmojiAndOrphanedComponents(string text) => Assert.False(EmojiValidator.IsValid(text));
    [Fact]
    public void RejectsMoreThanTwenty() => Assert.False(EmojiValidator.IsValid(string.Concat(Enumerable.Repeat("😀", 21))));
}
