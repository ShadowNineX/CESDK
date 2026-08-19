using System;
using System.Collections.Generic;
using System.Text;
using CESDK.Utils;

namespace CESDK.Classes
{
    /// <summary>Target-process library injection, remote execution, and script generation.</summary>
    public static class Injection
    {
        private static readonly Lua.LuaNative lua = PluginContext.Lua;

        /// <summary>Injects a native library into the attached process.</summary>
        public static bool InjectLibrary(string filename, bool skipSymbolReloadWait = false) =>
            LuaUtils.CallLuaFunction(
                "injectDLL",
                "inject library",
                () => lua.ToBoolean(-1),
                filename,
                skipSymbolReloadWait);

        /// <summary>Injects a managed assembly and invokes a static method.</summary>
        public static void InjectDotNet(
            string dllPath,
            string fullClassName,
            string methodName,
            string parameterString,
            int? timeout = null)
        {
            if (timeout.HasValue)
                LuaUtils.CallVoidLuaFunction(
                    "injectDotNetDLL",
                    "inject .NET assembly",
                    dllPath,
                    fullClassName,
                    methodName,
                    parameterString,
                    timeout.Value);
            else
                LuaUtils.CallVoidLuaFunction(
                    "injectDotNetDLL",
                    "inject .NET assembly",
                    dllPath,
                    fullClassName,
                    methodName,
                    parameterString);
        }

        /// <summary>Executes a one-parameter target function and returns its integer result.</summary>
        public static long Execute(ulong address, long parameter = 0, int timeout = -1) =>
            LuaUtils.CallLuaFunction(
                "executeCode",
                "execute target code",
                ReadRequiredIntegerResult,
                address,
                parameter,
                timeout);

        /// <summary>Executes a target function with integer parameters.</summary>
        public static long? ExecuteExtended(
            int callMethod,
            int timeout,
            ulong address,
            IReadOnlyList<long> parameters)
        {
            var args = new object?[3 + parameters.Count];
            args[0] = callMethod;
            args[1] = timeout;
            args[2] = address;
            for (int i = 0; i < parameters.Count; i++)
                args[i + 3] = parameters[i];

            return LuaUtils.CallLuaFunction(
                "executeCodeEx",
                "execute target code with parameters",
                ReadOptionalIntegerResult,
                args);
        }

        /// <summary>Generates an Auto Assembler API hook script.</summary>
        public static string GenerateApiHookScript(
            string address,
            string jumpTarget,
            string? newCallAddress = null,
            string? extension = null,
            bool targetSelf = false) =>
            LuaUtils.CallLuaFunction(
                "generateAPIHookScript",
                "generate API hook script",
                () => lua.ToString(-1),
                address,
                jumpTarget,
                newCallAddress,
                extension,
                targetSelf);

        /// <summary>Generates a standard Auto Assembler code-injection script.</summary>
        public static string GenerateCodeInjectionScript(string address, bool farJump = false)
        {
            string script = $"local s=createStringList(); local ok,e=pcall(function() generateCodeInjectionScript(s,{Quote(address)},{(farJump ? "true" : "false")}) end); if not ok then s.destroy(); error(e) end; local r=s.Text; s.destroy(); return r";
            return LuaExecutor.Execute(script).Value as string
                ?? throw new InvalidOperationException("Cheat Engine did not generate an injection script");
        }

        /// <summary>Compiles C source for the target and returns the exported symbol table.</summary>
        public static object? CompileC(
            string source,
            ulong? address = null,
            bool targetSelf = false,
            bool kernelMode = false,
            bool noDebug = false)
        {
            int initialTop = lua.GetTop();
            try
            {
                lua.GetGlobal("compile");
                if (!lua.IsFunction(-1))
                    throw new InvalidOperationException("compile function not available in this CE version");

                lua.PushString(source);
                if (address.HasValue)
                    lua.PushInteger((long)address.Value);
                else
                    lua.PushNil();
                lua.PushBoolean(targetSelf);
                lua.PushBoolean(kernelMode);
                lua.PushBoolean(noDebug);

                int result = lua.PCall(5, 2);
                if (result != 0)
                    throw new InvalidOperationException($"compile() call failed: {lua.ToString(-1)}");
                if (lua.IsNil(-2))
                    throw new InvalidOperationException($"C compilation failed: {lua.ToString(-1)}");
                if (!lua.IsTable(-2))
                    throw new InvalidOperationException("compile() returned an invalid symbol table");

                return LuaExecutor.ReadStackValue(-2);
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        private static long ReadRequiredIntegerResult()
        {
            if (!lua.IsNumber(-1))
                throw new InvalidOperationException("Cheat Engine did not return an execution result");
            return lua.ToInteger64(-1);
        }

        private static long? ReadOptionalIntegerResult()
        {
            if (lua.IsNil(-1))
                return null;
            if (!lua.IsNumber(-1))
                throw new InvalidOperationException("Cheat Engine returned an invalid execution result");
            return lua.ToInteger64(-1);
        }

        private static string Quote(string value)
        {
            var result = new StringBuilder(value.Length + 2);
            result.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\': result.Append("\\\\"); break;
                    case '"': result.Append("\\\""); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (character < ' ')
                            result.Append($"\\{(int)character:D3}");
                        else
                            result.Append(character);
                        break;
                }
            }
            return result.Append('"').ToString();
        }
    }
}
