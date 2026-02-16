#region "copyright"

/*
    Copyright © 2026 William Buchanan (william@williambuchanan.net)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Nikon;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyCamera;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NikonCameraSettings.Utils {
    /*
     * This class definition is also derived from Christian Palm's work with regards to the abstract
     * structure of the class in terms of the class members and types of methods to include.
     */

    public class CamInfo {
        public const uint VIBRATION_REDUCTION = 34053;
        public const uint CHANGE_MONITOR_OFF_STATUS = 34080;
        public const uint PIXEL_MAPPING = 34097;
        public const uint FOCUS_SHIFT = 34104;
        public const uint FOCUS_SHIFT_INFO = 34108;
        public const uint DIFFRACTION_COMPENSATION = 34052;

        public static Dictionary<uint, string> CameraTypetoFirmware = new Dictionary<uint, string> {
            { 0x57, "Z 9" },
            { 0x59, "Z 8" },
            { 0x5A, "Z 9_FU1" },
            { 0x5B, "Z 9_FU2" },
            { 0x5C, "Z 9_FU3" },
            { 0x5D, "Z 9_FU4" },
            { 0x5E, "Z 8_FU1" },
        };

        private static Dictionary<string, int> _monitorSettings = new() {
            { "On", 0 },
            { "Off", 1 }
        };

        // Stores the three possible media states available
        private static Dictionary<string, uint> _storageLocationOptions = new Dictionary<string, uint>() {
            { "Card", (uint)eNkMAIDSaveMedia.kNkMAIDSaveMedia_Card },
            { "SDRAM", (uint)eNkMAIDSaveMedia.kNkMAIDSaveMedia_SDRAM },
            { "Card+SDRAM", (uint)eNkMAIDSaveMedia.kNkMAIDSaveMedia_Card_SDRAM }
        };

        private static Dictionary<string, int> _vrSettings = new Dictionary<string, int>() {
            { "Off", 0 },
            { "On", 1 },
            { "Normal", 1 },
            { "Sport", 2 }
        };

        private static NikonDevice camera;

        // Mirrors the way the superclass is initialized so this class will have the same member variables defined.
        public CamInfo(NikonDevice camera) {
            camera = camera;
            Logger.Debug($"Initialized CamInfo for a camera with firmware version: {GetCameraFirmware(camera)}");
        }

        public static string GetCameraFirmware(NikonDevice camera) {
            uint camtype = camera.GetUnsigned(eNkMAIDCapability.kNkMAIDCapability_CameraType);
            return CameraTypetoFirmware.TryGetValue(camtype, out string firmware) ? firmware : "Unknown/Unmapped Firmware Version";
        }


        // This should be used to populate the dropdown for image stabilization settings in the UI
        // in addition to being used to set the image stabilization setting on the camera.
        public static List<string> GetVRSettings() {
            return _vrSettings.Keys.ToList();
        }

        public static List<string> GetMonitorSettings() {
            return _monitorSettings.Keys.ToList();
        }

        public static List<string> GetStorageLocationSettings() {
            return _storageLocationOptions.Keys.ToList();
        }

        // Sets the image stabilization setting in the camera.
        public void SetImageStabilization(NikonDevice camera, string vrSetting) {
            if (camera.SupportsCapability((eNkMAIDCapability)VIBRATION_REDUCTION)) {
                var e = camera.GetEnum((eNkMAIDCapability)VIBRATION_REDUCTION);
                e.Index = _vrSettings[vrSetting];
                camera.SetEnum((eNkMAIDCapability)VIBRATION_REDUCTION, e);
                Logger.Debug($"Set image stabilization to {vrSetting}");
            } else {
                // Handle exceptions as needed, e.g., log the error or show a message to the user.
                Logger.Error($"Error setting image stabilization.  The SDK does not expose this setting for the current camera.");
            }
        }

        public void SetMonitorOnOff(NikonDevice camera, string monitorStatus) {
            if (camera.SupportsCapability((eNkMAIDCapability)CHANGE_MONITOR_OFF_STATUS)) {
                var e = camera.GetEnum((eNkMAIDCapability)CHANGE_MONITOR_OFF_STATUS);
                e.Index = _monitorSettings[monitorStatus];
                camera.SetEnum((eNkMAIDCapability)CHANGE_MONITOR_OFF_STATUS, e);
                Logger.Debug($"Set monitor status to {monitorStatus}");
            } else {
                // Handle exceptions as needed, e.g., log the error or show a message to the user.
                Logger.Error($"The SDK for this camera does not expose the change monitor off status setting.");
            }
        }

        public void SetStorageLocation(NikonDevice camera, string storageLocation) {
            if (camera.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_SaveMedia)) {
                camera.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_SaveMedia, _storageLocationOptions[storageLocation]);
                Logger.Debug($"Set media state to {storageLocation}");
            } else {
                // Handle exceptions as needed, e.g., log the error or show a message to the user.
                Logger.Error($"The SDK for this camera does not expose the StorageMedia settings.");
            }
        }

        public void StartPixelMapping(NikonDevice camera) {
            if (camera.SupportsCapability((eNkMAIDCapability)PIXEL_MAPPING)) {
                Logger.Debug("Started pixel mapping.");
                camera.Start((eNkMAIDCapability)PIXEL_MAPPING);
                Logger.Debug("Completed pixel mapping.");
            } else {
                // Handle exceptions as needed, e.g., log the error or show a message to the user.
                Logger.Error($"The SDK for this camera does not expose the Pixel Mapping settings.");
            }
        }

        // To try avoiding DeviceBusy status errors across the board.  
        // Should also figure out how to make the maxWaitSeconds parameter user-configurable in the UI at some point since 
        // 5 minutes is probably only needed if someone is taking an exceptionally long focus shift stack or trying to use 
        // focus shift stacking in low light situations where the exposure time will be very long.
        public static async Task<bool> WaitForDeviceReady(NikonDevice device, CancellationToken token,
                                                          int maxWaitSeconds = 600, int pollIntervalMs = 500) {

            if (device == null) {
                Logger.Warning("WaitForDeviceReady: NikonDevice is null.");
                return false;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            TimeSpan timeout = TimeSpan.FromSeconds(maxWaitSeconds);
            int retryCount = 0;

            while (!token.IsCancellationRequested) {
                try {
                    device.GetInteger(eNkMAIDCapability.kNkMAIDCapability_BatteryLevel);
                    if (retryCount > 0) {
                        Logger.Info($"WaitForDeviceReady: Camera ready after " + $"{retryCount} retries ({stopwatch.ElapsedMilliseconds}ms).");
                    }
                    return true;

                } catch (NikonException ex)
                    when (ex.ErrorCode == eNkMAIDResult.kNkMAIDResult_DeviceBusy) {
                    retryCount++;

                    if (stopwatch.Elapsed >= timeout) {
                        Logger.Warning($"WaitForDeviceReady: Timeout after " + $"{maxWaitSeconds}s ({retryCount} retries). Camera is still busy.");
                        return false;
                    }
                    if (retryCount % 10 == 0) {
                        Logger.Debug($"WaitForDeviceReady: Still busy after " + $"{retryCount} retries " +
                                     $"({stopwatch.ElapsedMilliseconds}ms elapsed).");
                    }

                } catch (NullReferenceException) {
                    Logger.Warning("WaitForDeviceReady: NullReferenceException from SDK worker thread (stored async exception). Proceeding — camera may be ready.");
                    return true;

                } catch (NikonException ex) {
                    Logger.Debug($"WaitForDeviceReady: SDK responded with " + $"{ex.ErrorCode} — camera is not busy, proceeding.");
                    return true;
                }
                try {
                    await Task.Delay(pollIntervalMs, token);
                } catch (TaskCanceledException) {
                    Logger.Debug("WaitForDeviceReady: Cancelled by user.");
                    return false;
                }
            }
            return false;
        }
    }
}