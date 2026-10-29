using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Suggestions;

public class UserListFilesTests
{
    [Fact]
    public void Phrases_round_trip_and_importing_only_adds_new_ones()
    {
        var file = new StringWriter();
        UserListFiles.WritePhrases(file, ["جزاك الله خيرا", "نفيدكم علما"]);

        var result = UserListFiles.ReadPhrases(new StringReader(file + "كلمة\nإن شاء الله\n"), ["نفيدكم علما", "مع التحية"]);

        result.Merged.ShouldBe(["نفيدكم علما", "مع التحية", "جزاك الله خيرا", "إن شاء الله"]);
        result.Added.ShouldBe(2);
        result.Skipped.ShouldBe(2); // «نفيدكم علما» already there, «كلمة» is not a phrase
    }

    [Fact]
    public void Shortcuts_round_trip_and_an_existing_key_keeps_its_text()
    {
        var file = new StringWriter();
        UserListFiles.WriteShortcuts(file, [new("ع1", "السلام عليكم"), new("ع2", "وعليكم السلام")]);

        var result = UserListFiles.ReadShortcuts(new StringReader(file + "بلا نص\n"), [new("ع1", "نص قديم")]);

        result.Merged.ShouldBe([new TextShortcut("ع1", "نص قديم"), new TextShortcut("ع2", "وعليكم السلام")]);
        result.Added.ShouldBe(1);
        result.Skipped.ShouldBe(2);
    }
}
