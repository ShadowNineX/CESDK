using System;
using CESDK.Lua;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class MemScanException : CesdkException
    {
        public MemScanException(string message) : base(message) { }
        public MemScanException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Memory scanning class that wraps Cheat Engine's MemScan Lua object
    /// </summary>
    public class MemScan : CEObjectWrapper
    {
        public IntPtr obj { get { return CEObject; } }

        /// <summary>
        /// Whether this MemScan is the main CE UI scanner (from getCurrentMemscan).
        /// If true, the destructor will NOT call destroy on it.
        /// </summary>
        public bool IsMainScanner { get; private set; }

        /// <summary>
        /// Internal constructor for wrapping an existing CE MemScan object (e.g. from getCurrentMemscan)
        /// </summary>
        private MemScan(bool isMainScanner)
        {
            IsMainScanner = isMainScanner;
        }

        /// <summary>
        /// Creates a new independent MemScan object via createMemScan().
        /// This does NOT sync with the CE GUI.
        /// </summary>
        public MemScan()
        {
            IsMainScanner = false;
            int initialTop = lua.GetTop();
            try
            {
                lua.GetGlobal("createMemScan");
                if (!lua.IsFunction(-1))
                    throw new MemScanException("createMemScan function not available");

                int result = lua.PCall(0, 1);
                if (result != 0)
                    throw new MemScanException($"createMemScan() call failed: {lua.ToString(-1)}");

                if (!lua.IsCEObject(-1))
                    throw new MemScanException("createMemScan did not return a MemScan object");

                CEObject = lua.ToCEObject(-1);
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        /// <summary>
        /// Returns the current main CE GUI memory scan object via getCurrentMemscan().
        /// Scanning with this object syncs with the Cheat Engine UI (results appear in the foundlist panel).
        /// </summary>
        public static MemScan GetCurrentMemScan()
        {
            var lua = PluginContext.Lua;
            int initialTop = lua.GetTop();
            try
            {
                lua.GetGlobal("getCurrentMemscan");
                if (!lua.IsFunction(-1))
                    throw new MemScanException("getCurrentMemscan function not available");

                int result = lua.PCall(0, 1);
                if (result != 0)
                    throw new MemScanException($"getCurrentMemscan() call failed: {lua.ToString(-1)}");

                if (!lua.IsCEObject(-1))
                    throw new MemScanException("getCurrentMemscan did not return a MemScan object");

                var memScan = new MemScan(isMainScanner: true)
                {
                    CEObject = lua.ToCEObject(-1),
                    SuppressDestroy = true
                };
                return memScan;
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        /// <summary>
        /// Clears the current scan results (newScan). Use before starting a fresh first scan on the main scanner.
        /// </summary>
        public void NewScan() =>
            CallMethod("newScan");

        /// <summary>
        /// Deinitializes the foundlist attached to this memscan (clears the scan results UI panel).
        /// This should be called before NewScan() to properly clear the foundlist UI in Cheat Engine.
        /// </summary>
        public void DeinitializeFoundList()
        {
            FoundList? foundList = GetAttachedFoundList();
            if (foundList == null)
                return;

            foundList.Deinitialize();
            foundList.Dispose();
        }

        /// <summary>
        /// Gets the last scan type: 'stNewScan', 'stFirstScan', 'stNextScan'
        /// </summary>
        public string LastScanType => GetStringProperty("LastScanType");

        /// <summary>
        /// Returns true if the last scan was a region scan (unknown initial value).
        /// Region scans don't have individual addressable results.
        /// </summary>
        public bool LastScanWasRegionScan => GetBoolProperty("LastScanWasRegionScan");

        /// <summary>
        /// Returns true if a first scan has been completed (i.e. results exist for a next scan).
        /// </summary>
        public bool HasPreviousScan
        {
            get
            {
                var lst = LastScanType;
                return lst == "stFirstScan" || lst == "stNextScan";
            }
        }

        /// <summary>
        /// Performs a first or next scan based on the current MemScan state.
        /// </summary>
        public void Scan(ScanParameters parameters)
        {
            if (HasPreviousScan)
                NextScan(parameters);
            else
                FirstScan(parameters);
        }

        /// <summary>
        /// Performs an initial memory scan
        /// </summary>
        /// <param name="parameters">Scan parameters</param>
        public void FirstScan(ScanParameters parameters) =>
            CallMethod(
                "firstScan",
                () =>
                {
                    lua.PushInteger((long)parameters.ScanOption);
                    lua.PushInteger((long)(parameters.VarType ?? VariableType.vtDword));
                    lua.PushInteger((long)parameters.RoundingType);
                    lua.PushString(parameters.Input1);
                    lua.PushString(parameters.Input2);
                    lua.PushInteger((long)parameters.StartAddress);
                    lua.PushInteger((long)parameters.StopAddress);
                    lua.PushString(parameters.ProtectionFlags);
                    lua.PushInteger((long)parameters.AlignmentType);
                    lua.PushString(parameters.AlignmentParam);
                    lua.PushBoolean(parameters.IsHexadecimalInput);
                    lua.PushBoolean(parameters.IsNotABinaryString);
                    lua.PushBoolean(parameters.IsUnicodeScan);
                    lua.PushBoolean(parameters.IsCaseSensitive);
                },
                14);

        /// <summary>
        /// Performs a next scan based on previous scan results
        /// </summary>
        /// <param name="parameters">Scan parameters</param>
        public void NextScan(ScanParameters parameters)
        {
            bool saveResults = !string.IsNullOrEmpty(parameters.SavedResultName);
            CallMethod(
                "nextScan",
                () =>
                {
                    lua.PushInteger((long)parameters.ScanOption);
                    lua.PushInteger((long)parameters.RoundingType);
                    lua.PushString(parameters.Input1);
                    lua.PushString(parameters.Input2);
                    lua.PushBoolean(parameters.IsHexadecimalInput);
                    lua.PushBoolean(parameters.IsNotABinaryString);
                    lua.PushBoolean(parameters.IsUnicodeScan);
                    lua.PushBoolean(parameters.IsCaseSensitive);
                    lua.PushBoolean(parameters.IsPercentageScan);
                    if (saveResults)
                        lua.PushString(parameters.SavedResultName);
                },
                saveResults ? 10 : 9);
        }

        /// <summary>
        /// Waits for the scan to complete
        /// </summary>
        public void WaitTillDone() =>
            CallMethod("waitTillDone");

        #region High-level Result Access (handles FoundList internally)

        /// <summary>
        /// Cached FoundList wrapper to avoid creating multiple wrappers for the same CE object
        /// (which would cause double-destroy during GC).
        /// </summary>
        private FoundList? _cachedFoundList;

        /// <summary>
        /// Creates an independent FoundList for reading scan results via createFoundList(memscan).
        /// This avoids touching the main scanner's GUI FoundList which can cause access violations.
        /// Caches the result to avoid creating duplicate FoundLists.
        /// </summary>
        private FoundList GetInternalFoundList()
        {
            if (_cachedFoundList != null)
                return _cachedFoundList;

            try
            {
                _cachedFoundList = new FoundList(this);
                return _cachedFoundList;
            }
            catch (Exception ex)
            {
                throw new MemScanException("Failed to create FoundList for reading results", ex);
            }
        }

        /// <summary>
        /// Deinitializes and releases the cached results FoundList, if any.
        /// MUST be called before running another scan (especially nextScan): a
        /// FoundList left initialized over this memscan holds pointers into the
        /// previous result set, and scanning again frees/reallocates those
        /// results, so CE then writes through stale pointers and crashes the
        /// whole process. Safe to call when no results exist (no-op).
        /// </summary>
        public void DeinitializeResults()
        {
            if (_cachedFoundList == null)
                return;

            FoundList foundList = _cachedFoundList;
            _cachedFoundList = null;
            if (foundList.IsInitialized)
                foundList.Deinitialize();
            foundList.Dispose();
        }

        /// <summary>
        /// Initializes the scan results for reading. Call after WaitTillDone().
        /// Creates a new independent FoundList via createFoundList() and initializes it.
        /// </summary>
        public void InitializeResults()
        {
            DeinitializeResults();
            GetInternalFoundList().Initialize();
        }

        /// <summary>
        /// Gets the number of scan results. Call after InitializeResults().
        /// </summary>
        public int GetResultCount() =>
            GetInternalFoundList().Count;

        /// <summary>
        /// Gets the address at the specified result index as a string.
        /// </summary>
        /// <param name="index">Result index (0-based)</param>
        public string GetResultAddress(int index) =>
            GetInternalFoundList().GetAddress(index);

        /// <summary>
        /// Gets the value at the specified result index as a string.
        /// </summary>
        /// <param name="index">Result index (0-based)</param>
        public string GetResultValue(int index) =>
            GetInternalFoundList().GetValue(index);

        #endregion

        /// <summary>
        /// Saves current scan results with the given name
        /// </summary>
        /// <param name="name">Name to save results under</param>
        public void SaveCurrentResults(string name) =>
            CallMethod("saveCurrentResults", () => lua.PushString(name), 1);

        /// <summary>
        /// Gets the attached FoundList for reading scan results
        /// </summary>
        /// <returns>FoundList object or null if none attached</returns>
        public FoundList? GetAttachedFoundList() =>
            CallMethod(
                "getAttachedFoundlist",
                () => { },
                0,
                () =>
                {
                    if (lua.IsNil(-1))
                        return null;
                    if (!lua.IsCEObject(-1))
                        throw new MemScanException("getAttachedFoundlist returned an invalid object");

                    var foundList = new FoundList();
                    foundList.SetCEObjectFromFoundListOnStack();
                    foundList.SuppressDestroy = true;
                    return foundList;
                });

        /// <inheritdoc />
        public override void Dispose()
        {
            DeinitializeResults();
            base.Dispose();
        }
    }


    // Enums from celua.txt
    public enum ScanOption
    {
        soUnknownValue = 0,
        soExactValue = 1,
        soValueBetween = 2,
        soBiggerThan = 3,
        soSmallerThan = 4,
        soIncreasedValue = 5,
        soIncreasedValueBy = 6,
        soDecreasedValue = 7,
        soDecreasedValueBy = 8,
        soChanged = 9,
        soUnchanged = 10
    }

    public enum VariableType
    {
        vtByte = 0,
        vtWord = 1,
        vtDword = 2,
        vtQword = 3,
        vtSingle = 4,
        vtDouble = 5,
        vtString = 6,
        vtUnicodeString = 7,
        vtWideString = 7,
        vtByteArray = 8,
        vtBinary = 9,
        vtAll = 10,
        vtAutoAssembler = 11,
        vtPointer = 12,
        vtCustom = 13,
        vtGrouped = 14
    }

    public enum RoundingType
    {
        rtRounded = 0,
        rtExtremerounded = 1,
        rtTruncated = 2
    }

    public enum AlignmentType
    {
        fsmNotAligned = 0,
        fsmAligned = 1,
        fsmLastDigits = 2
    }

    public class ScanParameters
    {
        public ScanOption ScanOption { get; set; }
        public VariableType? VarType { get; set; }
        public RoundingType RoundingType { get; set; }
        public string Input1 { get; set; } = "";
        public string Input2 { get; set; } = "";
        public ulong StartAddress { get; set; } = 0;
        public ulong StopAddress { get; set; } = 0x7FFFFFFFFFFFFFFF;
        public string ProtectionFlags { get; set; } = "+W-C";
        public AlignmentType AlignmentType { get; set; } = AlignmentType.fsmAligned;
        public string AlignmentParam { get; set; } = "4";
        public bool IsHexadecimalInput { get; set; } = false;
        public bool IsNotABinaryString { get; set; } = false;
        public bool IsUnicodeScan { get; set; } = false;
        public bool IsCaseSensitive { get; set; } = false;
        public bool IsPercentageScan { get; set; } = false;
        public string SavedResultName { get; set; } = "";
    }
}