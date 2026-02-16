// *****************************************************************************
// File: Models/WeatherData.cs
// Purpose: Encapsulates environmental and weather conditions captured from
//          NINA's weather data mediator at the time a sequence item executes.
//          The data is embedded into camera images via a custom XMP namespace
//          (nina:) to record observation conditions alongside the images.
//
//          Recording environmental conditions is critical for astrophotography
//          because atmospheric parameters directly affect image quality:
//            - Temperature affects focus position (thermal expansion)
//            - Humidity and dew point indicate condensation risk
//            - Sky quality (SQM) quantifies light pollution / transparency
//            - Pressure affects refraction corrections
//
// References:
//   - NINA WeatherDataInfo: https://github.com/isbeorn/nina
//   - ASCOM IObservingConditions interface:
//     https://ascom-standards.org/Help/Developer/html/T_ASCOM_DeviceInterface_IObservingConditions.htm
//   - Bortle scale and SQM measurement:
//     https://en.wikipedia.org/wiki/Sky_quality_meter
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
    /// Immutable data model representing environmental/weather conditions
    /// at the observing site. All numeric properties use double.NaN as a
    /// sentinel for "not available", matching NINA's mediator conventions
    /// for weather devices that may not report all parameters.
    /// </summary>
    public sealed class WeatherData : IEquatable<WeatherData> {
        // =====================================================================
        //  TEMPERATURE AND MOISTURE
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Ambient air temperature in degrees Celsius.
        // Critical for astrophotography: thermal expansion changes the focuser
        // position, and temperature gradients cause atmospheric seeing.
        // Reference: ASCOM IObservingConditions.Temperature
        // ---------------------------------------------------------------------------
        public double Temperature { get; }

        // ---------------------------------------------------------------------------
        // Relative humidity as a percentage (0.0 to 100.0).
        // High humidity (>85%) increases the risk of dew forming on optics.
        // Reference: ASCOM IObservingConditions.Humidity
        // ---------------------------------------------------------------------------
        public double Humidity { get; }

        // ---------------------------------------------------------------------------
        // Dew point temperature in degrees Celsius.
        // When the ambient temperature approaches the dew point, condensation
        // will form on optical surfaces. A gap of <3°C warrants dew heaters.
        // Reference: ASCOM IObservingConditions.DewPoint
        // ---------------------------------------------------------------------------
        public double DewPoint { get; }

        // =====================================================================
        //  ATMOSPHERIC CONDITIONS
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Atmospheric pressure in hectopascals (hPa), equivalent to millibars.
        // Used for atmospheric refraction corrections that adjust telescope
        // pointing near the horizon.
        // Reference: ASCOM IObservingConditions.Pressure
        // ---------------------------------------------------------------------------
        public double Pressure { get; }

        // ---------------------------------------------------------------------------
        // Wind speed in meters per second.
        // High winds (>8 m/s) can cause telescope vibration and tracking errors.
        // Reference: ASCOM IObservingConditions.WindSpeed
        // ---------------------------------------------------------------------------
        public double WindSpeed { get; }

        // ---------------------------------------------------------------------------
        // Wind direction in degrees (0-360), measured clockwise from North.
        // Useful for determining wind-shadow effects and observatory dome slaving.
        // Reference: ASCOM IObservingConditions.WindDirection
        // ---------------------------------------------------------------------------
        public double WindDirection { get; }

        // =====================================================================
        //  SKY CONDITIONS
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Cloud cover as a percentage (0.0 to 100.0).
        // 0% = clear sky, 100% = fully overcast.
        // Reference: ASCOM IObservingConditions.CloudCover
        // ---------------------------------------------------------------------------
        public double CloudCover { get; }

        // ---------------------------------------------------------------------------
        // Sky Quality Meter reading in magnitudes per square arcsecond.
        // Higher values indicate darker skies (less light pollution):
        //   ~22.0 = excellent dark site (Bortle 1-2)
        //   ~21.0 = good rural site (Bortle 3-4)
        //   ~19.5 = suburban (Bortle 5-6)
        //   ~18.0 = urban (Bortle 7-8)
        // Reference: https://en.wikipedia.org/wiki/Sky_quality_meter
        // ---------------------------------------------------------------------------
        public double SkyQuality { get; }

        // =====================================================================
        //  CONSTRUCTOR
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Constructs an immutable snapshot. All parameters default to NaN.
        // The sequence item populates only the fields reported by the weather
        // device, leaving unsupported sensors as NaN.
        // ---------------------------------------------------------------------------
        public WeatherData(
            double temperature = double.NaN,
            double humidity = double.NaN,
            double dewPoint = double.NaN,
            double pressure = double.NaN,
            double windSpeed = double.NaN,
            double windDirection = double.NaN,
            double cloudCover = double.NaN,
            double skyQuality = double.NaN) {
            Temperature = temperature;
            Humidity = humidity;
            DewPoint = dewPoint;
            Pressure = pressure;
            WindSpeed = windSpeed;
            WindDirection = windDirection;
            CloudCover = cloudCover;
            SkyQuality = skyQuality;
        }

        // =====================================================================
        //  VALIDATION
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Returns true if at least one weather parameter is available.
        // Used by XmpBuilder to decide whether to include the weather section.
        // ---------------------------------------------------------------------------
        public bool HasAnyData() {
            return !double.IsNaN(Temperature)
                || !double.IsNaN(Humidity)
                || !double.IsNaN(DewPoint)
                || !double.IsNaN(Pressure)
                || !double.IsNaN(WindSpeed)
                || !double.IsNaN(CloudCover)
                || !double.IsNaN(SkyQuality);
        }

        // =====================================================================
        //  EQUALITY
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Value equality with tolerances appropriate for each measurement type.
        // Weather data changes continuously, so we use practical tolerances that
        // prevent unnecessary camera writes for insignificant fluctuations.
        // ---------------------------------------------------------------------------
        public bool Equals(WeatherData other) {
            if (other is null) return false;
            // 0.5°C tolerance for temperature (typical sensor precision)
            const double tempTol = 0.5;
            // 2% tolerance for humidity and cloud cover
            const double pctTol = 2.0;
            // 1 hPa tolerance for pressure
            const double pressTol = 1.0;
            // 0.5 m/s tolerance for wind speed
            const double windTol = 0.5;
            // 0.1 mag/arcsec² tolerance for SQM
            const double sqmTol = 0.1;

            return NaNOrClose(Temperature, other.Temperature, tempTol)
                && NaNOrClose(Humidity, other.Humidity, pctTol)
                && NaNOrClose(DewPoint, other.DewPoint, tempTol)
                && NaNOrClose(Pressure, other.Pressure, pressTol)
                && NaNOrClose(WindSpeed, other.WindSpeed, windTol)
                && NaNOrClose(CloudCover, other.CloudCover, pctTol)
                && NaNOrClose(SkyQuality, other.SkyQuality, sqmTol);
        }

        // Helper: true if both NaN, or both within tolerance
        private static bool NaNOrClose(double a, double b, double tolerance) {
            if (double.IsNaN(a) && double.IsNaN(b)) return true;
            if (double.IsNaN(a) || double.IsNaN(b)) return false;
            return Math.Abs(a - b) < tolerance;
        }

        public override bool Equals(object obj) => Equals(obj as WeatherData);

        public override int GetHashCode() {
            // Round to tolerance-matched precision
            int tempHash = double.IsNaN(Temperature) ? 0 : Math.Round(Temperature, 1).GetHashCode();
            int humHash = double.IsNaN(Humidity) ? 0 : Math.Round(Humidity, 0).GetHashCode();
            int sqmHash = double.IsNaN(SkyQuality) ? 0 : Math.Round(SkyQuality, 1).GetHashCode();
            return (tempHash * 397) ^ (humHash * 23) ^ sqmHash;
        }

        // =====================================================================
        //  DISPLAY
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Human-readable summary for logging and the dc:description XMP field.
        // Only includes parameters that have valid (non-NaN) values.
        // ---------------------------------------------------------------------------
        public override string ToString() {
            // Build a compact weather summary string
            var parts = new System.Collections.Generic.List<string>();

            if (!double.IsNaN(Temperature)) {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:F1}°C", Temperature));
            }
            if (!double.IsNaN(Humidity)) {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:F0}%RH", Humidity));
            }
            if (!double.IsNaN(DewPoint)) {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "Dew:{0:F1}°C", DewPoint));
            }
            if (!double.IsNaN(Pressure)) {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:F0}hPa", Pressure));
            }
            if (!double.IsNaN(WindSpeed)) {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "Wind:{0:F1}m/s", WindSpeed));
            }
            if (!double.IsNaN(SkyQuality)) {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "SQM:{0:F2}", SkyQuality));
            }
            if (!double.IsNaN(CloudCover)) {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "Cloud:{0:F0}%", CloudCover));
            }

            return parts.Count > 0
                ? string.Join(", ", parts)
                : "No weather data";
        }
    }
}