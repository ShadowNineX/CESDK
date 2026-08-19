using System;
using CESDK.Lua;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class FoundListException : CesdkException
    {
        public FoundListException(string message) : base(message) { }
        public FoundListException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// FoundList class that wraps Cheat Engine's FoundList Lua object for reading scan results
    /// </summary>
    public class FoundList : CEObjectWrapper
    {
        private bool _initialized = false;

        /// <summary>
        /// Creates an empty FoundList for setting CE object from stack
        /// </summary>
        internal FoundList()
        {
            // Empty constructor for setting CE object from stack
        }

        /// <summary>
        /// Creates a FoundList from a MemScan object using createFoundList
        /// </summary>
        internal FoundList(MemScan memScan)
        {
            CreateFoundListFromMemScan(memScan);
        }

        /// <summary>
        /// Creates a FoundList from a MemScan object using CE's createFoundList function
        /// </summary>
        private void CreateFoundListFromMemScan(MemScan memScan)
        {
            int initialTop = lua.GetTop();
            try
            {
                lua.GetGlobal("createFoundList");
                if (!lua.IsFunction(-1))
                    throw new FoundListException("createFoundList function not available");

                lua.PushCEObject(memScan.obj);
                int result = lua.PCall(1, 1);
                if (result != 0)
                    throw new FoundListException($"createFoundList() call failed: {lua.ToString(-1)}");

                if (!lua.IsCEObject(-1))
                    throw new FoundListException("createFoundList did not return a FoundList object");

                CEObject = lua.ToCEObject(-1);
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        /// <summary>
        /// Sets the CE object from a FoundList object on the Lua stack
        /// </summary>
        internal void SetCEObjectFromFoundListOnStack()
        {
            if (!lua.IsCEObject(-1))
                throw new FoundListException("Top of stack is not a FoundList CE object");

            SetCEObjectFromStack();
        }

        private void EnsureFoundListObject()
        {
            if (CEObject == IntPtr.Zero)
                throw new FoundListException("FoundList object is not initialized");
        }

        public void Initialize()
        {
            EnsureFoundListObject();
            CallMethod("initialize");
            _initialized = true;
        }

        /// <summary>
        /// Releases the FoundList results
        /// </summary>
        public void Deinitialize()
        {
            EnsureFoundListObject();
            CallMethod("deinitialize");
            _initialized = false;
        }

        /// <summary>
        /// Gets the number of results found
        /// </summary>
        public int Count => CallMethod(
            "getCount",
            () => { },
            0,
            () =>
            {
                if (!lua.IsNumber(-1))
                    throw new FoundListException("getCount did not return an integer");
                return lua.ToInteger(-1);
            });

        public string GetAddress(int index) =>
            CallMethod(
                "getAddress",
                () => lua.PushInteger(index),
                1,
                () => ReadRequiredString($"getAddress returned no address for index {index}"));

        public string GetValue(int index) =>
            CallMethod(
                "getValue",
                () => lua.PushInteger(index),
                1,
                () => ReadRequiredString($"getValue returned no value for index {index}"));

        /// <summary>
        /// Indexer for address access. According to celua.txt: foundlist[index] returns address
        /// </summary>
        /// <param name="index">Index (0-based)</param>
        /// <returns>Address as string</returns>
        public string this[int index] =>
            GetIndexedProperty(
                "Address",
                index,
                () => ReadRequiredString($"FoundList has no address at index {index}"));

        private string ReadRequiredString(string error)
        {
            if (!lua.IsString(-1))
                throw new FoundListException(error);
            return lua.ToString(-1) ?? "";
        }

        /// <summary>
        /// Gets whether the FoundList has been initialized
        /// </summary>
        public bool IsInitialized => _initialized;
    }
}