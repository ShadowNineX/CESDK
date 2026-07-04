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
            try
            {
                lua.GetGlobal("createMemScan");
                if (lua.IsNil(-1))
                    throw new MemScanException("You have no createMemScan (WTF)");

                lua.PCall(0, 1);

                if (lua.IsCEObject(-1))
                {
                    CEObject = lua.ToCEObject(-1);
                }
                else
                    throw new MemScanException("No idea what createMemScan returned");
            }
            finally
            {
                lua.SetTop(0);
            }
        }

        /// <summary>
        /// Returns the current main CE GUI memory scan object via getCurrentMemscan().
        /// Scanning with this object syncs with the Cheat Engine UI (results appear in the foundlist panel).
        /// </summary>
        public static MemScan GetCurrentMemScan()
        {
            var lua = PluginContext.Lua;
            try
            {
                lua.GetGlobal("getCurrentMemscan");
                if (lua.IsNil(-1))
                    throw new MemScanException("getCurrentMemscan is not available");

                lua.PCall(0, 1);

                if (lua.IsCEObject(-1))
                {
                    var memScan = new MemScan(isMainScanner: true);
                    memScan.CEObject = lua.ToCEObject(-1);
                    memScan.SuppressDestroy = true; // Don't destroy CE's own memscan
                    return memScan;
                }
                else
                    throw new MemScanException("getCurrentMemscan did not return a valid CE object");
            }
            finally
            {
                lua.SetTop(0);
            }
        }

        /// <summary>
        /// Clears the current scan results (newScan). Use before starting a fresh first scan on the main scanner.
        /// </summary>
        public void NewScan()
        {
            try
            {
                PushCEObject();
                lua.GetField(-1, "newScan");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new MemScanException("memscan object without a newScan method");
                }
                lua.PushValue(-2); // Push self
                lua.PCall(1, 0);
                lua.Pop(1); // Pop MemScan object
            }
            catch (MemScanException) { throw; }
            catch (Exception ex) { throw new MemScanException("Failed to call newScan", ex); }
        }

        /// <summary>
        /// Deinitializes the foundlist attached to this memscan (clears the scan results UI panel).
        /// This should be called before NewScan() to properly clear the foundlist UI in Cheat Engine.
        /// </summary>
        public void DeinitializeFoundList()
        {
            try
            {
                PushCEObject();  // Stack: [memscan]
                lua.GetField(-1, "FoundList");  // Stack: [memscan, foundlist or nil]

                if (lua.IsCEObject(-1))
                {
                    // Get the deinitialize method
                    lua.GetField(-1, "deinitialize");  // Stack: [memscan, foundlist, func or nil]
                    if (lua.IsFunction(-1))
                    {
                        lua.PushValue(-2); // Push foundlist as self - Stack: [memscan, foundlist, func, foundlist]
                        lua.PCall(1, 0);   // Call deinitialize - Stack: [memscan, foundlist]
                    }
                    else
                    {
                        lua.Pop(1); // Pop the non-function value - Stack: [memscan, foundlist]
                    }
                    lua.Pop(1); // Pop foundlist - Stack: [memscan]
                }
                else
                {
                    lua.Pop(1); // Pop nil/non-object - Stack: [memscan]
                }

                lua.Pop(1); // Pop memscan - Stack: []
            }
            catch (Exception ex)
            {
                lua.SetTop(0); // Clean up stack on error
                throw new MemScanException($"Failed to deinitialize foundlist: {ex.Message}", ex);
            }
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
        /// Sets scan properties on the memscan and calls scan() which auto-detects first vs next scan.
        /// This is the preferred method for the main UI scanner.
        /// </summary>
        public void Scan(ScanParameters parameters)
        {
            try
            {
                PushCEObject();

                // Set properties
                lua.PushInteger((long)parameters.ScanOption);
                lua.SetField(-2, "ScanOption");

                if (parameters.VarType.HasValue)
                {
                    lua.PushInteger((long)parameters.VarType.Value);
                    lua.SetField(-2, "VarType");
                }

                lua.PushInteger((long)parameters.RoundingType);
                lua.SetField(-2, "Roundingtype");

                lua.PushString(parameters.Input1 ?? "");
                lua.SetField(-2, "Scanvalue1");

                lua.PushString(parameters.Input2 ?? "");
                lua.SetField(-2, "Scanvalue2");

                lua.PushInteger((long)parameters.StartAddress);
                lua.SetField(-2, "Startaddress");

                lua.PushInteger((long)parameters.StopAddress);
                lua.SetField(-2, "Stopaddress");

                lua.PushBoolean(parameters.IsHexadecimalInput);
                lua.SetField(-2, "Hexadecimal");

                lua.PushBoolean(parameters.IsUnicodeScan);
                lua.SetField(-2, "UTF16");

                lua.PushBoolean(parameters.IsCaseSensitive);
                lua.SetField(-2, "Casesensitive");

                lua.PushBoolean(parameters.IsPercentageScan);
                lua.SetField(-2, "Percentage");

                lua.PushInteger((long)parameters.AlignmentType);
                lua.SetField(-2, "Fastscanmethod");

                lua.PushString(parameters.AlignmentParam ?? "4");
                lua.SetField(-2, "Fastscanparameter");

                // Call scan() which auto-detects first vs next
                lua.GetField(-1, "scan");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new MemScanException("memscan object without a scan method");
                }
                lua.PushValue(-2); // Push self
                lua.PCall(1, 0);

                lua.Pop(1); // Pop MemScan object
            }
            catch (MemScanException) { throw; }
            catch (Exception ex) { throw new MemScanException("Failed to call scan()", ex); }
        }

        /// <summary>
        /// Calls a method on the MemScan Lua object with parameters
        /// </summary>
        private void CallMemScanMethod(string methodName, string operationName, params object[] parameters)
        {
            try
            {
                PushCEObject();

                lua.GetField(-1, methodName);
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new InvalidOperationException($"{methodName} method not available on MemScan object");
                }

                // Push self (MemScan object)
                lua.PushValue(-2);

                // Push parameters using LuaUtils helper pattern
                foreach (var param in parameters)
                {
                    switch (param)
                    {
                        case int intValue: lua.PushInteger(intValue); break;
                        case long longValue: lua.PushInteger(longValue); break;
                        case ulong ulongValue: lua.PushInteger((long)ulongValue); break;
                        case string stringValue: lua.PushString(stringValue); break;
                        case bool boolValue: lua.PushBoolean(boolValue); break;
                        default: throw new ArgumentException($"Unsupported parameter type: {param.GetType()}");
                    }
                }

                var result = lua.PCall(1 + parameters.Length, 0);
                if (result != 0)
                {
                    var error = lua.ToString(-1);
                    lua.Pop(1);
                    throw new InvalidOperationException($"{methodName}() call failed: {error}");
                }

                lua.Pop(1); // Pop MemScan object
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex) { throw new MemScanException($"Failed to {operationName}", ex); }
        }

        /// <summary>
        /// Performs an initial memory scan
        /// </summary>
        /// <param name="parameters">Scan parameters</param>
        public void FirstScan(ScanParameters parameters)
        {
            try
            {
                lua.PushCEObject(CEObject);

                lua.PushString("firstScan");
                lua.GetTable(-2);

                if (!lua.IsFunction(-1))
                    throw new MemScanException("memscan object without a firstScan method");

                lua.PushValue(-2); // Push self
                lua.PushInteger((long)parameters.ScanOption);
                lua.PushInteger((long)(parameters.VarType ?? VariableType.vtDword));
                lua.PushInteger((long)parameters.RoundingType);
                lua.PushString(parameters.Input1 ?? "");
                lua.PushString(parameters.Input2 ?? "");
                lua.PushInteger((long)parameters.StartAddress);
                lua.PushInteger((long)parameters.StopAddress);
                lua.PushString(parameters.ProtectionFlags ?? "");
                lua.PushInteger((long)parameters.AlignmentType);
                lua.PushString(parameters.AlignmentParam ?? "4");
                lua.PushBoolean(parameters.IsHexadecimalInput);
                lua.PushBoolean(!parameters.IsNotABinaryString); // isnotabinarystring
                lua.PushBoolean(parameters.IsUnicodeScan);
                lua.PushBoolean(parameters.IsCaseSensitive);
                lua.PCall(15, 0);
            }
            finally
            {
                lua.SetTop(0);
            }
        }

        /// <summary>
        /// Performs a next scan based on previous scan results
        /// </summary>
        /// <param name="parameters">Scan parameters</param>
        public void NextScan(ScanParameters parameters)
        {
            try
            {
                lua.PushCEObject(CEObject);

                lua.PushString("nextScan");
                lua.GetTable(-2);

                if (!lua.IsFunction(-1))
                    throw new MemScanException("memscan object without a nextScan method");

                lua.PushValue(-2); // Push self
                lua.PushInteger((long)parameters.ScanOption);
                lua.PushInteger((long)parameters.RoundingType);
                lua.PushString(parameters.Input1 ?? "");
                lua.PushString(parameters.Input2 ?? "");
                lua.PushBoolean(parameters.IsHexadecimalInput);
                lua.PushBoolean(!parameters.IsNotABinaryString);
                lua.PushBoolean(parameters.IsUnicodeScan);
                lua.PushBoolean(parameters.IsCaseSensitive);
                lua.PushBoolean(parameters.IsPercentageScan);
                lua.PCall(10, 0);
            }
            finally
            {
                lua.SetTop(0);
            }
        }

        /// <summary>
        /// Waits for the scan to complete
        /// </summary>
        public void WaitTillDone()
        {
            try
            {
                lua.PushCEObject(CEObject);
                lua.PushString("waitTillDone");
                lua.GetTable(-2);

                if (!lua.IsFunction(-1))
                    throw new MemScanException("memscan object without a waitTillDone method");

                lua.PushValue(-2); // Push self
                lua.PCall(1, 0);
            }
            finally
            {
                lua.SetTop(0);
            }
        }

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
        private FoundList? GetInternalFoundList()
        {
            if (_cachedFoundList != null)
                return _cachedFoundList;

            try
            {
                // Use createFoundList(memscan) to create an independent FoundList
                // instead of accessing the .FoundList property (which is the GUI's own foundlist)
                var foundList = new FoundList(this);
                _cachedFoundList = foundList;
                return foundList;
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
            if (_cachedFoundList != null)
            {
                try { _cachedFoundList.Deinitialize(); } catch { /* ignore cleanup errors */ }
                _cachedFoundList = null;
            }
        }

        /// <summary>
        /// Initializes the scan results for reading. Call after WaitTillDone().
        /// Creates a new independent FoundList via createFoundList() and initializes it.
        /// </summary>
        public void InitializeResults()
        {
            // Clean up old FoundList if it exists
            if (_cachedFoundList != null)
            {
                try { _cachedFoundList.Deinitialize(); } catch { /* ignore cleanup errors */ }
                _cachedFoundList = null;
            }

            var foundList = GetInternalFoundList();
            if (foundList == null)
                throw new MemScanException("Failed to create FoundList. Did the scan complete successfully?");

            foundList.Initialize();
        }

        /// <summary>
        /// Gets the number of scan results. Call after InitializeResults().
        /// </summary>
        public int GetResultCount()
        {
            var foundList = GetInternalFoundList();
            if (foundList == null)
                return 0;

            return foundList.Count;
        }

        /// <summary>
        /// Gets the address at the specified result index as a string.
        /// </summary>
        /// <param name="index">Result index (0-based)</param>
        public string GetResultAddress(int index)
        {
            var foundList = GetInternalFoundList();
            if (foundList == null)
                throw new MemScanException("No results available");

            return foundList.GetAddress(index);
        }

        /// <summary>
        /// Gets the value at the specified result index as a string.
        /// </summary>
        /// <param name="index">Result index (0-based)</param>
        public string GetResultValue(int index)
        {
            var foundList = GetInternalFoundList();
            if (foundList == null)
                throw new MemScanException("No results available");

            return foundList.GetValue(index);
        }

        #endregion

        /// <summary>
        /// Saves current scan results with the given name
        /// </summary>
        /// <param name="name">Name to save results under</param>
        public void SaveCurrentResults(string name)
        {
            CallMemScanMethod("saveCurrentResults", "save current results", name);
        }

        /// <summary>
        /// Gets the attached FoundList for reading scan results
        /// </summary>
        /// <returns>FoundList object or null if none attached</returns>
        public FoundList? GetAttachedFoundList()
        {
            try
            {
                PushCEObject();

                // Try to get FoundList property first
                lua.GetField(-1, "FoundList");
                if (!lua.IsNil(-1))
                {
                    var foundList = new FoundList();
                    foundList.SetCEObjectFromFoundListOnStack();
                    lua.Pop(2); // Pop FoundList and MemScan object
                    return foundList;
                }
                lua.Pop(1); // Pop nil FoundList

                // Fallback to getAttachedFoundlist() method
                lua.GetField(-1, "getAttachedFoundlist");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2); // Pop non-function and MemScan object
                    return null;
                }

                // Push self (MemScan object)
                lua.PushValue(-2);

                // Call getAttachedFoundlist()
                var result = lua.PCall(1, 1);
                if (result != 0)
                {
                    lua.Pop(2); // Pop error and MemScan object
                    return null;
                }

                if (lua.IsNil(-1))
                {
                    lua.Pop(2); // Pop nil and MemScan object
                    return null;
                }

                var attachedFoundList = new FoundList();
                attachedFoundList.SetCEObjectFromFoundListOnStack();
                lua.Pop(2); // Pop FoundList and MemScan object
                return attachedFoundList;
            }
            catch (Exception ex)
            {
                throw new MemScanException("Failed to get attached found list", ex);
            }
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
        vtByteArray = 7,
        vtGrouped = 8,
        vtBinary = 9,
        vtAll = 10
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