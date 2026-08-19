using CESDK.Utils;

namespace CESDK.Classes
{
    /// <summary>Cheat Engine process creation and execution-state controls.</summary>
    public static class ProcessControl
    {
        /// <summary>Creates and opens a process through Cheat Engine.</summary>
        public static void Create(string path, string? parameters = null, bool debug = false, bool breakOnEntryPoint = false) =>
            LuaUtils.CallVoidLuaFunction(
                "createProcess",
                "create process",
                path,
                parameters ?? string.Empty,
                debug,
                breakOnEntryPoint);

        /// <summary>Pauses the currently opened process.</summary>
        public static void Pause() => LuaUtils.CallVoidLuaFunction("pause", "pause process");

        /// <summary>Resumes the currently opened process.</summary>
        public static void Resume() => LuaUtils.CallVoidLuaFunction("unpause", "resume process");

        /// <summary>Returns whether the opened process is paused.</summary>
        public static bool IsPaused() => LuaUtils.CallLuaFunction(
            "isPaused",
            "check process pause state",
            () => PluginContext.Lua.ToBoolean(-1));

        /// <summary>Returns the foreground process identifier.</summary>
        public static int GetForegroundProcessId() => LuaUtils.CallLuaFunction(
            "getForegroundProcess",
            "get foreground process",
            () => PluginContext.Lua.ToInteger(-1));

        /// <summary>Returns the age of the opened process in milliseconds.</summary>
        public static long GetProcessAge() => LuaUtils.CallLuaFunction(
            "getProcessAge",
            "get process age",
            () => PluginContext.Lua.ToInteger64(-1));
    }
}
