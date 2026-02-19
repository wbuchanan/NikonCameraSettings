#region "copyright"

/*
    Copyright © 2026 William Buchanan (william@williambuchanan.net)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Globalization;

namespace NikonCameraSettings.Models {

    public class GPSParts {
        public int degrees;
        public int minutes;
        public int seconds;
        public string hemisphere;

        public GPSParts(int deg, int min, int sec, string hemi) {
            degrees = deg;
            minutes = min;
            seconds = sec;
            hemisphere = hemi;
        }

        override public string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "{0}°{1}'{2}\"{3}", degrees, minutes, seconds, hemisphere);
        }

        public string ToExifString() {
            return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", degrees, minutes, seconds);
        }

    }

    public sealed class GpsData : IEquatable<GpsData> {

        public double Latitude { get; }
        public double Longitude { get; }
        public double Elevation { get; }
        public DateTime dtstamp;
        public Dictionary<string, string> gpsDTStamps;
        public GPSParts lat;
        public GPSParts lon;

        public GpsData(double latitude, double longitude, double elevation) {
            Latitude = latitude;
            Longitude = longitude;
            Elevation = elevation;
            lat = ToParts(latitude);
            lon = ToParts(longitude, false);
            dtstamp = DateTime.UtcNow;
            makeStamps();
        }

        public string ToExifLatitudeRef() {
            return Latitude >= 0 ? "N" : "S";
        }

        public string ToExifLongitudeRef() {
            return Longitude >= 0 ? "E" : "W";
        }

        private void makeStamps() {

            string H = dtstamp.Hour.ToString();
            string M = dtstamp.Minute.ToString();
            string S = dtstamp.Second.ToString();
            gpsDTStamps = new Dictionary<string, string>() {
                { "GPSTimeStamp", H + "," + M + "," + S },
                { "GPSDateStamp", $"{dtstamp:yyyy:MM:dd}" }
            };
        }

        public GPSParts ToParts(double coord, bool isLat = true) {
            string hemi;
            if (isLat) hemi = ToExifLatitudeRef();
            else hemi = ToExifLongitudeRef();
            double abs = Math.Abs(coord);
            int degrees = (int)abs;
            double minFrac = (abs - degrees) * 60.0;
            int minutes = (int)minFrac;
            double seconds = (minFrac - minutes) * 60.0;
            return new GPSParts(degrees, minutes, (int)seconds, hemi);
        }

        public string ToExifAltitude() {
            long numerator = (long)Math.Round(Math.Abs(Elevation));
            return string.Format(CultureInfo.InvariantCulture, "{0}", numerator);
        }

        public string ToExifAltitudeRef() {
            return Elevation >= 0 ? "0" : "1";
        }

        public bool IsValid() {
            bool isNullIsland = Math.Abs(Latitude) < 0.0001 && Math.Abs(Longitude) < 0.0001;
            bool latInRange = Latitude >= -90.0 && Latitude <= 90.0;
            bool lonInRange = Longitude >= -180.0 && Longitude <= 180.0;
            return !isNullIsland && latInRange && lonInRange;
        }

        public bool Equals(GpsData other) {
            if (other is null) return false;
            const double coordTolerance = 0.0001;
            const double elevTolerance = 0.1;
            return Math.Abs(Latitude - other.Latitude) < coordTolerance
                && Math.Abs(Longitude - other.Longitude) < coordTolerance
                && Math.Abs(Elevation - other.Elevation) < elevTolerance;
        }

        public override bool Equals(object obj) {
            return Equals(obj as GpsData);
        }

        public override int GetHashCode() {
            int latHash = Math.Round(Latitude, 4).GetHashCode();
            int lonHash = Math.Round(Longitude, 4).GetHashCode();
            int eleHash = Math.Round(Elevation, 1).GetHashCode();
            return (latHash * 397) ^ (lonHash * 23) ^ eleHash;
        }

        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "GPS: {0}, {1}, Elev: {2:F1}m", lat.ToString(), lon.ToString(), Elevation);
        }
    }
}
