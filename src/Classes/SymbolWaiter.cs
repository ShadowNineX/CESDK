using System;
using System.Collections.Generic;
using CESDK.Utils;

namespace CESDK.Classes
{
    public enum SymbolLevel
    {
        Sections = 1,
        Exports = 2,
        DotNet = 3,
        PDB = 4
    }

    public class SymbolWaitException : CesdkException
    {
        public SymbolLevel Level { get; }

        public SymbolWaitException(SymbolLevel level, string message) : base($"Failed to wait for {level} symbols: {message}")
        {
            Level = level;
        }

        public SymbolWaitException(SymbolLevel level, string message, Exception innerException) : base($"Failed to wait for {level} symbols: {message}", innerException)
        {
            Level = level;
        }
    }

    public static class SymbolWaiter
    {
        private static readonly Dictionary<SymbolLevel, string> LevelFunctionMap = new()
        {
            [SymbolLevel.Sections] = "waitForSections",
            [SymbolLevel.Exports] = "waitForExports",
            [SymbolLevel.DotNet] = "waitForDotNet",
            [SymbolLevel.PDB] = "waitForPDB"
        };

        public static void WaitForSections() => WaitFor(SymbolLevel.Sections);
        public static void WaitForExports() => WaitFor(SymbolLevel.Exports);
        public static void WaitForDotNet() => WaitFor(SymbolLevel.DotNet);
        public static void WaitForPDB() => WaitFor(SymbolLevel.PDB);


        public static void WaitFor(SymbolLevel level)
        {
            if (!LevelFunctionMap.TryGetValue(level, out var functionName))
                throw new ArgumentException($"Invalid symbol level: {level}", nameof(level));

            try
            {
                LuaUtils.CallVoidLuaFunction(functionName, $"wait for {level} symbols");
            }
            catch (InvalidOperationException ex)
            {
                throw new SymbolWaitException(level, ex.Message, ex);
            }
        }

    }
}