using DesignAid.Application.Services;

namespace DesignAid.Tests.Application;

/// <summary>
/// asset rename の判定（純関数）のテスト。
/// </summary>
public class AssetRenamePlannerTests
{
    // ---- ValidateName ----

    [Theory]
    [InlineData("immune-guard")]
    [InlineData("lifting-unit")]
    [InlineData("昇降ユニット")]
    [InlineData("a b")]
    [InlineData("v1.2")]
    [InlineData("コンソール")]
    public void ValidateName_使える名前_nullを返す(string name)
    {
        Assert.Null(AssetRenamePlanner.ValidateName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" lead")]
    [InlineData("trail ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("a\tb")]
    [InlineData("name.")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("nul.txt")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("aux")]
    [InlineData("prn")]
    public void ValidateName_使えない名前_理由を返す(string? name)
    {
        Assert.NotNull(AssetRenamePlanner.ValidateName(name));
    }

    [Theory]
    [InlineData("COM\u00B9")]
    [InlineData("COM\u00B2")]
    [InlineData("COM\u00B3")]
    [InlineData("LPT\u00B9")]
    [InlineData("LPT\u00B2")]
    [InlineData("LPT\u00B3")]
    [InlineData("com\u00B9.txt")]
    [InlineData("LPT\u00B3.log")]
    public void ValidateName_上付き数字の予約名_拒否する(string name)
    {
        Assert.NotNull(AssetRenamePlanner.ValidateName(name));
    }

    [Theory]
    [InlineData("COM10")]
    [InlineData("LPT10")]
    [InlineData("COM0")]
    [InlineData("COMX")]
    [InlineData("xCOM1")]
    public void ValidateName_予約名に似た通常の名前_通す(string name)
    {
        Assert.Null(AssetRenamePlanner.ValidateName(name));
    }

    [Fact]
    public void ValidateNewName_UTF8で255バイトまで_境界()
    {
        Assert.Null(AssetRenamePlanner.ValidateNewName(new string('a', 255)));
        Assert.NotNull(AssetRenamePlanner.ValidateNewName(new string('a', 256)));
        Assert.Null(AssetRenamePlanner.ValidateNewName(new string('漢', 85)));       // 255 バイト
        Assert.NotNull(AssetRenamePlanner.ValidateNewName(new string('漢', 86)));    // 258 バイト
        var emoji = char.ConvertFromUtf32(0x1F600);                                   // 4 バイト
        Assert.Null(AssetRenamePlanner.ValidateNewName(string.Concat(Enumerable.Repeat(emoji, 63)) + "abc"));   // 255
        Assert.NotNull(AssetRenamePlanner.ValidateNewName(string.Concat(Enumerable.Repeat(emoji, 63)) + "abcd")); // 256
    }

    [Fact]
    public void CheckPreconditions_新名はバイト上限を課し_旧名には課さない()
    {
        var longOld = new string('漢', 86); // 258 バイトだが 255 文字以内
        Assert.Empty(AssetRenamePlanner.CheckPreconditions(Ok() with { OldName = longOld }));
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { NewName = new string('漢', 86) });
        Assert.Single(errors);
        Assert.Contains("バイト", errors[0]);
    }

    [Fact]
    public void ValidateName_長すぎる名前_理由を返す()
    {
        Assert.NotNull(AssetRenamePlanner.ValidateName(new string('a', 256)));
        Assert.Null(AssetRenamePlanner.ValidateName(new string('a', 255)));
    }

    // ---- CheckPreconditions ----

    private static AssetRenamePlanner.Facts Ok() => new(
        OldName: "old", NewName: "new",
        OldInDb: true, OldHasAssetJson: true, OldAssetJsonUnreadable: false,
        NewInDb: false, NewHasAssetJson: false, NewPathExists: false);

    [Fact]
    public void CheckPreconditions_全部問題なし_空を返す()
    {
        Assert.Empty(AssetRenamePlanner.CheckPreconditions(Ok()));
    }

    [Fact]
    public void CheckPreconditions_DBだけにある旧名_進めてよい()
    {
        var f = Ok() with { OldHasAssetJson = false };
        Assert.Empty(AssetRenamePlanner.CheckPreconditions(f));
    }

    [Fact]
    public void CheckPreconditions_asset_jsonだけにある旧名_進めてよい()
    {
        var f = Ok() with { OldInDb = false };
        Assert.Empty(AssetRenamePlanner.CheckPreconditions(f));
    }

    [Fact]
    public void CheckPreconditions_旧名がどこにも無い_止める()
    {
        var f = Ok() with { OldInDb = false, OldHasAssetJson = false };
        Assert.Single(AssetRenamePlanner.CheckPreconditions(f));
    }

    [Fact]
    public void CheckPreconditions_旧名のasset_jsonが読めない_止める()
    {
        var f = Ok() with { OldAssetJsonUnreadable = true };
        Assert.Single(AssetRenamePlanner.CheckPreconditions(f));
    }

    [Fact]
    public void CheckPreconditions_新名がDBにある_止める()
    {
        Assert.Single(AssetRenamePlanner.CheckPreconditions(Ok() with { NewInDb = true }));
    }

    [Fact]
    public void CheckPreconditions_新名のasset_jsonがある_止める()
    {
        Assert.Single(AssetRenamePlanner.CheckPreconditions(Ok() with { NewHasAssetJson = true }));
    }

    [Fact]
    public void CheckPreconditions_移動先が既にある_止める()
    {
        Assert.Single(AssetRenamePlanner.CheckPreconditions(Ok() with { NewPathExists = true }));
    }

    [Fact]
    public void CheckPreconditions_旧名と新名が同じ_理由を返して止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { NewName = "old" });
        Assert.Single(errors);
        Assert.Contains("旧名と新名が同じです", errors[0]);
    }

    [Fact]
    public void CheckPreconditions_大文字小文字だけの違い_理由を返して止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { NewName = "OLD" });
        Assert.Single(errors);
        Assert.Contains("大文字小文字", errors[0]);
    }

    [Fact]
    public void CheckPreconditions_新名が名前の検査に通らない_止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { NewName = "a/b" });
        Assert.Single(errors);
        Assert.Contains("新しい名前が使えません", errors[0]);
    }

    [Fact]
    public void CheckPreconditions_複数当てはまる_全部を返す()
    {
        var f = Ok() with { NewInDb = true, NewHasAssetJson = true, NewPathExists = true };
        Assert.Equal(3, AssetRenamePlanner.CheckPreconditions(f).Count);
    }

    [Fact]
    public void CheckPreconditions_旧名が不正_止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { OldName = "a/b" });
        Assert.Single(errors);
        Assert.Contains("旧名が使えません", errors[0]);
    }

    [Fact]
    public void CheckPreconditions_旧名がassetsの外_止める()
    {
        Assert.Single(AssetRenamePlanner.CheckPreconditions(Ok() with { OldDirOutsideAssets = true }));
    }

    [Fact]
    public void CheckPreconditions_大小文字だけ違う名前がある_正しい名前を出して止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { OldCaseVariants = new[] { "Old" } });
        Assert.Single(errors);
        Assert.Contains("Old", errors[0]);
    }

    [Fact]
    public void CheckPreconditions_アーカイブ済み_復元を案内して止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { OldArchived = true });
        Assert.Single(errors);
        Assert.Contains("archive restore", errors[0]);
    }

    [Fact]
    public void CheckPreconditions_アーカイブ内の参照_親を復元するよう案内して止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { ArchivesReferencingOld = new[] { "parent" } });
        Assert.Single(errors);
        Assert.Contains("parent", errors[0]);
    }

    [Fact]
    public void CheckPreconditions_空の大小文字違い一覧とアーカイブ参照一覧_進めてよい()
    {
        var f = Ok() with { OldCaseVariants = Array.Empty<string>(), ArchivesReferencingOld = Array.Empty<string>() };
        Assert.Empty(AssetRenamePlanner.CheckPreconditions(f));
    }

    [Fact]
    public void IsDirectChildOf_assets直下だけtrue()
    {
        var assets = Path.Combine(Path.GetTempPath(), "x-assets");
        Assert.True(AssetRenamePlanner.IsDirectChildOf(assets, Path.Combine(assets, "old")));
        Assert.True(AssetRenamePlanner.IsDirectChildOf(assets + Path.DirectorySeparatorChar, Path.Combine(assets, "old")));
        Assert.False(AssetRenamePlanner.IsDirectChildOf(assets, Path.Combine(assets, "..", "other", "old")));
        Assert.False(AssetRenamePlanner.IsDirectChildOf(assets, Path.Combine(assets, "a", "b")));
        Assert.False(AssetRenamePlanner.IsDirectChildOf(assets, assets));
        Assert.False(AssetRenamePlanner.IsDirectChildOf(assets, Path.Combine(Path.GetTempPath(), "elsewhere", "old")));
    }

    [Fact]
    public void CheckPreconditions_検査できないアーカイブ_パスを出して止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { UnreadableArchives = new[] { "x/broken.zip" } });
        Assert.Single(errors);
        Assert.Contains("broken.zip", errors[0]);
    }

    [Fact]
    public void CheckPreconditions_asset_jsonのnameが旧名と違う_止める_一致なら通る()
    {
        var bad = AssetRenamePlanner.CheckPreconditions(Ok() with { OldAssetJsonName = "other" });
        Assert.Single(bad);
        Assert.Contains("other", bad[0]);
        Assert.Empty(AssetRenamePlanner.CheckPreconditions(Ok() with { OldAssetJsonName = "old" }));
    }

    [Fact]
    public void CheckPreconditions_新名と大小文字だけ違うものがある_止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { NewFsCaseCollisions = new[] { "NEW" } });
        Assert.Single(errors);
        Assert.Contains("NEW", errors[0]);
        Assert.Empty(AssetRenamePlanner.CheckPreconditions(Ok() with { NewFsCaseCollisions = Array.Empty<string>() }));
    }

    [Fact]
    public void CheckPreconditions_新名のアーカイブがある_止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { NewArchived = true });
        Assert.Single(errors);
        Assert.Contains("同じ名前のアーカイブ", errors[0]);
    }

    [Fact]
    public void CheckPreconditions_asset_jsonのIDが別の装置のもの_止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { OldJsonIdOwner = "assets/other" });
        Assert.Single(errors);
        Assert.Contains("assets/other", errors[0]);
    }

    [Fact]
    public void ChoosePreviousNamesBase_欄が無ければDB_空配列ならJSON_JSONが無ければDB()
    {
        var db = new[] { "a" };
        Assert.Equal(db, AssetRenamePlanner.ChoosePreviousNamesBase(true, null, db));
        Assert.Empty(AssetRenamePlanner.ChoosePreviousNamesBase(true, Array.Empty<string>(), db)!);
        Assert.Equal(new[] { "j" }, AssetRenamePlanner.ChoosePreviousNamesBase(true, new[] { "j" }, db));
        Assert.Equal(db, AssetRenamePlanner.ChoosePreviousNamesBase(false, null, db));
    }

    [Fact]
    public void CheckPreconditions_別フォルダのasset_jsonが新名を持つ_止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { NewJsonNameOwners = new[] { "x" } });
        Assert.Single(errors);
        Assert.Contains("x", errors[0]);
        Assert.Empty(AssetRenamePlanner.CheckPreconditions(Ok() with { NewJsonNameOwners = Array.Empty<string>() }));
    }

    [Fact]
    public void CheckPreconditions_別の変更が実行中_止める()
    {
        var errors = AssetRenamePlanner.CheckPreconditions(Ok() with { RenameInProgress = true });
        Assert.Single(errors);
        Assert.Contains("別の変更が実行中", errors[0]);
    }

    // ---- UpdatePreviousNames ----

    [Fact]
    public void UpdatePreviousNames_初回_旧名だけの一覧()
    {
        Assert.Equal(new[] { "a" }, AssetRenamePlanner.UpdatePreviousNames(null, "a", "b"));
    }

    [Fact]
    public void UpdatePreviousNames_古い順に追記する()
    {
        var r = AssetRenamePlanner.UpdatePreviousNames(new[] { "a" }, "b", "c");
        Assert.Equal(new[] { "a", "b" }, r);
    }

    [Fact]
    public void UpdatePreviousNames_重複させない()
    {
        var r = AssetRenamePlanner.UpdatePreviousNames(new[] { "a", "b" }, "a", "c");
        Assert.Equal(new[] { "a", "b" }, r);
    }

    [Fact]
    public void UpdatePreviousNames_大文字小文字違いも重複とみなす()
    {
        var r = AssetRenamePlanner.UpdatePreviousNames(new[] { "A" }, "a", "c");
        Assert.Equal(new[] { "A" }, r);
    }

    [Fact]
    public void UpdatePreviousNames_既存の一覧の中の重複もまとめる()
    {
        var r = AssetRenamePlanner.UpdatePreviousNames(new[] { "a", "b", "a", "B" }, "c", "d");
        Assert.Equal(new[] { "a", "b", "c" }, r);
    }

    [Fact]
    public void UpdatePreviousNames_現在名に戻したら履歴から外す()
    {
        // a → b → a と戻した後、現在名 a が「変更前の名前」に残ってはならない
        var r = AssetRenamePlanner.UpdatePreviousNames(new[] { "a" }, "b", "a");
        Assert.Equal(new[] { "b" }, r);
    }

    [Fact]
    public void UpdatePreviousNames_元の一覧を書き換えない()
    {
        var existing = new List<string> { "a" };
        AssetRenamePlanner.UpdatePreviousNames(existing, "b", "c");
        Assert.Equal(new[] { "a" }, existing);
    }

    [Fact]
    public void UpdatePreviousNames_空要素は捨てる()
    {
        var r = AssetRenamePlanner.UpdatePreviousNames(new[] { "", "a", " " }, "b", "c");
        Assert.Equal(new[] { "a", "b" }, r);
    }

    // ---- ResolveDisplayName ----

    [Fact]
    public void ResolveDisplayName_旧名と同じなら新名に変える()
        => Assert.Equal("new", AssetRenamePlanner.ResolveDisplayName("old", "old", "new"));

    [Fact]
    public void ResolveDisplayName_違えば変えない()
        => Assert.Null(AssetRenamePlanner.ResolveDisplayName("昇降ユニット", "old", "new"));

    [Fact]
    public void ResolveDisplayName_nullなら変えない()
        => Assert.Null(AssetRenamePlanner.ResolveDisplayName(null, "old", "new"));

    // ---- RenameChildReferences ----

    private const string LinksJson = """
        {
          "parts": [ { "part_number": "P-1", "quantity": 2 } ],
          "child_assets": [
            { "child_asset_name": "old", "quantity": 1, "notes": "安全装置" },
            { "child_asset_name": "other", "quantity": 3 }
          ],
          "extra": "残る"
        }
        """;

    [Fact]
    public void RenameChildReferences_該当する子装置の名前だけ書き換える()
    {
        var (json, count) = AssetRenamePlanner.RenameChildReferences(LinksJson, "old", "new");

        Assert.Equal(1, count);
        Assert.NotNull(json);
        var doc = System.Text.Json.JsonDocument.Parse(json!);
        var children = doc.RootElement.GetProperty("child_assets");
        Assert.Equal("new", children[0].GetProperty("child_asset_name").GetString());
        Assert.Equal("other", children[1].GetProperty("child_asset_name").GetString());
        // 他の欄は落とさない
        Assert.Equal(1, children[0].GetProperty("quantity").GetInt32());
        Assert.Equal("安全装置", children[0].GetProperty("notes").GetString());
        Assert.Equal("残る", doc.RootElement.GetProperty("extra").GetString());
        Assert.Equal("P-1", doc.RootElement.GetProperty("parts")[0].GetProperty("part_number").GetString());
    }

    [Fact]
    public void RenameChildReferences_大文字小文字違いの参照も書き換える()
    {
        var (_, count) = AssetRenamePlanner.RenameChildReferences(LinksJson, "OLD", "new");
        Assert.Equal(1, count);
    }

    [Fact]
    public void RenameChildReferences_該当なし_nullと0を返す()
    {
        var (json, count) = AssetRenamePlanner.RenameChildReferences(LinksJson, "nothing", "new");
        Assert.Null(json);
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ broken")]
    [InlineData("[]")]
    [InlineData("{\"parts\": []}")]
    [InlineData("{\"child_assets\": \"not-an-array\"}")]
    [InlineData("{\"child_assets\": [1, null, {\"child_asset_name\": 5}]}")]
    public void RenameChildReferences_壊れた入力や形の違う入力_例外を出さずnullを返す(string input)
    {
        var (json, count) = AssetRenamePlanner.RenameChildReferences(input, "old", "new");
        Assert.Null(json);
        Assert.Equal(0, count);
    }
}
