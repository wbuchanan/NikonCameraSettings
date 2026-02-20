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

    public sealed class WeatherData : IEquatable<WeatherData> {
        public double Temperature { get; }
        public double Humidity { get; }
        public double DewPoint { get; }
        public double Pressure { get; }
        public double WindSpeed { get; }
        public double WindDirection { get; }
        public double CloudCover { get; }
        // This is not included in the XISF format currently, but hopefully it will be at some point.
        public double SkyQuality { get; }
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

        public bool HasAnyData() {
            return !double.IsNaN(Temperature)
                || !double.IsNaN(Humidity)
                || !double.IsNaN(DewPoint)
                || !double.IsNaN(Pressure)
                || !double.IsNaN(WindSpeed)
                || !double.IsNaN(CloudCover)
                || !double.IsNaN(SkyQuality);
        }

        public bool Equals(WeatherData other) {
            if (other is null) return false;
            const double tempTol = 0.5;
            const double pctTol = 2.0;
            const double pressTol = 1.0;
            const double windTol = 0.5;
            const double sqmTol = 0.1;

            return NaNOrClose(Temperature, other.Temperature, tempTol)
                && NaNOrClose(Humidity, other.Humidity, pctTol)
                && NaNOrClose(DewPoint, other.DewPoint, tempTol)
                && NaNOrClose(Pressure, other.Pressure, pressTol)
                && NaNOrClose(WindSpeed, other.WindSpeed, windTol)
                && NaNOrClose(CloudCover, other.CloudCover, pctTol)
                && NaNOrClose(SkyQuality, other.SkyQuality, sqmTol);
        }

        private static bool NaNOrClose(double a, double b, double tolerance) {
            if (double.IsNaN(a) && double.IsNaN(b)) return true;
            if (double.IsNaN(a) || double.IsNaN(b)) return false;
            return Math.Abs(a - b) < tolerance;
        }

        public override bool Equals(object obj) => Equals(obj as WeatherData);

        public override int GetHashCode() {
            int tempHash = double.IsNaN(Temperature) ? 0 : Math.Round(Temperature, 1).GetHashCode();
            int humHash = double.IsNaN(Humidity) ? 0 : Math.Round(Humidity, 0).GetHashCode();
            int sqmHash = double.IsNaN(SkyQuality) ? 0 : Math.Round(SkyQuality, 1).GetHashCode();
            return (tempHash * 397) ^ (humHash * 23) ^ sqmHash;
        }

        public override string ToString() {
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
                parts.Add(string.Format(CultureInfo.InvariantCulture, "Sky Quality:{0:F2}", SkyQuality));
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