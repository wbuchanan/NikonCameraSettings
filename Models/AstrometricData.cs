// *****************************************************************************
// File: Models/AstrometricData.cs
// Purpose: Encapsulates astrometric (celestial coordinate) data captured from
//          NINA's telescope mediator at the time a sequence item executes.
//          Stores both equatorial coordinates (Right Ascension and Declination
//          in the J2000/ICRS reference frame) and horizontal coordinates
//          (Altitude and Azimuth for the observer's local reference frame).
//
// References:
//   - AVM 1.2 Standard: http://www.virtualastronomy.org/avm_metadata.php
//   - FITS Standard §4.4.2.1: OBJCTRA / OBJCTDEC keyword format
//   - NINA TelescopeInfo: https://github.com/isbeorn/nina
//   - IAU ICRS: https://www.iau.org/science/scientific_bodies/working_groups/icrs/
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
    /// Immutable data model representing astrometric data from a telescope mount.
    /// All equatorial coordinates are stored in J2000 (ICRS) epoch. Properties
    /// use double.NaN as a sentinel for "not available", matching how NINA's
    /// mediator system reports unavailable data from disconnected devices.
    /// </summary>
    public sealed class AstrometricData : IEquatable<AstrometricData> {
        // =====================================================================
        //  EQUATORIAL COORDINATES — J2000 / ICRS reference frame
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Right Ascension in hours (0.0 to 24.0), J2000 epoch.
        // Standard unit used by NINA's TelescopeInfo and ASCOM mounts.
        // Converted to degrees (RA * 15) for AVM Spatial.ReferenceValue.
        // Reference: IAU — RA measured eastward from the vernal equinox
        // ---------------------------------------------------------------------------
        public double RightAscensionHours { get; }

        // ---------------------------------------------------------------------------
        // Declination in degrees (-90.0 to +90.0), J2000 epoch.
        // Positive = north of celestial equator. Used directly in AVM.
        // ---------------------------------------------------------------------------
        public double DeclinationDegrees { get; }

        // =====================================================================
        //  HORIZONTAL COORDINATES — observer's local topocentric frame
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Altitude in degrees above the observer's horizon (0°=horizon, 90°=zenith).
        // Reference: Meeus, "Astronomical Algorithms" §13
        // ---------------------------------------------------------------------------
        public double AltitudeDegrees { get; }

        // ---------------------------------------------------------------------------
        // Azimuth in degrees clockwise from true North (N=0°, E=90°, S=180°, W=270°).
        // Reference: ASCOM ITelescopeV3.Azimuth
        // ---------------------------------------------------------------------------
        public double AzimuthDegrees { get; }

        // =====================================================================
        //  OBSERVATION CONTEXT
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Local Sidereal Time in hours (0.0 to 24.0). When LST equals a target's
        // RA, that target is at transit (highest point in the sky).
        // Reference: Meeus, "Astronomical Algorithms" §12
        // ---------------------------------------------------------------------------
        public double SiderealTimeHours { get; }

        // ---------------------------------------------------------------------------
        // Name of the celestial object being observed (e.g., "M31", "NGC 7000").
        // Sourced from the sequence's DeepSkyObjectContainer or user input.
        // ---------------------------------------------------------------------------
        public string ObjectName { get; }

        // =====================================================================
        //  COMPUTED PROPERTIES
        // =====================================================================

        // ---------------------------------------------------------------------------
        // RA converted to degrees (0.0 to 360.0) for AVM Spatial.ReferenceValue.
        // Conversion: 1 hour of RA = 15° of arc (360° / 24h).
        // Reference: AVM 1.2 §4.1 — Spatial.ReferenceValue format
        // ---------------------------------------------------------------------------
        public double RightAscensionDegrees =>
            double.IsNaN(RightAscensionHours) ? double.NaN : RightAscensionHours * 15.0;

        // =====================================================================
        //  CONSTRUCTOR
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Constructs an immutable snapshot. All numeric parameters default to NaN
        // ("not available"). The sequence item populates only the fields that
        // the connected telescope mediator provides.
        // ---------------------------------------------------------------------------
        public AstrometricData(
            double rightAscensionHours = double.NaN,
            double declinationDegrees = double.NaN,
            double altitudeDegrees = double.NaN,
            double azimuthDegrees = double.NaN,
            double siderealTimeHours = double.NaN,
            string objectName = null) {
            RightAscensionHours = rightAscensionHours;
            DeclinationDegrees = declinationDegrees;
            AltitudeDegrees = altitudeDegrees;
            AzimuthDegrees = azimuthDegrees;
            SiderealTimeHours = siderealTimeHours;
            // Normalize null to empty for consistent downstream handling
            ObjectName = objectName ?? string.Empty;
        }

        // =====================================================================
        //  VALIDATION
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Returns true if both RA and Dec are available and within valid ranges.
        // Minimum requirement for AVM Spatial metadata in the XMP payload.
        // ---------------------------------------------------------------------------
        public bool HasEquatorialCoordinates() {
            bool raValid = !double.IsNaN(RightAscensionHours)
                && RightAscensionHours >= 0.0 && RightAscensionHours < 24.0;
            bool decValid = !double.IsNaN(DeclinationDegrees)
                && DeclinationDegrees >= -90.0 && DeclinationDegrees <= 90.0;
            return raValid && decValid;
        }

        // ---------------------------------------------------------------------------
        // Returns true if both Altitude and Azimuth are available and valid.
        // ---------------------------------------------------------------------------
        public bool HasHorizontalCoordinates() {
            bool altValid = !double.IsNaN(AltitudeDegrees)
                && AltitudeDegrees >= -90.0 && AltitudeDegrees <= 90.0;
            bool azValid = !double.IsNaN(AzimuthDegrees)
                && AzimuthDegrees >= 0.0 && AzimuthDegrees < 360.0;
            return altValid && azValid;
        }

        // ---------------------------------------------------------------------------
        // Returns true if any meaningful astrometric data is present.
        // ---------------------------------------------------------------------------
        public bool HasAnyData() {
            return HasEquatorialCoordinates()
                || HasHorizontalCoordinates()
                || !string.IsNullOrWhiteSpace(ObjectName);
        }

        // =====================================================================
        //  FITS-FORMAT CONVERSION
        //  Formats per FITS standard keyword conventions used by PixInsight,
        //  Astro Pixel Processor, and other astronomical software.
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Formats RA as "HH MM SS.ss" for the FITS OBJCTRA keyword.
        // Reference: FITS Standard §4.4.2.1
        // ---------------------------------------------------------------------------
        public string ToFitsRightAscension() {
            if (double.IsNaN(RightAscensionHours)) return string.Empty;
            double absRa = Math.Abs(RightAscensionHours);
            int hours = (int)absRa;
            double minFrac = (absRa - hours) * 60.0;
            int minutes = (int)minFrac;
            double seconds = (minFrac - minutes) * 60.0;
            return string.Format(CultureInfo.InvariantCulture,
                "{0:D2} {1:D2} {2:F2}", hours, minutes, seconds);
        }

        // ---------------------------------------------------------------------------
        // Formats Dec as "+DD MM SS.s" for the FITS OBJCTDEC keyword.
        // Reference: FITS Standard §4.4.2.1
        // ---------------------------------------------------------------------------
        public string ToFitsDeclination() {
            if (double.IsNaN(DeclinationDegrees)) return string.Empty;
            string sign = DeclinationDegrees >= 0 ? "+" : "-";
            double absDec = Math.Abs(DeclinationDegrees);
            int degrees = (int)absDec;
            double minFrac = (absDec - degrees) * 60.0;
            int minutes = (int)minFrac;
            double seconds = (minFrac - minutes) * 60.0;
            return string.Format(CultureInfo.InvariantCulture,
                "{0}{1:D2} {2:D2} {3:F1}", sign, degrees, minutes, seconds);
        }

        // =====================================================================
        //  EQUALITY AND DISPLAY
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Value equality with astronomical precision tolerances:
        // 1 arcsecond for equatorial coords, 0.1° for horizontal coords.
        // ---------------------------------------------------------------------------
        public bool Equals(AstrometricData other) {
            if (other is null) return false;
            // 1 arcsecond RA tolerance (in hours: 1"/15 ≈ 0.0000185h)
            const double raTolerance = 0.0000185;
            // 1 arcsecond Dec tolerance (in degrees: 1/3600 ≈ 0.000278°)
            const double decTolerance = 0.000278;
            // 0.1° tolerance for horizontal (they change rapidly with tracking)
            const double horizTolerance = 0.1;

            bool raEq = BothNaN(RightAscensionHours, other.RightAscensionHours)
                || Math.Abs(RightAscensionHours - other.RightAscensionHours) < raTolerance;
            bool decEq = BothNaN(DeclinationDegrees, other.DeclinationDegrees)
                || Math.Abs(DeclinationDegrees - other.DeclinationDegrees) < decTolerance;
            bool altEq = BothNaN(AltitudeDegrees, other.AltitudeDegrees)
                || Math.Abs(AltitudeDegrees - other.AltitudeDegrees) < horizTolerance;
            bool azEq = BothNaN(AzimuthDegrees, other.AzimuthDegrees)
                || Math.Abs(AzimuthDegrees - other.AzimuthDegrees) < horizTolerance;
            bool nameEq = string.Equals(ObjectName, other.ObjectName,
                StringComparison.OrdinalIgnoreCase);

            return raEq && decEq && altEq && azEq && nameEq;
        }

        // Helper: both values NaN means both "unavailable" which is equal
        private static bool BothNaN(double a, double b) =>
            double.IsNaN(a) && double.IsNaN(b);

        public override bool Equals(object obj) => Equals(obj as AstrometricData);

        public override int GetHashCode() {
            int raHash = double.IsNaN(RightAscensionHours) ? 0 : Math.Round(RightAscensionHours, 5).GetHashCode();
            int decHash = double.IsNaN(DeclinationDegrees) ? 0 : Math.Round(DeclinationDegrees, 4).GetHashCode();
            return (raHash * 397) ^ (decHash * 23) ^ (ObjectName?.GetHashCode() ?? 0);
        }

        // ---------------------------------------------------------------------------
        // Human-readable summary for logging and the dc:description XMP field.
        // ---------------------------------------------------------------------------
        public override string ToString() {
            string result = string.Empty;
            if (!string.IsNullOrWhiteSpace(ObjectName)) {
                result += ObjectName + " ";
            }
            if (HasEquatorialCoordinates()) {
                result += string.Format(CultureInfo.InvariantCulture,
                    "RA={0} Dec={1}", ToFitsRightAscension(), ToFitsDeclination());
            }
            if (HasHorizontalCoordinates()) {
                result += string.Format(CultureInfo.InvariantCulture,
                    " Alt={0:F1}° Az={1:F1}°", AltitudeDegrees, AzimuthDegrees);
            }
            return result.Trim();
        }
    }
}