using System;
using System.Collections.Generic;
using CESDK.Lua;

namespace CESDK.Classes
{
    public class ThreadListException : CesdkException
    {
        public ThreadListException(string message) : base(message) { }
        public ThreadListException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// ThreadList class that wraps Cheat Engine's getThreadlist function
    /// </summary>
    public class ThreadList
    {
        private readonly LuaNative lua;
        private readonly List<string> threadIds = [];

        public ThreadList()
        {
            lua = PluginContext.Lua;
            Refresh();
        }

        /// <summary>
        /// Refreshes the thread list by reloading it from the process
        /// </summary>
        public void Refresh()
        {
            int initialTop = lua.GetTop();
            IntPtr stringList = IntPtr.Zero;
            try
            {
                threadIds.Clear();

                lua.GetGlobal("createStringlist");
                if (!lua.IsFunction(-1))
                    throw new ThreadListException("createStringlist function not available");

                int result = lua.PCall(0, 1);
                if (result != 0)
                    throw new ThreadListException($"createStringlist() call failed: {lua.ToString(-1)}");
                if (!lua.IsCEObject(-1))
                    throw new ThreadListException("createStringlist did not return a StringList object");

                stringList = lua.ToCEObject(-1);

                lua.GetGlobal("getThreadlist");
                if (!lua.IsFunction(-1))
                    throw new ThreadListException("getThreadlist function not available");
                lua.PushCEObject(stringList);
                result = lua.PCall(1, 0);
                if (result != 0)
                    throw new ThreadListException($"getThreadlist() call failed: {lua.ToString(-1)}");

                lua.PushCEObject(stringList);
                lua.GetField(-1, "Count");
                if (!lua.IsNumber(-1))
                    throw new ThreadListException("StringList.Count is not an integer");

                int count = lua.ToInteger(-1);
                lua.Pop(1);
                for (int i = 0; i < count; i++)
                {
                    lua.PushInteger(i);
                    lua.GetTable(-2);
                    try
                    {
                        if (!lua.IsString(-1))
                            throw new ThreadListException($"StringList item {i} is not a thread ID");

                        string threadId = lua.ToString(-1) ?? "";
                        if (!string.IsNullOrEmpty(threadId))
                            threadIds.Add(threadId);
                    }
                    finally
                    {
                        lua.Pop(1);
                    }
                }
            }
            catch (Exception ex) when (ex is not ThreadListException)
            {
                throw new ThreadListException("Failed to load thread list", ex);
            }
            finally
            {
                lua.SetTop(initialTop);
                if (stringList != IntPtr.Zero)
                    DestroyStringList(stringList, initialTop);
            }
        }

        private void DestroyStringList(IntPtr stringList, int initialTop)
        {
            try
            {
                lua.PushCEObject(stringList);
                lua.GetField(-1, "destroy");
                if (lua.IsFunction(-1))
                {
                    lua.PCall(0, 0);
                }
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        /// <summary>
        /// Gets the number of threads
        /// </summary>
        public int Count => threadIds.Count;

        /// <summary>
        /// Gets the thread ID at the specified index as a hex string
        /// </summary>
        public string GetThreadId(int index)
        {
            if (index < 0 || index >= threadIds.Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is out of range. Thread count: {threadIds.Count}");

            return threadIds[index];
        }

        /// <summary>
        /// Gets the thread ID at the specified index as an integer
        /// </summary>
        public int GetThreadIdAsInt(int index)
        {
            var hexString = GetThreadId(index);
            if (hexString.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hexString = hexString.Substring(2);

            return Convert.ToInt32(hexString, 16);
        }

        /// <summary>
        /// Gets all thread IDs as hex strings
        /// </summary>
        public string[] GetAllThreadIds() => threadIds.ToArray();

        /// <summary>
        /// Gets all thread IDs as integers
        /// </summary>
        public int[] GetAllThreadIdsAsInt()
        {
            var intIds = new int[threadIds.Count];
            for (int i = 0; i < threadIds.Count; i++)
                intIds[i] = GetThreadIdAsInt(i);
            return intIds;
        }

        /// <summary>
        /// Indexer for thread access
        /// </summary>
        public string this[int index] => GetThreadId(index);
    }
}