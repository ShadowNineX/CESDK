using CESDK.Utils;

namespace CESDK.Classes
{
    /// <summary>Loads and saves Cheat Engine table files.</summary>
    public static class CheatTable
    {
        /// <summary>Loads a .CT or .CETRAINER file into Cheat Engine.</summary>
        public static void Load(string filename, bool merge = false) =>
            LuaUtils.CallVoidLuaFunction("loadTable", "load cheat table", filename, merge);

        /// <summary>Saves the current Cheat Engine table.</summary>
        public static void Save(
            string filename,
            bool protect = false,
            bool dontDeactivateDesignerForms = false) =>
            LuaUtils.CallVoidLuaFunction(
                "saveTable",
                "save cheat table",
                filename,
                protect,
                dontDeactivateDesignerForms);
    }
}
