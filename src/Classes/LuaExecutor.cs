using System;
using System.Collections.Generic;
using CESDK.Lua;

namespace CESDK.Classes
{
    public class LuaExecutorException : CesdkException
    {
        public LuaExecutorException(string message) : base(message) { }
        public LuaExecutorException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Result of a Lua script execution, containing typed return values.
    /// </summary>
    public class LuaResult
    {
        /// <summary>Single return value (null if no returns or multiple returns)</summary>
        public object? Value { get; init; }

        /// <summary>All return values when multiple values are returned</summary>
        public List<object?>? Values { get; init; }

        /// <summary>Number of values returned by the script</summary>
        public int ReturnCount { get; init; }

        /// <summary>Whether the script returned any values</summary>
        public bool HasValue => ReturnCount > 0;
    }

    /// <summary>
    /// Executes Lua scripts in CE's Lua environment and serializes return values
    /// including tables, multiple returns, and nested structures.
    /// </summary>
    public static class LuaExecutor
    {
        private static readonly LuaNative lua = PluginContext.Lua;

        /// <summary>Maximum recursion depth when serializing nested tables</summary>
        public const int MaxTableDepth = 5;

        /// <summary>Maximum entries to read from a single table</summary>
        public const int MaxTableEntries = 100;

        /// <summary>
        /// Executes a Lua script and returns all results with full type serialization.
        /// </summary>
        /// <param name="script">The Lua code to execute. Use 'return' to get values back.</param>
        /// <returns>A LuaResult containing the serialized return values.</returns>
        /// <exception cref="LuaExecutorException">Thrown if the script fails to compile or execute.</exception>
        public static LuaResult Execute(string script)
        {
            int initialStackSize = lua.GetTop();
            try
            {
                lua.DoString(script);

                int returnCount = lua.GetTop() - initialStackSize;
                if (returnCount <= 0)
                    return new LuaResult { ReturnCount = 0 };

                if (returnCount == 1)
                    return new LuaResult { Value = ReadStackValue(-1), ReturnCount = 1 };

                var values = new List<object?>(returnCount);
                for (int i = 0; i < returnCount; i++)
                    values.Add(ReadStackValue(initialStackSize + 1 + i));

                return new LuaResult { Values = values, ReturnCount = returnCount };
            }
            catch (InvalidOperationException ex)
            {
                throw new LuaExecutorException(ex.Message, ex);
            }
            catch (Exception ex) when (ex is not LuaExecutorException)
            {
                throw new LuaExecutorException($"Lua execution failed: {ex.Message}", ex);
            }
            finally
            {
                lua.SetTop(initialStackSize);
            }
        }

        /// <summary>
        /// Reads any Lua value from the stack, recursively serializing tables.
        /// </summary>
        /// <param name="index">Stack index to read from.</param>
        /// <param name="depth">Current recursion depth for table serialization.</param>
        /// <returns>
        /// A C# representation of the Lua value:
        /// nil → null, boolean → bool, number → int/double, string → string,
        /// table → List or Dictionary, other → descriptive string.
        /// </returns>
        public static object? ReadStackValue(int index, int depth = 0)
        {
            int type = lua.Type(index);
            return type switch
            {
                0 => null,                                              // LUA_TNIL
                1 => lua.ToBoolean(index),                              // LUA_TBOOLEAN
                3 => lua.IsInteger(index)                               // LUA_TNUMBER
                    ? (object)lua.ToInteger64(index)
                    : lua.ToNumber(index),
                4 => lua.ToString(index),                               // LUA_TSTRING
                5 => ReadTable(index, depth),                           // LUA_TTABLE
                6 => "[function]",                                      // LUA_TFUNCTION
                7 => $"[userdata: 0x{lua.ToUserData(index):X}]",        // LUA_TUSERDATA
                2 => $"[lightuserdata: 0x{lua.ToUserData(index):X}]",   // LUA_TLIGHTUSERDATA
                8 => "[thread]",                                        // LUA_TTHREAD
                _ => $"[unknown type: {GetTypeName(type)}]"
            };
        }

        /// <summary>
        /// Returns the human-readable name for a Lua type constant.
        /// </summary>
        public static string GetTypeName(int luaType) => luaType switch
        {
            0 => "nil",
            1 => "boolean",
            2 => "lightuserdata",
            3 => "number",
            4 => "string",
            5 => "table",
            6 => "function",
            7 => "userdata",
            8 => "thread",
            _ => "unknown"
        };

        /// <summary>
        /// Reads a Lua table into either a List (sequential integer keys 1..N) or Dictionary.
        /// </summary>
        private static object ReadTable(int index, int depth)
        {
            if (depth >= MaxTableDepth)
                return "[table: max depth reached]";

            // Normalize to absolute index (negative indices shift as we push values)
            int absIndex = index > 0 ? index : lua.GetTop() + index + 1;

            // First pass: detect if table is a sequential array (keys 1..N)
            bool isArray = true;
            int arrayLen = 0;

            lua.PushNil();
            while (lua.Next(absIndex) != 0)
            {
                arrayLen++;
                if (arrayLen > MaxTableEntries)
                {
                    lua.Pop(2); // pop value + key to stop iteration
                    isArray = false;
                    break;
                }

                if (!lua.IsInteger(-2) || lua.ToInteger(-2) != arrayLen)
                    isArray = false;

                lua.Pop(1); // pop value, keep key for next iteration
            }

            if (isArray && arrayLen > 0)
                return ReadArrayTable(absIndex, arrayLen, depth);

            return ReadDictTable(absIndex, depth);
        }

        private static List<object?> ReadArrayTable(int absIndex, int length, int depth)
        {
            var list = new List<object?>(length);
            for (int i = 1; i <= length; i++)
            {
                lua.PushInteger(i);
                lua.GetTable(absIndex);
                list.Add(ReadStackValue(-1, depth + 1));
                lua.Pop(1);
            }
            return list;
        }

        private static Dictionary<string, object?> ReadDictTable(int absIndex, int depth)
        {
            var dict = new Dictionary<string, object?>();
            int entryCount = 0;

            lua.PushNil();
            while (lua.Next(absIndex) != 0)
            {
                if (++entryCount > MaxTableEntries)
                {
                    lua.Pop(2); // stop iteration
                    dict["..."] = $"truncated ({entryCount}+ entries)";
                    break;
                }

                string key = ReadKeyAsString(-2);
                dict[key] = ReadStackValue(-1, depth + 1);
                lua.Pop(1); // pop value, keep key
            }

            return dict;
        }

        /// <summary>
        /// Reads a Lua value at the given index and converts it to a string suitable for use as a dictionary key.
        /// </summary>
        private static string ReadKeyAsString(int index)
        {
            int keyType = lua.Type(index);
            return keyType switch
            {
                3 => lua.IsInteger(index) ? lua.ToInteger64(index).ToString() : lua.ToNumber(index).ToString(),
                4 => lua.ToString(index) ?? "",
                1 => lua.ToBoolean(index).ToString(),
                _ => $"[{GetTypeName(keyType)}]"
            };
        }
    }
}
