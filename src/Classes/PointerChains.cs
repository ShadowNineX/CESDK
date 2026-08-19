using System;
using System.Collections.Generic;
using System.Globalization;

namespace CESDK.Classes
{
    /// <summary>One dereference performed while resolving a pointer chain.</summary>
    public sealed record PointerChainStep(ulong ReadAddress, ulong PointerValue, long Offset, ulong ResultAddress, bool Readable);

    /// <summary>Resolved pointer-chain state.</summary>
    public sealed record PointerChainResult(ulong Address, IReadOnlyList<PointerChainStep> Steps, bool Valid);

    /// <summary>Pointer-chain resolution, validation, and bounded direct-reference scans.</summary>
    public static class PointerChains
    {
        /// <summary>Resolves a chain by dereferencing the current address, then applying each signed offset.</summary>
        public static PointerChainResult Resolve(ulong baseAddress, IReadOnlyList<long> offsets)
        {
            var steps = new List<PointerChainStep>(offsets.Count);
            ulong current = baseAddress;

            foreach (long offset in offsets)
            {
                MemoryProtection protection = MemoryRegions.GetMemoryProtection(current);
                if (!protection.Read)
                    return new PointerChainResult(current, steps, false);

                ulong pointer = MemoryAccess.ReadPointer(current);
                ulong result = AddOffset(pointer, offset);
                bool readable = MemoryRegions.GetMemoryProtection(result).Read;
                steps.Add(new PointerChainStep(current, pointer, offset, result, readable));
                current = result;

                if (!readable)
                    return new PointerChainResult(current, steps, false);
            }

            return new PointerChainResult(current, steps, true);
        }

        /// <summary>Scans a bounded address range for direct pointers to a target address.</summary>
        public static List<ulong> FindDirectReferences(
            ulong targetAddress,
            ulong startAddress,
            ulong stopAddress,
            int pointerSize,
            string protectionFlags,
            int maxResults)
        {
            if (pointerSize is not 4 and not 8)
                throw new ArgumentOutOfRangeException(nameof(pointerSize), "Pointer size must be 4 or 8 bytes");
            if (stopAddress < startAddress)
                throw new ArgumentException("Stop address must not precede start address");

            var scanner = new MemScan();
            try
            {
                scanner.FirstScan(new ScanParameters
                {
                    ScanOption = ScanOption.soExactValue,
                    VarType = pointerSize == 8 ? VariableType.vtQword : VariableType.vtDword,
                    Input1 = targetAddress.ToString("X", CultureInfo.InvariantCulture),
                    Input2 = string.Empty,
                    StartAddress = startAddress,
                    StopAddress = stopAddress,
                    ProtectionFlags = protectionFlags,
                    AlignmentType = AlignmentType.fsmAligned,
                    AlignmentParam = pointerSize.ToString(CultureInfo.InvariantCulture),
                    IsHexadecimalInput = true
                });
                scanner.WaitTillDone();
                scanner.InitializeResults();

                int count = Math.Min(scanner.GetResultCount(), maxResults);
                var addresses = new List<ulong>(count);
                for (int i = 0; i < count; i++)
                {
                    string text = scanner.GetResultAddress(i);
                    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        text = text.Substring(2);
                    if (ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong address))
                        addresses.Add(address);
                }
                return addresses;
            }
            finally
            {
                scanner.Dispose();
            }
        }

        private static ulong AddOffset(ulong address, long offset) =>
            offset >= 0 ? checked(address + (ulong)offset) : checked(address - (ulong)(-offset));
    }
}
