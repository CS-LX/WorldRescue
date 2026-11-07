using Game;

namespace WorldRescue;

public static class Texts {
    public static bool English => !LanguageControl.CurrentLanguageName.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    public static string Get(string chinese, string english) => English ? english : chinese;
    public static string BlockName(int value) {
        if (Terrain.ExtractContents(value) == 0) return Get("空气", "Air");
        try { return BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(null, value); }
        catch { return Get("无法识别的方块", "Unrecognized block"); }
    }
}
