using System;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class ConverterException : CesdkException
    {
        public ConverterException(string message) : base(message) { }
        public ConverterException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class Converter
    {
        public static string StringToMD5(string value) =>
            WrapException(() => LuaUtils.CallLuaFunction("stringToMD5String", "convert string to MD5",
                () => PluginContext.Lua.ToString(-1) ?? "", value));

        public static string AnsiToUtf8(string text) =>
            WrapException(() => LuaUtils.CallLuaFunction("ansiToUTF8", "convert ANSI to UTF-8",
                () => PluginContext.Lua.ToString(-1) ?? "", text));

        public static string Utf8ToAnsi(string text) =>
            WrapException(() => LuaUtils.CallLuaFunction("UTF8ToAnsi", "convert UTF-8 to ANSI",
                () => PluginContext.Lua.ToString(-1) ?? "", text));

        private static T WrapException<T>(Func<T> operation)
        {
            try { return operation(); }
            catch (InvalidOperationException ex) { throw new ConverterException(ex.Message, ex); }
        }
    }
}