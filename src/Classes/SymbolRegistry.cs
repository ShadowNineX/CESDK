using CESDK.Utils;

namespace CESDK.Classes
{
    /// <summary>RTTI inspection and user-defined symbol registration.</summary>
    public static class SymbolRegistry
    {
        /// <summary>Returns the RTTI class name for an address, or null when unavailable.</summary>
        public static string? GetRttiClassName(ulong address) =>
            LuaUtils.CallLuaFunction(
                "getRTTIClassName",
                "get RTTI class name",
                () => PluginContext.Lua.IsNil(-1) ? null : PluginContext.Lua.ToString(-1),
                address);

        /// <summary>Registers a user-defined symbol.</summary>
        public static void Register(string name, ulong address, bool doNotSave = false) =>
            LuaUtils.CallVoidLuaFunction("registerSymbol", "register symbol", name, address, doNotSave);

        /// <summary>Unregisters a user-defined symbol.</summary>
        public static void Unregister(string name) =>
            LuaUtils.CallVoidLuaFunction("unregisterSymbol", "unregister symbol", name);

        /// <summary>Returns all symbols registered through Cheat Engine.</summary>
        public static object? Enumerate() =>
            LuaUtils.CallLuaFunction(
                "enumRegisteredSymbols",
                "enumerate registered symbols",
                () => LuaExecutor.ReadStackValue(-1));
    }
}
