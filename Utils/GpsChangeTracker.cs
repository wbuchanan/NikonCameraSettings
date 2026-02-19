#region "copyright"

/*
    Copyright © 2026 William Buchanan (william@williambuchanan.net)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NikonCameraSettings.Models;
using NINA.Core.Utility;

namespace NikonCameraSettings.Utils {

    public sealed class GpsChangeTracker {

        private readonly object _lock = new object();
        private GpsData _lastSentGps;
        private static readonly GpsChangeTracker _instance = new GpsChangeTracker();
        private GpsChangeTracker() { }
        public static GpsChangeTracker Instance => _instance;

        public bool HasChanged(GpsData currentGps) {
            // Null input means no data to send, so no change to report
            if (currentGps == null) return false;

            lock (_lock) {
                // If nothing has been sent yet, a write is always needed
                if (_lastSentGps == null) return true;
                // Compare using GpsData.Equals which applies coordinate tolerance
                return !currentGps.Equals(_lastSentGps);
            }
        }

        public void RecordSuccessfulWrite(GpsData sentGps) {
            lock (_lock) {
                _lastSentGps = sentGps;
                Logger.Debug($"GPS change tracker updated: {sentGps}");
            }
        }

        public void Reset() {
            lock (_lock) {
                _lastSentGps = null;
                Logger.Debug("GPS change tracker has been reset.");
            }
        }

        public GpsData GetLastSentData() {
            lock (_lock) {
                return _lastSentGps;
            }
        }
    }
}
