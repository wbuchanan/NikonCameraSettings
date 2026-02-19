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

    public class AltAz {
        public double AltitudeDegrees { get; }
        public double AzimuthDegrees { get; }
        public string AltitudeDMS { get; }
        public string AzimuthDMS { get; }
        public AltAz(double altitudeDegrees, double azimuthDegrees) {
            AltitudeDegrees = altitudeDegrees;
            AzimuthDegrees = azimuthDegrees;
            AltitudeDMS = ToDMS(altitudeDegrees);
            AzimuthDMS = ToDMS(azimuthDegrees);
        }

        public static string ToDMS(double degrees) {
            if (double.IsNaN(degrees)) return string.Empty;
            string sign = degrees >= 0 ? "+" : "-";
            double absDeg = Math.Abs(degrees);
            int d = (int)absDeg;
            double minFrac = (absDeg - d) * 60.0;
            int m = (int)minFrac;
            double s = (minFrac - m) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:D2}º{2:D2}:{3:F2}", sign, d, m, s);
        }

    }

    public sealed class AstrometricData : IEquatable<AstrometricData> {
        private const double DegreesToRadians = Math.PI / 180.0;
        private const double RadiansToDegrees = 180.0 / Math.PI;

        public double RightAscension { get; }
        public string RaDMS {  get; }
        public double DeclinationDegrees { get; }
        public string DecDMS { get; }
        public double AltitudeDegrees { get; }
        public string AltitudeDMS { get; }
        public double AzimuthDegrees { get; }
        public string AzimuthDMS { get; }
        public double SiderealTimeHours { get; }
        public string ObjectName { get; }

        public AstrometricData(double rightAscension = double.NaN, double declinationDegrees = double.NaN, double altitudeDegrees = double.NaN,
                               double azimuthDegrees = double.NaN, double siderealTimeHours = double.NaN, string objectName = null,
                               double longitude = double.NaN, double latitude = double.NaN) {

            RightAscension = rightAscension;
            DeclinationDegrees = declinationDegrees;
            AltitudeDegrees = altitudeDegrees;
            AzimuthDegrees = azimuthDegrees;
            SiderealTimeHours = siderealTimeHours;

            // Normalize null to empty for consistent downstream handling
            ObjectName = objectName ?? string.Empty;

            if (longitude >= -180 && longitude <= 180 && longitude != double.NaN) {
                SiderealTimeHours = LocalApproxSiderealTime(longitude);
            }

            if (SiderealTimeHours != double.NaN && latitude != double.NaN) {
                AltAz coords = EquatorialToHorizontal(SiderealTimeHours, RightAscension, DeclinationDegrees, 0.0);
                altitudeDegrees = coords.AltitudeDegrees;
                azimuthDegrees = coords.AzimuthDegrees;
                AltitudeDMS = coords.AltitudeDMS;
                AzimuthDMS = coords.AzimuthDMS;
            }

        }

        public void SetLocalSiderealTime(double longitude) {
            // This method is intentionally left blank since the class is immutable.
            // In a mutable design, this would update the SiderealTimeHours property.
            throw new InvalidOperationException("AstrometricData is immutable. Create a new instance with the desired sidereal time.");
        }

        public bool HasEquatorialCoordinates() {
            bool raValid = !double.IsNaN(RightAscension)
                && RightAscension >= 0.0 && RightAscension < 24.0;
            bool decValid = !double.IsNaN(DeclinationDegrees)
                && DeclinationDegrees >= -90.0 && DeclinationDegrees <= 90.0;
            return raValid && decValid;
        }

        public bool HasHorizontalCoordinates() {
            bool altValid = !double.IsNaN(AltitudeDegrees)
                && AltitudeDegrees >= -90.0 && AltitudeDegrees <= 90.0;
            bool azValid = !double.IsNaN(AzimuthDegrees)
                && AzimuthDegrees >= 0.0 && AzimuthDegrees < 360.0;
            return altValid && azValid;
        }

        public bool HasAnyData() {
            return HasEquatorialCoordinates()
                || HasHorizontalCoordinates()
                || !string.IsNullOrWhiteSpace(ObjectName);
        }

        public string ToHMSRightAscension() {
            if (double.IsNaN(RightAscension)) return string.Empty;
            double absRa = Math.Abs(RightAscension);
            int hours = (int)absRa;
            double minFrac = (absRa - hours) * 60.0;
            int minutes = (int)minFrac;
            double seconds = (minFrac - minutes) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:D2} {1:D2} {2:F2}", hours, minutes, seconds);
        }

        public string ToDMSDeclination() {
            if (double.IsNaN(DeclinationDegrees)) return string.Empty;
            string sign = DeclinationDegrees >= 0 ? "+" : "-";
            double absDec = Math.Abs(DeclinationDegrees);
            int degrees = (int)absDec;
            double minFrac = (absDec - degrees) * 60.0;
            int minutes = (int)minFrac;
            double seconds = (minFrac - minutes) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:D2} {2:D2} {3:F1}", sign, degrees, minutes, seconds);
        }

        public bool Equals(AstrometricData other) {
            if (other is null) return false;
            // 1 arcsecond RA tolerance (in hours: 1"/15 ≈ 0.0000185h)
            const double raTolerance = 0.0000185;
            // 1 arcsecond Dec tolerance (in degrees: 1/3600 ≈ 0.000278°)
            const double decTolerance = 0.000278;
            // 0.1° tolerance for horizontal (they change rapidly with tracking)
            const double horizTolerance = 0.1;

            bool raEq = BothNaN(RightAscension, other.RightAscension)
                || Math.Abs(RightAscension - other.RightAscension) < raTolerance;
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

        private static bool BothNaN(double a, double b) => double.IsNaN(a) && double.IsNaN(b);

        public override bool Equals(object obj) => Equals(obj as AstrometricData);

        public override int GetHashCode() {
            int raHash = double.IsNaN(RightAscension) ? 0 : Math.Round(RightAscension, 5).GetHashCode();
            int decHash = double.IsNaN(DeclinationDegrees) ? 0 : Math.Round(DeclinationDegrees, 4).GetHashCode();
            return (raHash * 397) ^ (decHash * 23) ^ (ObjectName?.GetHashCode() ?? 0);
        }

        public override string ToString() {
            string result = string.Empty;
            if (!string.IsNullOrWhiteSpace(ObjectName)) {
                result += ObjectName + " ";
            }
            if (HasEquatorialCoordinates()) {
                result += string.Format(CultureInfo.InvariantCulture, "RA={0} Dec={1}", ToHMSRightAscension(), ToDMSDeclination());
            }
            if (HasHorizontalCoordinates()) {
                result += string.Format(CultureInfo.InvariantCulture, " Alt={0:F1}° Az={1:F1}°", AltitudeDegrees, AzimuthDegrees);
            }
            return result.Trim();
        }

        public static double LocalApproxSiderealTime(double longitudeDegrees) {
            if (longitudeDegrees < -180.0 || longitudeDegrees > 180.0)
                throw new ArgumentOutOfRangeException(nameof(longitudeDegrees),
                    "Longitude must be in the range −180° to +180° (east positive).");
            DateTime utcNow = DateTime.UtcNow;
            double jdUt = DateTimeToJulianDate(utcNow);
            double jd0 = Math.Floor(jdUt - 0.5) + 0.5;
            double H = (jdUt - jd0) * 24.0;
            const double j2000 = 2451545.0;
            double dUt = jd0 - j2000;
            double dTt = jdUt - j2000;
            double T = dTt / 36525.0;
            double gmst = 6.697375 + 0.065707485828 * dUt + 1.0027379 * H + 0.0854103 * T + 0.0000258 * (T * T);
            gmst = Mod(gmst, 24.0);
            double omega = 125.04 - 0.052954 * dTt;
            double L = 280.47 + 0.98565 * dTt;
            double epsilon = 23.4393 - 0.0000004 * dTt;
            double omegaRad = omega * DegreesToRadians;
            double twoLRad = 2.0 * L * DegreesToRadians;
            double epsilonRad = epsilon * DegreesToRadians;
            double deltaPsi = -0.000319 * Math.Sin(omegaRad) - 0.000024 * Math.Sin(twoLRad);
            double eqeq = deltaPsi * Math.Cos(epsilonRad);
            double gast = Mod(gmst + eqeq, 24.0);
            double last = Mod(gast + longitudeDegrees / 15.0, 24.0);
            return last;
        }

        public static AltAz EquatorialToHorizontal(double lst, double ra, double dec, double latitude) {
            if (lst < 0.0 || lst >= 24.0) throw new ArgumentOutOfRangeException(nameof(lst), "Local Approximate Sidereal Time must be in the range [0, 24) hours.");
            if (ra < 0.0 || ra >= 24.0) throw new ArgumentOutOfRangeException(nameof(ra), "Right Ascension must be in the range [0, 24) hours.");
            if (dec < -90.0 || dec > 90.0) throw new ArgumentOutOfRangeException(nameof(dec), "Declination must be in the range −90° to +90°.");
            if (latitude < -90.0 || latitude > 90.0) throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be in the range −90° to +90°.");
            double lhaHours = Mod(lst - ra, 24.0);
            double lhaDegrees = lhaHours * 15.0;
            double lhaRad = lhaDegrees * DegreesToRadians;
            double decRad = dec * DegreesToRadians;
            double latRad = latitude * DegreesToRadians;
            double sinLHA = Math.Sin(lhaRad);
            double cosLHA = Math.Cos(lhaRad);
            double sinDec = Math.Sin(decRad);
            double cosDec = Math.Cos(decRad);
            double sinLat = Math.Sin(latRad);
            double cosLat = Math.Cos(latRad);
            double sinAlt = sinDec * sinLat + cosDec * cosLat * cosLHA;
            sinAlt = Math.Max(-1.0, Math.Min(1.0, sinAlt));
            double altRad = Math.Asin(sinAlt);
            double altitudeDegrees = altRad * RadiansToDegrees;
            double ay = -sinLHA * cosDec;
            double ax = sinDec * cosLat - cosDec * sinLat * cosLHA;
            double azRad = Math.Atan2(ay, ax);
            double azimuthDegrees = azRad * RadiansToDegrees;
            azimuthDegrees = Mod(azimuthDegrees, 360.0);
            return new AltAz(altitudeDegrees, azimuthDegrees);
        }

        private static double DateTimeToJulianDate(DateTime dt) {
            int Y = dt.Year;
            int M = dt.Month;
            int D = dt.Day;
            double dayFraction = (dt.Hour + dt.Minute / 60.0 + dt.Second / 3_600.0 + dt.Millisecond / 3_600_000.0) / 24.0;
            if (M <= 2) { Y -= 1; M += 12; }
            int A = Y / 100;
            int B = 2 - A + (A / 4);
            double jd = Math.Floor(365.25 * (Y + 4716.0)) + Math.Floor(30.6001 * (M + 1)) + D + B - 1524.5 + dayFraction;
            return jd;
        }

        private static double Mod(double value, double modulus) {
            double result = value % modulus;
            return result < 0.0 ? result + modulus : result;
        }
    }
}