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

namespace NikonCameraSettings.Utils {
    /*
     * This class definition is also derived from Christian Palm's work with regards to the abstract
     * structure of the class in terms of the class members and types of methods to include.
     */

    public class CamInfo {
        public const uint VIBRATION_REDUCTION = 34053;
        public const uint CHANGE_MONITOR_OFF_STATUS = 34080;

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
    }
}