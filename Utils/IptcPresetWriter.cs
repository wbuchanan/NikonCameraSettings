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
        private const byte IptcTypeXmp = 2;
        private const byte IptcTypeNms = 1;
        public const uint NmsPresetSlotMin = 1;
        public const uint NmsPresetSlotMax = 10;
        private const int ReservedByteCount = 5;
        public const uint XmpPresetSlotMin = 11;
        public const uint XmpPresetSlotMax = 13;
        public const uint DefaultXmpPresetSlot = 11;
        private const int MaxXmpPresetDataSetSize = 30755;
        private const int MaxXmpPresetDataListSize = 4 + MaxXmpPresetDataSetSize;
        private const int MaxNmsPresetDataSetSizeV200 = 4984;
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

        public static void WriteXmpIptcPreset(NikonDevice camera, uint presetNumber,
            string profileName, string xmpPayload) {
            if (camera == null) {
                throw new ArgumentNullException(nameof(camera), "NikonDevice must not be null.");
            }

            if (presetNumber < XmpPresetSlotMin || presetNumber > XmpPresetSlotMax) {
                throw new ArgumentOutOfRangeException(nameof(presetNumber),
                    $"XMP-IPTC preset number must be between {XmpPresetSlotMin} and {XmpPresetSlotMax}.");
            }

            if (string.IsNullOrEmpty(xmpPayload)) {
                throw new ArgumentException("XMP payload must not be null or empty.", nameof(xmpPayload));
            }

            if (!camera.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetInfo)) {
                throw new InvalidOperationException(
                    "The connected camera does not support kNkMAIDCapability_IPTCPresetInfo. " +
                    "This capability is available on cameras like the Nikon Z 8.");
            }

            ushort DatasetVersion = GetDatasetVersion(camera);
            byte[] dataListBytes = BuildXmpIptcPresetDataList(profileName, xmpPayload, DatasetVersion);
            Logger.Debug($"IPTC preset data list size: {dataListBytes.Length} bytes for slot {presetNumber}");

            IntPtr pDataUnmanaged = IntPtr.Zero;
            IntPtr pStructUnmanaged = IntPtr.Zero;

            try {
                pDataUnmanaged = Marshal.AllocHGlobal(dataListBytes.Length);
                Marshal.Copy(dataListBytes, 0, pDataUnmanaged, dataListBytes.Length);

                NkMAIDIPTCPresetInfo presetInfo = new NkMAIDIPTCPresetInfo();
                presetInfo.ulPresetNo = presetNumber;
                presetInfo.ulSize = (uint)dataListBytes.Length;
                presetInfo.pData = pDataUnmanaged;
                int structSize = Marshal.SizeOf<NkMAIDIPTCPresetInfo>();
                pStructUnmanaged = Marshal.AllocHGlobal(structSize);
                Marshal.StructureToPtr(presetInfo, pStructUnmanaged, false);

                Logger.Info($"Writing XMP-IPTC data to camera preset slot {presetNumber}...");
                camera.SetGeneric(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetInfo, pStructUnmanaged);
                Logger.Info($"Successfully wrote XMP-IPTC data to preset slot {presetNumber}.");
            } catch (NikonException nex) {
                Logger.Error($"Nikon SDK error writing IPTC preset {presetNumber}: " +
                             $"ErrorCode={nex.ErrorCode}, Message={nex.Message}");
                throw;
            } catch (Exception ex) {
                Logger.Error($"Unexpected error writing IPTC preset {presetNumber}: {ex.Message}");
                throw;
            } finally {
                if (pStructUnmanaged != IntPtr.Zero) {
                    Marshal.FreeHGlobal(pStructUnmanaged);
                }
                if (pDataUnmanaged != IntPtr.Zero) {
                    Marshal.FreeHGlobal(pDataUnmanaged);
                }
            }
        }

        public static void WriteNmsIptcPreset(NikonDevice camera, uint presetNumber,
            string profileName, NmsIptcData data) {
            if (camera == null) {
                throw new ArgumentNullException(nameof(camera), "NikonDevice must not be null.");
            }
            if (presetNumber < NmsPresetSlotMin || presetNumber > NmsPresetSlotMax) {
                throw new ArgumentOutOfRangeException(nameof(presetNumber),
                    $"NMS-IPTC preset number must be {NmsPresetSlotMin}-{NmsPresetSlotMax}.");
            }
            if (data == null) {
                throw new ArgumentNullException(nameof(data), "NmsIptcData must not be null.");
            }
            if (!camera.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetInfo)) {
                throw new InvalidOperationException(
                    "The connected camera does not support kNkMAIDCapability_IPTCPresetInfo. " +
                    "This capability is available on cameras like the Nikon Z 8.");
            }

            byte[] dataListBytes = BuildNmsIptcPresetDataList(profileName, data, GetDatasetVersion(camera));
            Logger.Debug($"NMS-IPTC preset data list size: {dataListBytes.Length} bytes for slot {presetNumber}");
            IntPtr pDataUnmanaged = IntPtr.Zero;
            IntPtr pStructUnmanaged = IntPtr.Zero;

            try {
                pDataUnmanaged = Marshal.AllocHGlobal(dataListBytes.Length);
                Marshal.Copy(dataListBytes, 0, pDataUnmanaged, dataListBytes.Length);
                NkMAIDIPTCPresetInfo presetInfo = new NkMAIDIPTCPresetInfo();
                presetInfo.ulPresetNo = presetNumber;
                presetInfo.ulSize = (uint)dataListBytes.Length;
                presetInfo.pData = pDataUnmanaged;
                int structSize = Marshal.SizeOf<NkMAIDIPTCPresetInfo>();
                pStructUnmanaged = Marshal.AllocHGlobal(structSize);
                Marshal.StructureToPtr(presetInfo, pStructUnmanaged, false);
                Logger.Info($"Writing NMS-IPTC data to camera preset slot {presetNumber}...");
                camera.SetGeneric(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetInfo, pStructUnmanaged);
                Logger.Info($"Successfully wrote NMS-IPTC data to preset slot {presetNumber}.");
            } catch (NikonException nex) {
                Logger.Error($"Nikon SDK error writing NMS-IPTC preset {presetNumber}: " +
                             $"ErrorCode={nex.ErrorCode}, Message={nex.Message}");
                throw;
            } catch (Exception ex) {
                Logger.Error($"Unexpected error writing NMS-IPTC preset {presetNumber}: {ex.Message}");
                throw;
            } finally {
                if (pStructUnmanaged != IntPtr.Zero) {
                    Marshal.FreeHGlobal(pStructUnmanaged);
                }
                if (pDataUnmanaged != IntPtr.Zero) {
                    Marshal.FreeHGlobal(pDataUnmanaged);
                }
            }
        }

        private static byte[] BuildNmsIptcPresetDataList(string profileName, NmsIptcData data, ushort DatasetVersion) {
            string truncatedProfile = (profileName ?? "").Length > 18
                ? profileName.Substring(0, 18)
                : (profileName ?? "");

            EncodedField profileField = EncodeAsciiField(truncatedProfile);
            EncodedField captionField = EncodeUtf8Field(data.Caption);
            EncodedField altTextField = EncodeUtf8Field(data.AltText);
            EncodedField cityField = EncodeUtf8Field(data.City);
            EncodedField copyrightField = EncodeUtf8Field(data.Copyright);
            EncodedField countryField = EncodeUtf8Field(data.Country);
            EncodedField countryCodeField = EncodeUtf8Field(data.CountryCode);
            EncodedField objectNameField = EncodeUtf8Field(data.ObjectName);
            EncodedField creatorField = EncodeUtf8Field(data.Creator);
            EncodedField eventNameField = EncodeUtf8Field(data.EventName);
            EncodedField headlineField = EncodeUtf8Field(data.Headline);
            EncodedField stateField = EncodeUtf8Field(data.State);
            EncodedField jobTitleField = EncodeUtf8Field(data.JobTitle);
            EncodedField captionWriterField = EncodeUtf8Field(data.CaptionWriter);
            EncodedField creditLineField = EncodeUtf8Field(data.CreditLine);
            EncodedField sourceField = EncodeUtf8Field(data.Source);

            int contentSize = 4 + 2 + 1 + 5
                + profileField.TotalSize
                + captionField.TotalSize
                + altTextField.TotalSize
                + cityField.TotalSize
                + copyrightField.TotalSize
                + countryField.TotalSize    
                + countryCodeField.TotalSize
                + objectNameField.TotalSize
                + creatorField.TotalSize
                + eventNameField.TotalSize
                + headlineField.TotalSize
                + stateField.TotalSize
                + jobTitleField.TotalSize
                + captionWriterField.TotalSize
                + creditLineField.TotalSize
                + sourceField.TotalSize;

            // Allocate the full maximum buffer; all bytes initialized to zero by C#
            byte[] buffer = new byte[GetMaxNmsPresetDataSetSize(DatasetVersion)];
            // Track the current write position in the buffer
            int offset = 0;

            WriteUInt32LE(buffer, ref offset, 1);

            WriteUInt16LE(buffer, ref offset, DatasetVersion);

            buffer[offset++] = IptcTypeNms;

            offset += ReservedByteCount;

            WriteAuint8Field(buffer, ref offset, profileField);
            WriteAuint8Field(buffer, ref offset, captionField);
            WriteAuint8Field(buffer, ref offset, eventNameField);
            WriteAuint8Field(buffer, ref offset, headlineField);
            WriteAuint8Field(buffer, ref offset, objectNameField);
            WriteAuint8Field(buffer, ref offset, cityField);
            WriteAuint8Field(buffer, ref offset, stateField);
            WriteAuint8Field(buffer, ref offset, countryField);
            WriteAuint8Field(buffer, ref offset, jobTitleField);
            WriteAuint8Field(buffer, ref offset, captionWriterField);
            WriteAuint8Field(buffer, ref offset, creditLineField);
            WriteAuint8Field(buffer, ref offset, sourceField);

            if (offset != contentSize) {
                throw new InvalidOperationException(
                    $"NMS-IPTC buffer content mismatch: expected {contentSize} bytes but wrote {offset}.");
            }
            Logger.Debug($"NMS-IPTC content: {contentSize} bytes in {GetMaxNmsPresetDataSetSize(DatasetVersion)}-byte buffer");

            return buffer;
        }

        private struct EncodedField {
            public byte[] DataBytes;
            public int TotalSize;
        }

        private static EncodedField EncodeUtf8Field(string value) {
            if (string.IsNullOrEmpty(value)) {
                return new EncodedField { DataBytes = null, TotalSize = 4 };
            }
            byte[] encoded = Encoding.UTF8.GetBytes(value);
            return new EncodedField { DataBytes = encoded, TotalSize = 4 + encoded.Length + 1 };
        }

        private static EncodedField EncodeAsciiField(string value) {
            if (string.IsNullOrEmpty(value)) {
                return new EncodedField { DataBytes = null, TotalSize = 4 };
            }
            byte[] encoded = Encoding.ASCII.GetBytes(value);
            return new EncodedField { DataBytes = encoded, TotalSize = 4 + encoded.Length + 1 };
        }

        private static void WriteAuint8Field(byte[] buffer, ref int offset, EncodedField field) {
            if (field.DataBytes == null || field.DataBytes.Length == 0) {
                WriteUInt32LE(buffer, ref offset, 0);
            } else {
                uint dataLengthWithNull = (uint)(field.DataBytes.Length + 1);
                WriteUInt32LE(buffer, ref offset, dataLengthWithNull);
                Array.Copy(field.DataBytes, 0, buffer, offset, field.DataBytes.Length);
                offset += field.DataBytes.Length;
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

        private static void WriteUInt32LE(byte[] buffer, ref int offset, uint value) {
            buffer[offset++] = (byte)(value & 0xFF);
            buffer[offset++] = (byte)((value >> 8) & 0xFF);
            buffer[offset++] = (byte)((value >> 16) & 0xFF);
            buffer[offset++] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteUInt16LE(byte[] buffer, ref int offset, ushort value) {
            buffer[offset++] = (byte)(value & 0xFF);
            buffer[offset++] = (byte)((value >> 8) & 0xFF);
        }

        public static void ActivatePresetForAutoEmbed(NikonDevice camera, uint presetNumber) {
            if (camera == null) {
                throw new ArgumentNullException(nameof(camera), "NikonDevice must not be null.");
            }
            if (presetNumber > 13) {
                throw new ArgumentOutOfRangeException(nameof(presetNumber),
                    "Preset number must be 0 (off) or 1-13.");
            }

            if (!camera.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetSelect)) {
                Logger.Warning("Camera does not support kNkMAIDCapability_IPTCPresetSelect.");
                return;
            }

            Logger.Info($"Activating IPTC preset {presetNumber} for auto-embed during shooting.");
            camera.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetSelect, presetNumber);
            Logger.Info($"IPTC preset {presetNumber} is now active for auto-embed.");
        }
    }
}