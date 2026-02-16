// *****************************************************************************
// File: Utils/GpsChangeTracker.cs
// Purpose: Maintains the last successfully sent GPS data snapshot and provides
//          a thread-safe comparison to determine whether the current GPS values
//          from NINA's profile have changed enough to warrant re-sending to the
//          camera. This avoids redundant SDK calls that would waste time and
//          potentially disrupt camera operations.
//
// Design rationale:
//   The GPS location is read from NINA's IProfileService.ActiveProfile
//   .AstrometrySettings, which is typically configured once per session or
//   synced from a connected mount. Because changes are infrequent, we store
//   the last-sent snapshot and only invoke the expensive SetGeneric SDK call
//   when the coordinates have actually changed beyond the tolerance defined
//   in GpsData.Equals (approximately 11 meters / 0.36 arcseconds).
//
// References:
//   - NikonCameraSettings.Models.GpsData: equality semantics and tolerance
//   - NINA IProfileService: https://github.com/isbeorn/nina.plugin.template
// *****************************************************************************

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

    /// <summary>
    /// Thread-safe singleton that tracks the last GPS data successfully written
    /// to the camera's IPTC preset. The sequence item consults this tracker
    /// before each write to skip redundant SDK calls when the location has not
    /// changed. The tracker is reset when the camera disconnects.
    /// </summary>
    public sealed class GpsChangeTracker {

        // ---------------------------------------------------------------------------
        // Lock object for thread-safe access to the last-sent snapshot.
        // Multiple threads can read NINA's profile concurrently (e.g., the UI
        // thread and the sequencer thread), so we protect the shared state.
        // ---------------------------------------------------------------------------
        private readonly object _lock = new object();

        // ---------------------------------------------------------------------------
        // The most recently sent GPS data, or null if nothing has been sent yet.
        // This is compared to the current profile values each time the sequence
        // item executes to decide whether a write is needed.
        // ---------------------------------------------------------------------------
        private GpsData _lastSentGps;

        // ---------------------------------------------------------------------------
        // Singleton instance. A single tracker per plugin lifetime is sufficient
        // because only one Nikon camera can be connected to NINA at a time.
        // ---------------------------------------------------------------------------
        private static readonly GpsChangeTracker _instance = new GpsChangeTracker();

        // ---------------------------------------------------------------------------
        // Private constructor enforces the singleton pattern.
        // ---------------------------------------------------------------------------
        private GpsChangeTracker() { }

        // ---------------------------------------------------------------------------
        // Public accessor for the singleton instance.
        // ---------------------------------------------------------------------------
        public static GpsChangeTracker Instance => _instance;

        // ---------------------------------------------------------------------------
        // Checks whether the provided GPS data differs from the last value that
        // was successfully sent to the camera. Returns true if a write is needed.
        //
        // Cases where this returns true:
        //   1. No data has ever been sent (_lastSentGps is null)
        //   2. The new data differs from the last-sent data beyond the tolerance
        //
        // Cases where this returns false:
        //   1. The new data equals the last-sent data within tolerance
        //   2. The new data is null (caller should not send null data)
        // ---------------------------------------------------------------------------
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

        // ---------------------------------------------------------------------------
        // Records a successful write by updating the last-sent snapshot.
        // This should only be called after IptcPresetWriter.WriteXmpIptcPreset
        // returns without throwing an exception.
        // ---------------------------------------------------------------------------
        public void RecordSuccessfulWrite(GpsData sentGps) {
            lock (_lock) {
                // Store a reference to the immutable GpsData that was just sent
                _lastSentGps = sentGps;
                Logger.Debug($"GPS change tracker updated: {sentGps}");
            }
        }

        // ---------------------------------------------------------------------------
        // Resets the tracker to its initial state. This should be called when:
        //   - The camera is disconnected (the preset state is unknown after reconnect)
        //   - The user explicitly requests a forced resend
        //
        // After resetting, the next HasChanged() call will return true for any
        // valid GPS data, ensuring a fresh write to the camera.
        // ---------------------------------------------------------------------------
        public void Reset() {
            lock (_lock) {
                _lastSentGps = null;
                Logger.Debug("GPS change tracker has been reset.");
            }
        }

        // ---------------------------------------------------------------------------
        // Returns the last successfully sent GPS data for diagnostic purposes.
        // Returns null if no data has been sent since the last reset.
        // ---------------------------------------------------------------------------
        public GpsData GetLastSentData() {
            lock (_lock) {
                return _lastSentGps;
            }
        }
    }
}
