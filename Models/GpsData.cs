// *****************************************************************************
// File: Models/GpsData.cs
// Purpose: Encapsulates GPS location data (latitude, longitude, elevation)
//          sourced from NINA's profile and provides coordinate format
//          conversions for XMP-IPTC embedding into Nikon camera IPTC presets.
//
// References:
//   - NINA IProfileService: https://github.com/isbeorn/nina.plugin.template
//   - EXIF GPS coordinate format (CIPA DC-008): degrees,minutes.fractionalN/S
//   - XMP specification Part 2 (ISO 16684-2): GPSCoordinate type
//   - Nikon SDK MAID3 Type0031 §3.260: IPTCPresetInfo / XMP-IPTC data format
// *****************************************************************************

#region "copyright"

/*
    Copyright © 2026 William Buchanan (william@williambuchanan.net)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Globalization;

namespace NikonCameraSettings.Models {

    /// <summary>
    /// Immutable data model representing a GPS location. Values are captured
    /// from NINA's IProfileService.ActiveProfile.AstrometrySettings at a
    /// specific point in time, enabling before/after comparison for change
    /// detection. The class also provides helpers that convert decimal-degree
    /// coordinates into the EXIF/XMP "DD,MM.mmD" and "DD°MM'SS.s"D" formats
    /// required by the IPTC preset's XMP payload.
    /// </summary>
    public sealed class GpsData : IEquatable<GpsData> {

        // ---------------------------------------------------------------------------
        // Store latitude in decimal degrees; north is positive, south is negative.
        // NINA's AstrometrySettings.Latitude uses this same convention.
        // Reference: https://nighttime-imaging.eu/docs/master/site/tabs/options/general/
        // ---------------------------------------------------------------------------
        public double Latitude { get; }

        // ---------------------------------------------------------------------------
        // Store longitude in decimal degrees; east is positive, west is negative.
        // NINA's AstrometrySettings.Longitude uses this same convention.
        // ---------------------------------------------------------------------------
        public double Longitude { get; }

        // ---------------------------------------------------------------------------
        // Store elevation in meters above (positive) or below (negative) sea level.
        // NINA's AstrometrySettings.Elevation uses meters as its unit.
        // ---------------------------------------------------------------------------
        public double Elevation { get; }

        // ---------------------------------------------------------------------------
        // Construct an immutable snapshot of the current GPS location values.
        // All three parameters mirror the types returned by NINA's profile.
        // ---------------------------------------------------------------------------
        public GpsData(double latitude, double longitude, double elevation) {
            Latitude = latitude;
            Longitude = longitude;
            Elevation = elevation;
        }

        // ---------------------------------------------------------------------------
        // The EXIF/XMP GPSLatitude tag requires the format "DD,MM.mmN" or
        // "DD,MM.mmS" where DD is integer degrees, MM.mm is decimal minutes,
        // and the trailing letter indicates the hemisphere.
        // Reference: CIPA DC-008 §4.6.6 (EXIF Tag GPSLatitude rational format)
        //            and XMP Part 2 §1.2.7.4 (GPSCoordinate type string format).
        // ---------------------------------------------------------------------------
        public string ToExifLatitude() {
            // Determine hemisphere suffix: 'N' for north, 'S' for south
            string hemisphere = Latitude >= 0 ? "N" : "S";
            // Work with the absolute value so degree/minute values are positive
            double absLat = Math.Abs(Latitude);
            // Extract integer degrees by truncating the decimal part
            int degrees = (int)absLat;
            // Convert the fractional degrees to decimal minutes (1° = 60')
            double minutes = (absLat - degrees) * 60.0;
            // Format per XMP GPSCoordinate: "DD,MM.mmD" (invariant culture for '.')
            return string.Format(CultureInfo.InvariantCulture, "{0},{1:F4}{2}", degrees, minutes, hemisphere);
        }

        // ---------------------------------------------------------------------------
        // The EXIF/XMP GPSLongitude tag requires the format "DDD,MM.mmE" or
        // "DDD,MM.mmW" where DDD is integer degrees (up to 180), MM.mm is
        // decimal minutes, and the trailing letter indicates east or west.
        // ---------------------------------------------------------------------------
        public string ToExifLongitude() {
            // Determine hemisphere suffix: 'E' for east, 'W' for west
            string hemisphere = Longitude >= 0 ? "E" : "W";
            // Work with the absolute value so degree/minute values are positive
            double absLon = Math.Abs(Longitude);
            // Extract integer degrees by truncating the decimal part
            int degrees = (int)absLon;
            // Convert the fractional degrees to decimal minutes
            double minutes = (absLon - degrees) * 60.0;
            // Format per XMP GPSCoordinate: "DDD,MM.mmD" (invariant culture)
            return string.Format(CultureInfo.InvariantCulture, "{0},{1:F4}{2}", degrees, minutes, hemisphere);
        }

        // ---------------------------------------------------------------------------
        // Convert latitude to the more human-readable DMS format: DD°MM'SS.s"N/S.
        // This is stored in the XMP description text for convenient display in
        // photo cataloging software, while the EXIF rational tags carry the
        // machine-readable format above.
        // ---------------------------------------------------------------------------
        public string ToLatitudeDms() {
            // Determine hemisphere suffix
            string hemisphere = Latitude >= 0 ? "N" : "S";
            // Work with the absolute value for positive degree/minute/second
            double absLat = Math.Abs(Latitude);
            // Extract integer degrees
            int degrees = (int)absLat;
            // Extract integer arcminutes from the remaining fraction
            double minFrac = (absLat - degrees) * 60.0;
            int minutes = (int)minFrac;
            // Extract arcseconds from the remaining fraction of arcminutes
            double seconds = (minFrac - minutes) * 60.0;
            // Format as DD°MM'SS.s"H (invariant culture for decimal separator)
            return string.Format(CultureInfo.InvariantCulture, "{0}°{1:D2}'{2:F1}\"{3}", degrees, minutes, seconds, hemisphere);
        }

        // ---------------------------------------------------------------------------
        // Convert longitude to the DMS format: DDD°MM'SS.s"E/W.
        // ---------------------------------------------------------------------------
        public string ToLongitudeDms() {
            // Determine hemisphere suffix
            string hemisphere = Longitude >= 0 ? "E" : "W";
            // Work with the absolute value
            double absLon = Math.Abs(Longitude);
            // Extract integer degrees
            int degrees = (int)absLon;
            // Extract integer arcminutes
            double minFrac = (absLon - degrees) * 60.0;
            int minutes = (int)minFrac;
            // Extract arcseconds
            double seconds = (minFrac - minutes) * 60.0;
            // Format as DDD°MM'SS.s"H
            return string.Format(CultureInfo.InvariantCulture, "{0}°{1:D2}'{2:F1}\"{3}", degrees, minutes, seconds, hemisphere);
        }

        // ---------------------------------------------------------------------------
        // The EXIF GPSAltitude tag stores elevation as a rational number.
        // We express it as "meters*10/10" to preserve one decimal place.
        // Reference: CIPA DC-008 §4.6.6 Tag 0x0006 GPSAltitude.
        // ---------------------------------------------------------------------------
        public string ToExifAltitude() {
            // Round to one decimal place, then convert to a rational
            long numerator = (long)Math.Round(Math.Abs(Elevation) * 10.0);
            // Use denominator of 10 to preserve one decimal digit of precision
            return string.Format(CultureInfo.InvariantCulture, "{0}/10", numerator);
        }

        // ---------------------------------------------------------------------------
        // The EXIF GPSAltitudeRef tag is 0 when the altitude is above sea level
        // and 1 when below sea level. Reference: CIPA DC-008 §4.6.6 Tag 0x0005.
        // ---------------------------------------------------------------------------
        public string ToExifAltitudeRef() {
            // Return "0" for above sea level, "1" for below
            return Elevation >= 0 ? "0" : "1";
        }

        // ---------------------------------------------------------------------------
        // Determines whether the GPS data contains valid, non-default values.
        // NINA initializes latitude/longitude to 0 when no location is configured,
        // which would correspond to Null Island (0°N, 0°E). While technically a
        // valid coordinate, it almost certainly indicates unset data.
        // ---------------------------------------------------------------------------
        public bool IsValid() {
            // Treat (0, 0) as unset because Null Island is not a real observing site
            bool isNullIsland = Math.Abs(Latitude) < 0.0001 && Math.Abs(Longitude) < 0.0001;
            // Also reject coordinates outside the valid geographic range
            bool latInRange = Latitude >= -90.0 && Latitude <= 90.0;
            // Longitude must be between -180 and +180
            bool lonInRange = Longitude >= -180.0 && Longitude <= 180.0;
            // Return true only when coordinates are in range and not Null Island
            return !isNullIsland && latInRange && lonInRange;
        }

        // ---------------------------------------------------------------------------
        // Two GpsData instances are equal when their three coordinate values
        // are within a small epsilon of each other. This is used for change
        // detection: if the current GPS snapshot equals the last-sent snapshot,
        // we skip the expensive SDK call to avoid redundant camera writes.
        // A tolerance of ~0.36 arcseconds (~11 meters) is used for lat/lon,
        // and 0.1 meters for elevation — well within GPS accuracy limits.
        // ---------------------------------------------------------------------------
        public bool Equals(GpsData other) {
            // Null guard: a null reference is never equal
            if (other is null) return false;
            // Use a tolerance of 0.0001° ≈ 0.36 arcseconds for lat/lon comparison
            const double coordTolerance = 0.0001;
            // Use a tolerance of 0.1 meters for elevation comparison
            const double elevTolerance = 0.1;
            // Compare each component within the specified tolerance
            return Math.Abs(Latitude - other.Latitude) < coordTolerance
                && Math.Abs(Longitude - other.Longitude) < coordTolerance
                && Math.Abs(Elevation - other.Elevation) < elevTolerance;
        }

        // ---------------------------------------------------------------------------
        // Override object.Equals to delegate to the typed IEquatable<GpsData>.
        // ---------------------------------------------------------------------------
        public override bool Equals(object obj) {
            return Equals(obj as GpsData);
        }

        // ---------------------------------------------------------------------------
        // Override GetHashCode consistently with Equals. We round the coordinates
        // to the same precision used in the equality check so that "equal" objects
        // always produce the same hash code.
        // ---------------------------------------------------------------------------
        public override int GetHashCode() {
            // Round latitude to 4 decimal places to match the equality tolerance
            int latHash = Math.Round(Latitude, 4).GetHashCode();
            // Round longitude to 4 decimal places
            int lonHash = Math.Round(Longitude, 4).GetHashCode();
            // Round elevation to 1 decimal place to match the elevation tolerance
            int eleHash = Math.Round(Elevation, 1).GetHashCode();
            // Combine hashes using a standard approach
            return (latHash * 397) ^ (lonHash * 23) ^ eleHash;
        }

        // ---------------------------------------------------------------------------
        // Provide a human-readable representation for logging and debugging.
        // ---------------------------------------------------------------------------
        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture,
                "GPS: {0}, {1}, Elev: {2:F1}m",
                ToLatitudeDms(), ToLongitudeDms(), Elevation);
        }
    }
}
