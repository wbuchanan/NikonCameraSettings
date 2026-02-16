#region "copyright"

/*
    Copyright © 2026 William Buchanan (william@williambuchanan.net)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Nikon;
using NikonCameraSettings.Models;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace NikonCameraSettings.Utils {

    public static class IptcPresetWriter {
        // ---------------------------------------------------------------------------
        // Nikon SDK IPTCPresetDataSet version constant: 0x00C8 = version 2.00.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "DatasetVersion"
        // For cameras with firmware update 1 (Z 8_FU1), version is 0x00C9 (2.01).
        // We use 0x00C8 for broad compatibility with Z 8 base firmware.
        // ---------------------------------------------------------------------------
        // private const ushort DatasetVersion = 0x00C8;

        // ---------------------------------------------------------------------------
        // IPTC Type identifier for XMP-IPTC content (as opposed to NMS-IPTC).
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "IPTC Type" field
        //   1 = NMS-IPTC (presets 1-10)
        //   2 = XMP-IPTC (presets 11-13)
        // ---------------------------------------------------------------------------
        private const byte IptcTypeXmp = 2;

        // ---------------------------------------------------------------------------
        // IPTC Type identifier for NMS-IPTC content (standard IPTC fields).
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "IPTC Type" field
        //   1 = NMS-IPTC (presets 1-10): 14 individual metadata fields
        //   2 = XMP-IPTC (presets 11-13): single XMP XML payload
        // ---------------------------------------------------------------------------
        private const byte IptcTypeNms = 1;

        // ---------------------------------------------------------------------------
        // Valid range for NMS-IPTC preset slots. The Nikon camera reserves
        // presets 1-10 for NMS-IPTC format and 11-13 for XMP-IPTC format.
        // Reference: Nikon SDK MAID3 Type0031 §3.263 "IPTCPresetSelect"
        // ---------------------------------------------------------------------------
        public const uint NmsPresetSlotMin = 1;

        public const uint NmsPresetSlotMax = 10;

        // ---------------------------------------------------------------------------
        // Number of reserved bytes that follow the IPTC Type field in the
        // IPTCPresetDataSet header, per the SDK documentation.
        // ---------------------------------------------------------------------------
        private const int ReservedByteCount = 5;

        // ---------------------------------------------------------------------------
        // Valid range for XMP-IPTC preset slots. The Nikon camera reserves
        // presets 11-13 for XMP-IPTC format content. With the unified single-slot
        // architecture, all metadata (GPS, astrometric, weather) is combined
        // into one XMP payload written to a user-selected slot in this range.
        //
        // Only one slot can be auto-embedded at a time (via IPTCPresetSelect),
        // which is why all data must share a single slot.
        //
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 and §3.263
        // ---------------------------------------------------------------------------
        public const uint XmpPresetSlotMin = 11;

        public const uint XmpPresetSlotMax = 13;

        // ---------------------------------------------------------------------------
        // Default XMP-IPTC preset slot used when the user hasn't explicitly chosen.
        // Slot 11 is the first available XMP slot and is the recommended default
        // since it leaves slots 12-13 free for future use or manual presets.
        // ---------------------------------------------------------------------------
        public const uint DefaultXmpPresetSlot = 11;

        // ---------------------------------------------------------------------------
        // Maximum IPTCPresetDataSet size for XMP-IPTC presets (slots 11-13).
        // The camera firmware validates that the buffer is exactly this size.
        // Calculated from §3.260.2: 2 (DatasetVersion) + 1 (IPTC Type)
        //   + 5 (Reserved) + [4+19] (Profile AUINT8: max 18 ASCII + NULL)
        //   + [4+30720] (XMP Data AUINT8: max 30 KB) = 30755 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260 ulSize specification
        // ---------------------------------------------------------------------------
        private const int MaxXmpPresetDataSetSize = 30755;

        // ---------------------------------------------------------------------------
        // Maximum IPTCPresetDataList size for a single XMP-IPTC preset.
        // = 4 bytes (NumElement) + MaxXmpPresetDataSetSize = 30759 bytes.
        // This is the exact value the camera firmware expects for ulSize when
        // writing to presets 11-13. Sending a smaller buffer triggers
        // kNkMAIDResult_ValueOutOfBounds.
        // Reference: Nikon SDK MAID3 Type0031 §3.260 — "When the number 11-13
        //   is specified in the ulPresetNo: 4byte + (the max size of
        //   IPTCPresetDataSet [30755byte] × the number of entry [1])"
        // ---------------------------------------------------------------------------
        private const int MaxXmpPresetDataListSize = 4 + MaxXmpPresetDataSetSize;

        // ---------------------------------------------------------------------------
        // Maximum IPTCPresetDataSet size for NMS-IPTC presets (slots 1-10).
        // For DatasetVersion 0x00C8 (v2.00), the max is 4984 bytes.
        // For DatasetVersion 0x00C9 (v2.01/Z 8_FU1), the max is 5237 bytes.
        // We use the v2.00 size since our DatasetVersion constant is 0x00C8.
        // Reference: Nikon SDK MAID3 Type0031 §3.260 ulSize specification
        // ---------------------------------------------------------------------------
        private const int MaxNmsPresetDataSetSizeV200 = 4984;

        // ---------------------------------------------------------------------------
        // Maximum IPTCPresetDataList size for a single NMS-IPTC preset (v2.00).
        // = 4 bytes (NumElement) + MaxNmsPresetDataSetSizeV200 = 4988 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260 — "When the number 1-10
        //   is specified in the ulPresetNo: [Z 8] 4byte + (the max size of
        //   IPTCPresetDataSet [4984byte] × the number of entry [1])"
        // ---------------------------------------------------------------------------
        //private const int MaxNmsPresetDataListSize = 4 + MaxNmsPresetDataSetSizeV200;

        private static Dictionary<uint, ushort> FirmwareToIptcDataset = new Dictionary<uint, ushort> {
            { 0x59, 0x00C8  },
            { 0x57, 0x00C8 },
            { 0x5A, 0x00C8 },
            { 0x5B, 0x00C8 },
            { 0x5C, 0x00C9 },
            { 0x5D, 0x00C9 },
            { 0x5E, 0x00C9 },
        };

        public static ushort GetDatasetVersion(NikonDevice camera) {
            uint camtype = camera.GetUnsigned(eNkMAIDCapability.kNkMAIDCapability_CameraType);
            return FirmwareToIptcDataset[camtype];
        }

        public static int GetMaxNmsPresetDataSetSize(ushort datasetVersion) {
            return datasetVersion == 0x00C8 ? 4 + MaxNmsPresetDataSetSizeV200 : MaxNmsPresetDataSetSizeV200 + 257;
        }

        // ---------------------------------------------------------------------------
        // Writes an XMP-IPTC payload to the specified preset slot on the camera.
        //
        // Parameters:
        //   camera       – The NikonDevice obtained via DeviceAccessor.GetNikonDevice()
        //   presetNumber – The preset slot (11, 12, or 13) to write to
        //   profileName  – ASCII profile name (max 18 chars), e.g. "GPS", "Astro", "Weather"
        //   xmpPayload   – The complete XMP XML string built by XmpBuilder
        //
        // This method:
        //   1. Validates inputs and checks camera capability support
        //   2. Builds the IPTCPresetDataList byte array in little-endian format
        //   3. Allocates unmanaged memory for the NkMAIDIPTCPresetInfo struct
        //   4. Calls NikonDevice.SetGeneric to write the data to the camera
        //   5. Frees all unmanaged memory in a finally block
        //
        // Throws:
        //   ArgumentException   – if inputs are invalid or out of range
        //   InvalidOperationException – if the camera doesn't support IPTC presets
        //   NikonException      – if the SDK returns an error
        // ---------------------------------------------------------------------------
        public static void WriteXmpIptcPreset(NikonDevice camera, uint presetNumber,
            string profileName, string xmpPayload) {
            // Guard: camera reference must not be null
            if (camera == null) {
                throw new ArgumentNullException(nameof(camera), "NikonDevice must not be null.");
            }

            // Guard: preset number must be within the XMP-IPTC slot range (11-13)
            if (presetNumber < XmpPresetSlotMin || presetNumber > XmpPresetSlotMax) {
                throw new ArgumentOutOfRangeException(nameof(presetNumber),
                    $"XMP-IPTC preset number must be between {XmpPresetSlotMin} and {XmpPresetSlotMax}.");
            }

            // Guard: XMP payload must not be empty
            if (string.IsNullOrEmpty(xmpPayload)) {
                throw new ArgumentException("XMP payload must not be null or empty.", nameof(xmpPayload));
            }

            // -----------------------------------------------------------------------
            // Verify the camera supports the IPTC preset capability before attempting
            // the set operation, to provide a clear error message instead of a
            // generic SDK exception.
            // Reference: nikoncswrapper Nikon.cs — SupportsCapability method
            // -----------------------------------------------------------------------
            if (!camera.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetInfo)) {
                throw new InvalidOperationException(
                    "The connected camera does not support kNkMAIDCapability_IPTCPresetInfo. " +
                    "This capability is available on cameras like the Nikon Z 8.");
            }

            // Get the dataset version
            ushort DatasetVersion = GetDatasetVersion(camera);

            // -----------------------------------------------------------------------
            // Step 1: Build the IPTCPresetDataList as a byte array.
            // We construct the entire binary payload in managed memory first,
            // then copy it to unmanaged memory for the SDK call.
            // -----------------------------------------------------------------------
            byte[] dataListBytes = BuildXmpIptcPresetDataList(profileName, xmpPayload, DatasetVersion);

            // Log the size of the data being written for diagnostic purposes
            Logger.Debug($"IPTC preset data list size: {dataListBytes.Length} bytes for slot {presetNumber}");

            // -----------------------------------------------------------------------
            // Step 2: Allocate unmanaged memory and marshal the data.
            // We need two allocations:
            //   (a) pDataUnmanaged: holds the IPTCPresetDataList byte array
            //   (b) pStructUnmanaged: holds the NkMAIDIPTCPresetInfo struct
            //       (which contains ulPresetNo, ulSize, and a pointer to pData)
            //
            // Using Marshal.StructureToPtr with the nikoncswrapper's own
            // NkMAIDIPTCPresetInfo struct (Pack=2) ensures the binary layout
            // matches exactly what the Nikon SDK native DLL expects, including
            // proper alignment and field ordering.
            // Reference: nikoncswrapper NikonNativeStructs.cs — NkMAIDIPTCPresetInfo
            // -----------------------------------------------------------------------
            IntPtr pDataUnmanaged = IntPtr.Zero;
            IntPtr pStructUnmanaged = IntPtr.Zero;

            try {
                // -------------------------------------------------------------------
                // Allocate unmanaged memory for the IPTCPresetDataList and copy
                // the managed byte array into it. AllocHGlobal allocates from the
                // process heap and returns a pointer usable by native code.
                // Reference: https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.marshal.allochglobal
                // -------------------------------------------------------------------
                pDataUnmanaged = Marshal.AllocHGlobal(dataListBytes.Length);
                // Copy the managed byte array into the unmanaged memory block
                Marshal.Copy(dataListBytes, 0, pDataUnmanaged, dataListBytes.Length);

                // -------------------------------------------------------------------
                // Build the NkMAIDIPTCPresetInfo struct using the nikoncswrapper's
                // own type definition. This ensures the [StructLayout(Pack=2)]
                // attribute controls the exact memory layout, eliminating any
                // manual offset calculation errors.
                //
                // ulPresetNo: The target preset slot (11, 12, or 13)
                // ulSize:     Must be MaxXmpPresetDataListSize (30759 bytes) — the
                //             firmware validates this matches the expected maximum
                //             buffer size for XMP presets 11-13. Sending the actual
                //             content size (smaller) triggers ValueOutOfBounds.
                // pData:      Pointer to the IPTCPresetDataList buffer
                //
                // Reference: Nikon SDK MAID3 Type0031 §3.260
                //            nikoncswrapper NikonNativeStructs.cs — NkMAIDIPTCPresetInfo
                // -------------------------------------------------------------------
                NkMAIDIPTCPresetInfo presetInfo = new NkMAIDIPTCPresetInfo();
                // Set the target preset slot number for the camera firmware
                presetInfo.ulPresetNo = presetNumber;
                // Set the buffer size to the SDK-specified maximum for XMP presets
                presetInfo.ulSize = (uint)dataListBytes.Length;
                // Set the pointer to our IPTCPresetDataList buffer
                presetInfo.pData = pDataUnmanaged;

                // Compute the struct size using Marshal.SizeOf which respects Pack=2
                int structSize = Marshal.SizeOf<NkMAIDIPTCPresetInfo>();
                // Allocate unmanaged memory for the struct
                pStructUnmanaged = Marshal.AllocHGlobal(structSize);
                // Marshal the managed struct to unmanaged memory with correct layout
                // Reference: https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.marshal.structuretoptr
                Marshal.StructureToPtr(presetInfo, pStructUnmanaged, false);

                // -------------------------------------------------------------------
                // Step 3: Call NikonDevice.SetGeneric to send the IPTC preset data
                // to the camera. SetGeneric dispatches to NikonNative.CapSet with
                // kNkMAIDDataType_GenericPtr, which is the correct data type for
                // kNkMAIDCapability_IPTCPresetInfo (ulType = kNkMAIDCapType_Generic).
                // Reference: nikoncswrapper Nikon.cs — SetGeneric method
                //            Nikon SDK MAID3 §8.7 kNkMAIDCommand_CapSet
                // -------------------------------------------------------------------
                Logger.Info($"Writing XMP-IPTC data to camera preset slot {presetNumber}...");
                camera.SetGeneric(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetInfo, pStructUnmanaged);
                Logger.Info($"Successfully wrote XMP-IPTC data to preset slot {presetNumber}.");
            } catch (NikonException nex) {
                // -------------------------------------------------------------------
                // Log and rethrow Nikon SDK errors so the sequence item can handle
                // them. Common errors include DeviceBusy (camera is shooting),
                // ValueOutOfBounds, and InvalidData (malformed pData).
                // Reference: Nikon SDK MAID3 Type0031 §3.260 "Result Codes"
                // -------------------------------------------------------------------
                Logger.Error($"Nikon SDK error writing IPTC preset {presetNumber}: " +
                             $"ErrorCode={nex.ErrorCode}, Message={nex.Message}");
                throw;
            } catch (Exception ex) {
                // Log unexpected errors (e.g., access violations from bad pointers)
                Logger.Error($"Unexpected error writing IPTC preset {presetNumber}: {ex.Message}");
                throw;
            } finally {
                // -------------------------------------------------------------------
                // Always free unmanaged memory to prevent leaks. The order matters:
                // free pStructUnmanaged first (it references pDataUnmanaged), then
                // free pDataUnmanaged. FreeHGlobal is safe to call with IntPtr.Zero.
                // Reference: https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.marshal.freehglobal
                // -------------------------------------------------------------------
                if (pStructUnmanaged != IntPtr.Zero) {
                    Marshal.FreeHGlobal(pStructUnmanaged);
                }
                if (pDataUnmanaged != IntPtr.Zero) {
                    Marshal.FreeHGlobal(pDataUnmanaged);
                }
            }
        }

        // ---------------------------------------------------------------------------
        // Writes an NMS-IPTC payload to the specified preset slot (1-10) on the
        // camera. NMS-IPTC is the camera's native metadata format that stores
        // 14 individual fields (Caption, EventID, Headline, etc.) as separate
        // AUINT8 elements in the IPTCPresetDataSet structure.
        //
        // Unlike XMP-IPTC (presets 11-13) which uses a single XML blob, NMS-IPTC
        // writes each field individually, allowing the camera's built-in IPTC
        // editor to display and modify the fields natively.
        //
        // Parameters:
        //   camera       – The NikonDevice obtained via DeviceAccessor.GetNikonDevice()
        //   presetNumber – The preset slot (1-10) to write to
        //   profileName  – ASCII profile name (max 18 chars), e.g. "Astro Session"
        //   data         – The NmsIptcData containing the 14 field values
        //
        // This method:
        //   1. Validates inputs and checks camera capability support
        //   2. Builds the NMS-IPTC IPTCPresetDataList byte array
        //   3. Allocates unmanaged memory for the NkMAIDIPTCPresetInfo struct
        //   4. Calls NikonDevice.SetGeneric to write the data to the camera
        //   5. Frees all unmanaged memory in a finally block
        //
        // Throws:
        //   ArgumentException        – if inputs are invalid or out of range
        //   InvalidOperationException – if the camera doesn't support IPTC presets
        //   NikonException           – if the SDK returns an error
        //
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "NMS-IPTC" format
        // ---------------------------------------------------------------------------
        public static void WriteNmsIptcPreset(NikonDevice camera, uint presetNumber,
            string profileName, NmsIptcData data) {
            // Guard: camera reference must not be null
            if (camera == null) {
                throw new ArgumentNullException(nameof(camera), "NikonDevice must not be null.");
            }

            // Guard: preset number must be 1-10 for NMS-IPTC slots
            if (presetNumber < NmsPresetSlotMin || presetNumber > NmsPresetSlotMax) {
                throw new ArgumentOutOfRangeException(nameof(presetNumber),
                    $"NMS-IPTC preset number must be {NmsPresetSlotMin}-{NmsPresetSlotMax}.");
            }

            // Guard: data must not be null
            if (data == null) {
                throw new ArgumentNullException(nameof(data), "NmsIptcData must not be null.");
            }

            // -----------------------------------------------------------------------
            // Verify the camera supports the IPTC preset capability before attempting
            // the set operation, to provide a clear error message.
            // Reference: nikoncswrapper Nikon.cs — SupportsCapability method
            // -----------------------------------------------------------------------
            if (!camera.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetInfo)) {
                throw new InvalidOperationException(
                    "The connected camera does not support kNkMAIDCapability_IPTCPresetInfo. " +
                    "This capability is available on cameras like the Nikon Z 8.");
            }

            // -----------------------------------------------------------------------
            // Step 1: Build the NMS-IPTC IPTCPresetDataList as a byte array.
            // This constructs the complete binary payload with all 14 NMS fields
            // in managed memory before copying to unmanaged memory for the SDK call.
            // -----------------------------------------------------------------------
            byte[] dataListBytes = BuildNmsIptcPresetDataList(profileName, data, GetDatasetVersion(camera));

            // Log the size of the data being written for diagnostic purposes
            Logger.Debug($"NMS-IPTC preset data list size: {dataListBytes.Length} bytes for slot {presetNumber}");

            // -----------------------------------------------------------------------
            // Step 2: Allocate unmanaged memory and marshal the data.
            // Uses the same Marshal.StructureToPtr approach as WriteXmpIptcPreset
            // for consistent, correct struct layout across both IPTC types.
            // Reference: nikoncswrapper NikonNativeStructs.cs — NkMAIDIPTCPresetInfo
            // -----------------------------------------------------------------------
            IntPtr pDataUnmanaged = IntPtr.Zero;
            IntPtr pStructUnmanaged = IntPtr.Zero;

            try {
                // Allocate unmanaged memory for the NMS-IPTC data list
                pDataUnmanaged = Marshal.AllocHGlobal(dataListBytes.Length);
                // Copy the managed byte array into the unmanaged memory block
                Marshal.Copy(dataListBytes, 0, pDataUnmanaged, dataListBytes.Length);

                // -------------------------------------------------------------------
                // Build the NkMAIDIPTCPresetInfo struct using the nikoncswrapper's
                // own type definition with [StructLayout(Pack=2)].
                //
                // ulPresetNo: The target NMS preset slot (1-10)
                // ulSize:     Must be MaxNmsPresetDataListSize (4988 bytes for v2.00)
                //             — the firmware validates this matches the expected
                //             maximum buffer size for NMS presets 1-10.
                // pData:      Pointer to the IPTCPresetDataList buffer
                //
                // Reference: Nikon SDK MAID3 Type0031 §3.260
                // -------------------------------------------------------------------
                NkMAIDIPTCPresetInfo presetInfo = new NkMAIDIPTCPresetInfo();
                // Set the target NMS preset slot number for the camera firmware
                presetInfo.ulPresetNo = presetNumber;
                // Set the buffer size to the SDK-specified maximum for NMS presets
                presetInfo.ulSize = (uint)dataListBytes.Length;
                // Set the pointer to our IPTCPresetDataList buffer
                presetInfo.pData = pDataUnmanaged;

                // Compute the struct size using Marshal.SizeOf which respects Pack=2
                int structSize = Marshal.SizeOf<NkMAIDIPTCPresetInfo>();
                // Allocate unmanaged memory for the struct
                pStructUnmanaged = Marshal.AllocHGlobal(structSize);
                // Marshal the managed struct to unmanaged memory with correct layout
                Marshal.StructureToPtr(presetInfo, pStructUnmanaged, false);

                // -------------------------------------------------------------------
                // Step 3: Call NikonDevice.SetGeneric to send the NMS-IPTC data
                // to the camera via kNkMAIDCapability_IPTCPresetInfo.
                // Reference: nikoncswrapper Nikon.cs — SetGeneric method
                // -------------------------------------------------------------------
                Logger.Info($"Writing NMS-IPTC data to camera preset slot {presetNumber}...");
                camera.SetGeneric(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetInfo, pStructUnmanaged);
                Logger.Info($"Successfully wrote NMS-IPTC data to preset slot {presetNumber}.");
            } catch (NikonException nex) {
                // Log and rethrow Nikon SDK errors for the sequence item to handle
                Logger.Error($"Nikon SDK error writing NMS-IPTC preset {presetNumber}: " +
                             $"ErrorCode={nex.ErrorCode}, Message={nex.Message}");
                throw;
            } catch (Exception ex) {
                // Log unexpected errors (e.g., access violations from bad pointers)
                Logger.Error($"Unexpected error writing NMS-IPTC preset {presetNumber}: {ex.Message}");
                throw;
            } finally {
                // -------------------------------------------------------------------
                // Always free unmanaged memory to prevent leaks.
                // Reference: https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.marshal.freehglobal
                // -------------------------------------------------------------------
                if (pStructUnmanaged != IntPtr.Zero) {
                    Marshal.FreeHGlobal(pStructUnmanaged);
                }
                if (pDataUnmanaged != IntPtr.Zero) {
                    Marshal.FreeHGlobal(pDataUnmanaged);
                }
            }
        }

        // ---------------------------------------------------------------------------
        // Builds the IPTCPresetDataList byte array containing exactly one
        // NMS-type IPTCPresetDataSet with all 14 standard IPTC fields.
        //
        // Layout (all little-endian):
        //   [4 bytes] NumElement = 1
        //   [variable] IPTCPresetDataSet {
        //     [2 bytes] DatasetVersion (0x00C8)
        //     [1 byte]  IPTC Type (1 = NMS)
        //     [5 bytes] Reserved (zeroed)
        //     AUINT8    Profile       (ASCII)
        //     AUINT8    Caption       (UTF-8)
        //     AUINT8    EventID       (UTF-8)
        //     AUINT8    Headline      (UTF-8)
        //     AUINT8    ObjectName    (UTF-8)
        //     AUINT8    City          (UTF-8)
        //     AUINT8    State         (UTF-8)
        //     AUINT8    Country       (UTF-8)
        //     AUINT8    Category      (ASCII)
        //     AUINT8    SuppCat       (UTF-8)
        //     AUINT8    Byline        (UTF-8)
        //     AUINT8    BylineTitle   (UTF-8)
        //     AUINT8    WriterEditor  (UTF-8)
        //     AUINT8    Credit        (UTF-8)
        //     AUINT8    Source        (UTF-8)
        //   }
        //
        // Each AUINT8 field:
        //   - If non-empty: [4-byte uint32 length] + [data bytes] + [0x00 null]
        //     where length = data byte count + 1 (for the null terminator)
        //   - If empty: [4 bytes 0x00000000] (no data follows)
        //
        // Reference: Nikon SDK MAID3 Type0031 §3.260.1 and §3.260.2
        // ---------------------------------------------------------------------------
        private static byte[] BuildNmsIptcPresetDataList(string profileName, NmsIptcData data, ushort DatasetVersion) {
            // -----------------------------------------------------------------------
            // Encode the profile name as ASCII with a null terminator.
            // The SDK limits profile names to 18 ASCII characters + 1 null byte.
            // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Profile" field
            // -----------------------------------------------------------------------
            string truncatedProfile = (profileName ?? "").Length > 18
                ? profileName.Substring(0, 18)
                : (profileName ?? "");

            // -----------------------------------------------------------------------
            // Pre-encode all fields to calculate the total buffer size.
            // Each non-empty field occupies: 4 (length prefix) + N (data) + 1 (null)
            // Each empty field occupies: 4 (zero length, no data follows)
            //
            // We use a helper struct array to keep the encoding and size calculation
            // organized and to avoid encoding each field twice.
            // -----------------------------------------------------------------------
            EncodedField profileField = EncodeAsciiField(truncatedProfile);
            EncodedField captionField = EncodeUtf8Field(data.Caption);
            EncodedField eventIdField = EncodeUtf8Field(data.EventId);
            EncodedField headlineField = EncodeUtf8Field(data.Headline);
            EncodedField objectNameField = EncodeUtf8Field(data.ObjectName);
            EncodedField cityField = EncodeUtf8Field(data.City);
            EncodedField stateField = EncodeUtf8Field(data.State);
            EncodedField countryField = EncodeUtf8Field(data.Country);
            // Category is ASCII-only per the SDK specification
            EncodedField categoryField = EncodeAsciiField(data.Category);
            EncodedField suppCatField = EncodeUtf8Field(data.SuppCat);
            EncodedField bylineField = EncodeUtf8Field(data.Byline);
            EncodedField bylineTitleField = EncodeUtf8Field(data.BylineTitle);
            EncodedField writerEditorField = EncodeUtf8Field(data.WriterEditor);
            EncodedField creditField = EncodeUtf8Field(data.Credit);
            EncodedField sourceField = EncodeUtf8Field(data.Source);

            // -----------------------------------------------------------------------
            // Calculate total buffer size:
            //   4 bytes  → NumElement (uint32)
            //   2 bytes  → DatasetVersion (uint16)
            //   1 byte   → IPTC Type
            //   5 bytes  → Reserved
            //   + sum of all 15 AUINT8 field sizes (1 profile + 14 NMS data fields)
            //
            // Each AUINT8 field contributes either:
            //   4 bytes (empty: just the zero-length prefix), or
            //   4 + dataBytes.Length + 1 bytes (non-empty: prefix + data + null)
            // -----------------------------------------------------------------------
            // -----------------------------------------------------------------------
            // Calculate actual content size for validation, then allocate the
            // FULL maximum buffer size (4988 bytes for v2.00) required by the
            // Nikon SDK firmware. The camera validates ulSize against the
            // expected maximum for presets 1-10 and returns
            // kNkMAIDResult_ValueOutOfBounds if the size does not match.
            //
            // The remainder of the buffer beyond the content is zero-filled
            // (the default for new byte[] in C#), which the firmware ignores.
            //
            // Reference: Nikon SDK MAID3 Type0031 §3.260 — "[Z 8] 4byte +
            //   (the max size of IPTCPresetDataSet [4984byte] × the number
            //   of entry [1])"
            // -----------------------------------------------------------------------
            int contentSize = 4 + 2 + 1 + 5
                + profileField.TotalSize
                + captionField.TotalSize
                + eventIdField.TotalSize
                + headlineField.TotalSize
                + objectNameField.TotalSize
                + cityField.TotalSize
                + stateField.TotalSize
                + countryField.TotalSize
                + categoryField.TotalSize
                + suppCatField.TotalSize
                + bylineField.TotalSize
                + bylineTitleField.TotalSize
                + writerEditorField.TotalSize
                + creditField.TotalSize
                + sourceField.TotalSize;

            // Allocate the full maximum buffer; all bytes initialized to zero by C#
            byte[] buffer = new byte[GetMaxNmsPresetDataSetSize(DatasetVersion)];
            // Track the current write position in the buffer
            int offset = 0;

            // -----------------------------------------------------------------------
            // Write NumElement = 1 (little-endian uint32).
            // We write exactly one IPTCPresetDataSet for the Set operation.
            // Reference: Nikon SDK MAID3 Type0031 §3.260.1 "NumElement"
            // -----------------------------------------------------------------------
            WriteUInt32LE(buffer, ref offset, 1);

            // -----------------------------------------------------------------------
            // Write DatasetVersion = 0x00C8 (little-endian uint16, version 2.00).
            // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "DatasetVersion"
            // -----------------------------------------------------------------------
            WriteUInt16LE(buffer, ref offset, DatasetVersion);

            // -----------------------------------------------------------------------
            // Write IPTC Type = 1 (NMS-IPTC).
            // This distinguishes NMS presets (1-10) from XMP presets (11-13).
            // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "IPTC Type"
            // -----------------------------------------------------------------------
            buffer[offset++] = IptcTypeNms;

            // -----------------------------------------------------------------------
            // Write 5 reserved bytes (already zeroed by array initialization).
            // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Reserved"
            // -----------------------------------------------------------------------
            offset += ReservedByteCount;

            // -----------------------------------------------------------------------
            // Write all 15 AUINT8 fields in the exact order specified by the SDK.
            // The order MUST match §3.260.2: Profile, Caption, EventID, Headline,
            // ObjectName, City, State, Country, Category, SuppCat, Byline,
            // BylineTitle, WriterEditor, Credit, Source.
            //
            // Each field is written using WriteAuint8Field which handles both the
            // non-empty case (length + data + null) and the empty case (zero length).
            // -----------------------------------------------------------------------
            WriteAuint8Field(buffer, ref offset, profileField);
            WriteAuint8Field(buffer, ref offset, captionField);
            WriteAuint8Field(buffer, ref offset, eventIdField);
            WriteAuint8Field(buffer, ref offset, headlineField);
            WriteAuint8Field(buffer, ref offset, objectNameField);
            WriteAuint8Field(buffer, ref offset, cityField);
            WriteAuint8Field(buffer, ref offset, stateField);
            WriteAuint8Field(buffer, ref offset, countryField);
            WriteAuint8Field(buffer, ref offset, categoryField);
            WriteAuint8Field(buffer, ref offset, suppCatField);
            WriteAuint8Field(buffer, ref offset, bylineField);
            WriteAuint8Field(buffer, ref offset, bylineTitleField);
            WriteAuint8Field(buffer, ref offset, writerEditorField);
            WriteAuint8Field(buffer, ref offset, creditField);
            WriteAuint8Field(buffer, ref offset, sourceField);

            // -----------------------------------------------------------------------
            // Verify we wrote exactly the expected content bytes. The buffer
            // is larger (MaxNmsPresetDataListSize = 4988), but the remaining
            // bytes after the content are zero-padded by the array initializer.
            // -----------------------------------------------------------------------
            if (offset != contentSize) {
                throw new InvalidOperationException(
                    $"NMS-IPTC buffer content mismatch: expected {contentSize} bytes but wrote {offset}.");
            }

            // Log the content size vs total buffer size for diagnostic purposes
            Logger.Debug($"NMS-IPTC content: {contentSize} bytes in {GetMaxNmsPresetDataSetSize(DatasetVersion)}-byte buffer");

            return buffer;
        }

        // ---------------------------------------------------------------------------
        // Lightweight struct to hold a pre-encoded AUINT8 field's byte data and
        // its total contribution to the buffer size. Using a struct avoids heap
        // allocations for the 15 fields we need to encode per NMS-IPTC write.
        // ---------------------------------------------------------------------------
        private struct EncodedField {

            // The encoded data bytes (null if the source string was empty)
            public byte[] DataBytes;

            // Total bytes this field occupies in the buffer:
            //   Empty: 4 (just the zero-length prefix)
            //   Non-empty: 4 (length prefix) + DataBytes.Length + 1 (null terminator)
            public int TotalSize;
        }

        // ---------------------------------------------------------------------------
        // Encodes a string as UTF-8 for an NMS-IPTC AUINT8 field. Returns an
        // EncodedField struct with the byte data and total buffer size contribution.
        //
        // For empty/null strings, DataBytes is null and TotalSize is 4 (just the
        // zero-length prefix), matching the SDK spec: "If the element is empty,
        // the element count is 0x00000000 and no element exists."
        //
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 — AUINT8 field format
        // ---------------------------------------------------------------------------
        private static EncodedField EncodeUtf8Field(string value) {
            // Empty or null strings produce a zero-length AUINT8 (4 bytes only)
            if (string.IsNullOrEmpty(value)) {
                return new EncodedField { DataBytes = null, TotalSize = 4 };
            }
            // Encode the string as UTF-8
            byte[] encoded = Encoding.UTF8.GetBytes(value);
            // Total: 4 (length prefix) + encoded bytes + 1 (null terminator)
            return new EncodedField { DataBytes = encoded, TotalSize = 4 + encoded.Length + 1 };
        }

        // ---------------------------------------------------------------------------
        // Encodes a string as ASCII for an NMS-IPTC AUINT8 field. Used for the
        // Profile and Category fields which are specified as ASCII-only.
        //
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 — "Profile: ASCII",
        //            "Category: ASCII"
        // ---------------------------------------------------------------------------
        private static EncodedField EncodeAsciiField(string value) {
            // Empty or null strings produce a zero-length AUINT8 (4 bytes only)
            if (string.IsNullOrEmpty(value)) {
                return new EncodedField { DataBytes = null, TotalSize = 4 };
            }
            // Encode the string as ASCII
            byte[] encoded = Encoding.ASCII.GetBytes(value);
            // Total: 4 (length prefix) + encoded bytes + 1 (null terminator)
            return new EncodedField { DataBytes = encoded, TotalSize = 4 + encoded.Length + 1 };
        }

        // ---------------------------------------------------------------------------
        // Writes a single AUINT8 field to the buffer at the current offset.
        //
        // Non-empty fields are written as:
        //   [4 bytes] uint32 LE length = data byte count + 1 (includes null)
        //   [N bytes] the encoded data bytes
        //   [1 byte]  0x00 null terminator
        //
        // Empty fields are written as:
        //   [4 bytes] uint32 LE length = 0 (no data follows)
        //
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 — "The length of each
        //   element is variable. If the element is empty, the element count is
        //   0x00000000 and no element exists."
        // ---------------------------------------------------------------------------
        private static void WriteAuint8Field(byte[] buffer, ref int offset, EncodedField field) {
            if (field.DataBytes == null || field.DataBytes.Length == 0) {
                // Empty field: write a zero-length prefix only
                WriteUInt32LE(buffer, ref offset, 0);
            } else {
                // Non-empty field: length includes the data bytes + null terminator
                uint dataLengthWithNull = (uint)(field.DataBytes.Length + 1);
                // Write the length prefix (4 bytes, little-endian)
                WriteUInt32LE(buffer, ref offset, dataLengthWithNull);
                // Copy the encoded data bytes into the buffer
                Array.Copy(field.DataBytes, 0, buffer, offset, field.DataBytes.Length);
                offset += field.DataBytes.Length;
                // Write the null terminator (buffer is zeroed, but be explicit)
                buffer[offset++] = 0x00;
            }
        }

        // ---------------------------------------------------------------------------
        // Builds the IPTCPresetDataList byte array containing exactly one
        // XMP-type IPTCPresetDataSet.
        //
        // Layout (all little-endian):
        //   [4 bytes] NumElement = 1
        //   [variable] IPTCPresetDataSet {
        //     [2 bytes] DatasetVersion (0x00C8)
        //     [1 byte]  IPTC Type (2 = XMP)
        //     [5 bytes] Reserved (zeroed)
        //     [4 bytes] Profile string byte count
        //     [N bytes] Profile ASCII string + null terminator
        //     [4 bytes] XMP data byte count
        //     [M bytes] XMP UTF-8 string + null terminator
        //   }
        //
        // Reference: Nikon SDK MAID3 Type0031 §3.260.1 and §3.260.2
        // ---------------------------------------------------------------------------
        private static byte[] BuildXmpIptcPresetDataList(string profileName, string xmpPayload, ushort DatasetVersion) {
            // -----------------------------------------------------------------------
            // Encode the profile name as ASCII with a null terminator.
            // The SDK limits profile names to 18 ASCII characters + 1 null byte.
            // We truncate to 18 characters if the caller provides a longer name.
            // -----------------------------------------------------------------------
            string truncatedProfile = profileName.Length > 18
                ? profileName.Substring(0, 18)
                : profileName;
            // Get ASCII bytes and append a null terminator byte
            byte[] profileAscii = Encoding.ASCII.GetBytes(truncatedProfile);
            // Total profile field size: ASCII bytes + 1 for the null terminator
            int profileDataSize = profileAscii.Length + 1;

            // -----------------------------------------------------------------------
            // Encode the XMP payload as UTF-8 with a null terminator.
            // The SDK limits XMP data to 30 KB (30,720 bytes). We validate the
            // size and throw if it exceeds the limit.
            // -----------------------------------------------------------------------
            byte[] xmpUtf8 = Encoding.UTF8.GetBytes(xmpPayload);
            // Total XMP field size: UTF-8 bytes + 1 for the null terminator
            int xmpDataSize = xmpUtf8.Length + 1;

            // Validate XMP size against the SDK's maximum
            if (xmpDataSize > XmpBuilder.MaxXmpDataBytes) {
                throw new ArgumentException(
                    $"XMP payload ({xmpDataSize} bytes) exceeds the Nikon SDK maximum " +
                    $"of {XmpBuilder.MaxXmpDataBytes} bytes.", nameof(xmpPayload));
            }

            // -----------------------------------------------------------------------
            // Calculate the actual content size for validation purposes, then
            // allocate the FULL maximum buffer size (30759 bytes) required by
            // the Nikon SDK firmware. The camera validates ulSize against the
            // expected maximum for presets 11-13 and returns
            // kNkMAIDResult_ValueOutOfBounds if the size does not match.
            //
            //   Content layout:
            //     4 bytes              → NumElement (uint32)
            //     2 bytes              → DatasetVersion (uint16)
            //     1 byte               → IPTC Type
            //     5 bytes              → Reserved
            //     4 bytes              → Profile AUINT8 length prefix
            //     profileDataSize      → Profile ASCII data + null
            //     4 bytes              → XMP AUINT8 length prefix
            //     xmpDataSize          → XMP UTF-8 data + null
            //
            // The remainder of the buffer beyond the content is zero-filled
            // (the default for new byte[] in C#), which the firmware ignores.
            //
            // Reference: Nikon SDK MAID3 Type0031 §3.260 — "When the number
            //   11-13 is specified in the ulPresetNo: 4byte + (the max size
            //   of IPTCPresetDataSet [30755byte] × the number of entry [1])"
            // -----------------------------------------------------------------------
            int contentSize = 4 + 2 + 1 + 5 + 4 + profileDataSize + 4 + xmpDataSize;
            // Allocate the full maximum buffer; all bytes initialized to zero by C#
            byte[] buffer = new byte[MaxXmpPresetDataListSize];
            // Track the current write position in the buffer
            int offset = 0;

            // -----------------------------------------------------------------------
            // Write NumElement = 1 (little-endian uint32).
            // We are writing exactly one IPTCPresetDataSet for the Set operation.
            // Reference: Nikon SDK MAID3 Type0031 §3.260.1 "NumElement"
            // -----------------------------------------------------------------------
            WriteUInt32LE(buffer, ref offset, 1);

            // -----------------------------------------------------------------------
            // Write DatasetVersion = 0x00C8 (little-endian uint16, version 2.00).
            // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "DatasetVersion"
            // -----------------------------------------------------------------------
            WriteUInt16LE(buffer, ref offset, DatasetVersion);

            // -----------------------------------------------------------------------
            // Write IPTC Type = 2 (XMP-IPTC).
            // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "IPTC Type"
            // -----------------------------------------------------------------------
            buffer[offset++] = IptcTypeXmp;

            // -----------------------------------------------------------------------
            // Write 5 reserved bytes (already zeroed by array initialization).
            // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Reserved"
            // -----------------------------------------------------------------------
            offset += ReservedByteCount;

            // -----------------------------------------------------------------------
            // Write the Profile AUINT8 field: 4-byte length + ASCII data + null.
            // The length prefix contains the total byte count of the data that follows.
            // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Profile" AUINT8 format
            // -----------------------------------------------------------------------
            WriteUInt32LE(buffer, ref offset, (uint)profileDataSize);
            // Copy the ASCII profile name bytes
            Array.Copy(profileAscii, 0, buffer, offset, profileAscii.Length);
            offset += profileAscii.Length;
            // Write the null terminator (buffer is already zeroed, but be explicit)
            buffer[offset++] = 0x00;

            // -----------------------------------------------------------------------
            // Write the XMP Data AUINT8 field: 4-byte length + UTF-8 data + null.
            // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "XMP Data" AUINT8 format
            // -----------------------------------------------------------------------
            WriteUInt32LE(buffer, ref offset, (uint)xmpDataSize);
            // Copy the UTF-8 XMP payload bytes
            Array.Copy(xmpUtf8, 0, buffer, offset, xmpUtf8.Length);
            offset += xmpUtf8.Length;
            // Write the null terminator
            buffer[offset++] = 0x00;

            // -----------------------------------------------------------------------
            // Verify we wrote exactly the expected content bytes. The buffer
            // is larger (MaxXmpPresetDataListSize = 30759), but the remaining
            // bytes after the content are zero-padded by the array initializer,
            // which the camera firmware ignores during parsing.
            // -----------------------------------------------------------------------
            if (offset != contentSize) {
                throw new InvalidOperationException(
                    $"Buffer content mismatch: expected {contentSize} bytes but wrote {offset}.");
            }

            // Log the content size vs total buffer size for diagnostic purposes
            Logger.Debug($"XMP-IPTC content: {contentSize} bytes in {MaxXmpPresetDataListSize}-byte buffer");

            return buffer;
        }

        // ---------------------------------------------------------------------------
        // Helper: writes a 32-bit unsigned integer in little-endian byte order
        // to the buffer at the current offset, then advances the offset by 4.
        // Little-endian is the byte order used by the Nikon SDK on x86/x64.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.1 "Each field data is
        //            stored in the little endian format."
        // ---------------------------------------------------------------------------
        private static void WriteUInt32LE(byte[] buffer, ref int offset, uint value) {
            // Byte 0: least significant byte
            buffer[offset++] = (byte)(value & 0xFF);
            // Byte 1
            buffer[offset++] = (byte)((value >> 8) & 0xFF);
            // Byte 2
            buffer[offset++] = (byte)((value >> 16) & 0xFF);
            // Byte 3: most significant byte
            buffer[offset++] = (byte)((value >> 24) & 0xFF);
        }

        // ---------------------------------------------------------------------------
        // Helper: writes a 16-bit unsigned integer in little-endian byte order
        // to the buffer at the current offset, then advances the offset by 2.
        // ---------------------------------------------------------------------------
        private static void WriteUInt16LE(byte[] buffer, ref int offset, ushort value) {
            // Byte 0: least significant byte
            buffer[offset++] = (byte)(value & 0xFF);
            // Byte 1: most significant byte
            buffer[offset++] = (byte)((value >> 8) & 0xFF);
        }

        // ---------------------------------------------------------------------------
        // Activates the specified IPTC preset slot for auto-embedding during
        // shooting. After writing data to a preset slot, the camera needs to be
        // told which preset to auto-embed via kNkMAIDCapability_IPTCPresetSelect.
        //
        // Note: Only one preset can be auto-embedded at a time. If the user wants
        //       GPS, astrometric, and weather data all embedded, they would need to
        //       combine the XMP into a single slot or cycle through presets.
        //
        // Reference: Nikon SDK MAID3 Type0031 §3.263 "IPTCPresetSelect"
        //   0 = Not attached, 1-13 = Auto-embed the specified preset number
        // ---------------------------------------------------------------------------
        public static void ActivatePresetForAutoEmbed(NikonDevice camera, uint presetNumber) {
            // Guard: camera must not be null
            if (camera == null) {
                throw new ArgumentNullException(nameof(camera), "NikonDevice must not be null.");
            }

            // Guard: preset number must be valid (0 to disable, 1-13 to select)
            if (presetNumber > 13) {
                throw new ArgumentOutOfRangeException(nameof(presetNumber),
                    "Preset number must be 0 (off) or 1-13.");
            }

            // -----------------------------------------------------------------------
            // Check that the camera supports the IPTCPresetSelect capability.
            // Reference: nikoncswrapper NikonNativeEnums.cs
            //   kNkMAIDCapability_IPTCPresetSelect = 33982
            // -----------------------------------------------------------------------
            if (!camera.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetSelect)) {
                Logger.Warning("Camera does not support kNkMAIDCapability_IPTCPresetSelect.");
                return;
            }

            // -----------------------------------------------------------------------
            // Set the IPTCPresetSelect value. This is a simple unsigned integer
            // capability (ulType = kNkMAIDCapType_Unsigned), so we use SetUnsigned.
            // Reference: Nikon SDK MAID3 Type0031 §3.263 and nikoncswrapper Nikon.cs
            // -----------------------------------------------------------------------
            Logger.Info($"Activating IPTC preset {presetNumber} for auto-embed during shooting.");
            camera.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetSelect, presetNumber);
            Logger.Info($"IPTC preset {presetNumber} is now active for auto-embed.");
        }
    }
}