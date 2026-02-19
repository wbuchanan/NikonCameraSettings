#region "copyright"

/*
    Copyright © 2026 William Buchanan (william@williambuchanan.net)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NikonCameraSettings.Models;
using System;
using System.Globalization;
using System.Text;

namespace NikonCameraSettings.Utils {

    /// <summary>
    /// Six namespaces are used:
    ///   exif:          — EXIF GPS tags (universal photo app compatibility)
    ///   xisf:          — XISF Observation properties (PixInsight/XISF round-trip)
    ///   dc:            — Dublin Core (description, title, creator)
    ///   photoshop:     — Adobe Photoshop schema (headline, city, credit, etc.)
    ///   Iptc4xmpCore:  - Core IPTC elements.
    ///   Iptc4xmpExt:   — IPTC Extension (event identifier)
    /// </summary>
    public static class XmpBuilder {

        // =====================================================================
        //  NAMESPACE CONSTANTS
        // =====================================================================
        private const string ExifNs = "http://ns.adobe.com/exif/1.0/";
        private const string DcNs = "http://purl.org/dc/elements/1.1/";
        private const string XisfNs = "http://pixinsight.com/xisf/1.0/";
        private const string PhotoshopNs = "http://ns.adobe.com/photoshop/1.0/";
        private const string Iptc4xmpCore = "http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/";
        private const string IptcExtNs = "http://iptc.org/std/Iptc4xmpExt/2008-02-29/";

        public const int MaxXmpDataBytes = 30720;

        public static int GetXmpByteCount(string xmpXml) {
            if (string.IsNullOrEmpty(xmpXml)) return 0;
            return Encoding.UTF8.GetByteCount(xmpXml) + 1;
        }

        public static string BuildGpsXmp(GpsData gps) {
            if (gps == null) {
                throw new ArgumentNullException(nameof(gps), "GpsData must not be null.");
            }

            StringBuilder sb = new StringBuilder();

            // Should move the header and description opening to another method
            WritePacketHeader(sb);
            WriteDescriptionOpen(sb);

            WriteGpsSection(sb, gps);
            WriteXisfGPS(sb, gps);

            WriteDescriptionSummary(sb, null, gps, null, null);

            // Should move the closing and footer to a new method
            sb.AppendLine("    </rdf:Description>");
            WritePacketFooter(sb);

            return sb.ToString();
        }

        public static string BuildUnifiedXmp(GpsData gps, AstrometricData astro,
            WeatherData weather, NmsIptcData iptc = null) {

            bool hasGps = gps != null && gps.IsValid();
            bool hasAstro = astro != null && astro.HasAnyData();
            bool hasWeather = weather != null && weather.HasAnyData();
            bool hasIptc = iptc != null && iptc.HasAnyData();

            if (!hasGps && !hasAstro && !hasWeather && !hasIptc) {
                throw new ArgumentException(
                    "At least one data source (GPS, astrometric, weather, or IPTC) " +
                    "must contain valid data to build an XMP payload.");
            }

            StringBuilder sb = new StringBuilder(8192);
            WritePacketHeader(sb);
            WriteDescriptionOpen(sb);

            if (hasGps) {
                sb.AppendLine("      <!-- GPS location (EXIF) -->");
                WriteGpsSection(sb, gps);
            }

            if (hasGps) {
                sb.AppendLine("      <!-- XISF Observation location -->");
                WriteXisfGPS(sb, gps);
            }

            if (hasAstro) {
                sb.AppendLine("      <!-- XISF Observation astrometric data -->");
                WriteXisfAstro(sb, astro);
            }

            if (hasWeather) {
                sb.AppendLine("      <!-- XISF Observation meteorology -->");
                WriteXisfWeather(sb, weather);
            }

            if (hasIptc) {
                sb.AppendLine("      <!-- Standard IPTC metadata -->");
                WriteIptcSection(sb, iptc);
            }

            sb.AppendLine("      <!-- Human-readable description -->");
            WriteDescriptionSummary(sb, hasIptc ? iptc : null, hasGps ? gps : null,
                                    hasAstro ? astro : null, hasWeather ? weather : null);

            sb.AppendLine("    </rdf:Description>");
            WritePacketFooter(sb);
            return sb.ToString();
        }


        private static void WriteGpsSection(StringBuilder sb, GpsData gps) {
            sb.AppendLine("      <exif:GPSVersionID>2.4.0.0</exif:GPSVersionID>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <exif:GPSLatitudeRef>{0}</exif:GPSLatitudeRef>", gps.lat.hemisphere));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <exif:GPSLatitude>{0}</exif:GPSLatitude>", gps.lat.ToExifString()));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <exif:GPSLongitudeRef>{0}</exif:GPSLongitudeRef>", gps.lon.hemisphere));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <exif:GPSLongitude>{0}</exif:GPSLongitude>", gps.lon.ToExifString()));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <exif:GPSAltitudeRef>{0}</exif:GPSAltitudeRef>", gps.Elevation >= 0 ? "0" : "1"));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <exif:GPSAltitude>{0}</exif:GPSAltitude>", (int)Math.Round(Math.Abs(gps.Elevation))));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <exif:GPSTimeStamp>{0}</exif:GPSTimeStamp>", gps.gpsDTStamps["GPSTimeStamp"]));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <exif:GPSDateStamp>{0}</exif:GPSDateStamp>", gps.gpsDTStamps["GPSDateStamp"]));
            sb.AppendLine("      <exif:GPSMapDatum>WGS-84</exif:GPSMapDatum>");
        }


        private static void WriteXisfGPS(StringBuilder sb, GpsData gps) {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <xisf:Observation.Location.Latitude>{0:F8}</xisf:Observation.Location.Latitude>", gps.Latitude));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <xisf:Observation.Location.Longitude>{0:F8}</xisf:Observation.Location.Longitude>", gps.Longitude));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <xisf:Observation.Location.Elevation>{0:F2}</xisf:Observation.Location.Elevation>", gps.Elevation));
            sb.AppendLine("      <xisf:Observation.GeodeticReferenceSystem>WGS 84</xisf:Observation.GeodeticReferenceSystem>");
        }

        private static void WriteXisfAstro(StringBuilder sb, AstrometricData astro) {
            if (!string.IsNullOrWhiteSpace(astro.ObjectName)) {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <xisf:Observation.Object.Name>{0}</xisf:Observation.Object.Name>", EscapeXml(astro.ObjectName)));
            }
            if (!double.IsNaN(astro.RightAscension)) {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <xisf:Observation.Object.RA>{0:F8}</xisf:Observation.Object.RA>", astro.RightAscension));
            }
            if (!double.IsNaN(astro.DeclinationDegrees)) {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <xisf:Observation.Object.Dec>{0:F8}</xisf:Observation.Object.Dec>", astro.DeclinationDegrees));
            }
            // Observation:Center:Alt — altitude above horizon in degrees (EXTENSION)
            if (!double.IsNaN(astro.AltitudeDegrees) && astro.AltitudeDegrees > -90.0) {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <xisf:Observation.Center.Alt>{0:F4}</xisf:Observation.Center.Alt>", astro.AltitudeDegrees));
            }
            // Observation:Center:Az — azimuth from North in degrees (EXTENSION)
            if (!double.IsNaN(astro.AzimuthDegrees)) {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <xisf:Observation.Center.Az>{0:F4}</xisf:Observation.Center.Az>", astro.AzimuthDegrees));
            }
            // Observation:CelestialReferenceSystem — coordinate reference frame
            sb.AppendLine("      <xisf:Observation.CelestialReferenceSystem>ICRS</xisf:Observation.CelestialReferenceSystem>");
            sb.AppendLine("      <xisf:Observation.Equinox>2000.0</xisf:Observation.Equinox>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <xisf:Observation.Time.Start>{0:yyyy-MM-ddTHH:mm:ss.ffZ}</xisf:Observation.Time.Start>", DateTime.UtcNow));
        }

        private static void WriteXisfWeather(StringBuilder sb, WeatherData weather) {

            // Observation:Meteorology:AmbientTemperature — °C (no conversion)
            WriteOptionalDouble(sb, "xisf:Observation.Meteorology.AmbientTemperature", weather.Temperature, "F1");

            // Observation:Meteorology:AtmosphericPressure — hPa (no conversion)
            WriteOptionalDouble(sb, "xisf:Observation.Meteorology.AtmosphericPressure", weather.Pressure, "F1");

            // Observation:Meteorology:RelativeHumidity — % (no conversion)
            WriteOptionalDouble(sb, "xisf:Observation.Meteorology.RelativeHumidity", weather.Humidity, "F1");

            WriteOptionalDouble(sb, "xisf:Observation.Meteorology.WindDirection", weather.WindDirection, "F0");

            // WriteOptionalDouble(sb, "xisf:Observation.Meterology.WindGust", weather.WindGust, "F0");

            if (!double.IsNaN(weather.WindSpeed)) {
                double windSpeedKmh = weather.WindSpeed * 3.6;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <xisf:Observation.Meteorology.WindSpeed>{0:F1}</xisf:Observation.Meteorology.WindSpeed>", windSpeedKmh));
            }

            // Observation:Meteorology:DewPoint — °C (EXTENSION)
            // WriteOptionalDouble(sb, "xisf:Observation.Meteorology.DewPoint", weather.DewPoint, "F1");

            // Observation:Meteorology:SkyQuality — mag/arcsec² (EXTENSION)
            WriteOptionalDouble(sb, "xisf:Observation.Meteorology.SkyQuality", weather.SkyQuality, "F2");

            // Observation:Meteorology:CloudCover — % (EXTENSION)
            // WriteOptionalDouble(sb, "xisf:Observation.Meteorology.CloudCover", weather.CloudCover, "F0");

        }

        private static void WriteIptcSection(StringBuilder sb, NmsIptcData iptc) {
            if (!string.IsNullOrEmpty(iptc.AltText)) WriteLangAlt(sb, "Iptc4xmpCore:AltTextAccessibility", iptc.AltText);
            if (!string.IsNullOrEmpty(iptc.City)) WriteOptionalString(sb, "photoshop:City", iptc.City);
            if (!string.IsNullOrEmpty(iptc.Copyright)) WriteLangAlt(sb, "dc:rights", iptc.Copyright);
            if (!string.IsNullOrEmpty(iptc.Country)) WriteOptionalString(sb, "photoshop:Country", iptc.Country);
            if (!string.IsNullOrEmpty(iptc.CountryCode)) WriteOptionalString(sb, "Iptc4xmpCore:CountryCode", iptc.CountryCode);
            if (!string.IsNullOrEmpty(iptc.ObjectName)) WriteLangAlt(sb, "dc:title", iptc.ObjectName);
            if (!string.IsNullOrEmpty(iptc.Creator)) {
                sb.AppendLine("      <dc:creator>");
                sb.AppendLine("        <rdf:Seq>");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "          <rdf:li>{0}</rdf:li>", EscapeXml(iptc.Creator)));
                sb.AppendLine("        </rdf:Seq>");
                sb.AppendLine("      </dc:creator>"); ;
            }
            if (!string.IsNullOrEmpty(iptc.EventName)) WriteOptionalString(sb, "Iptc4xmpExt:EventId", iptc.EventName);
            if (!string.IsNullOrEmpty(iptc.Headline)) WriteOptionalString(sb, "photoshop:Headline", iptc.Headline);
            if (!string.IsNullOrEmpty(iptc.State)) WriteOptionalString(sb, "photoshop:State", iptc.State);
            if (!string.IsNullOrEmpty(iptc.JobTitle)) WriteOptionalString(sb, "photoshop:AuthorsPosition", iptc.JobTitle);
            if (!string.IsNullOrEmpty(iptc.CaptionWriter)) WriteOptionalString(sb, "photoshop:CaptionWriter", iptc.CaptionWriter);
            if (!string.IsNullOrEmpty(iptc.CreditLine)) WriteOptionalString(sb, "photoshop:CreditLine", iptc.CreditLine);
            if (!string.IsNullOrEmpty(iptc.Source)) WriteOptionalString(sb, "photoshop:Source", iptc.Source);
        }


        private static void WriteDescriptionSummary(StringBuilder sb, NmsIptcData iptc, GpsData gps, AstrometricData astro, WeatherData weather) {

            StringBuilder obsSummary = new StringBuilder(256);

            if (gps != null && gps.IsValid()) {
                obsSummary.AppendFormat(CultureInfo.InvariantCulture, "{0} {1} {2:F0}m", gps.lat.ToString(), gps.lon.ToString(), gps.Elevation);
            }

            if (astro != null && astro.HasAnyData()) {
                if (obsSummary.Length > 0) obsSummary.Append(" | ");
                if (!string.IsNullOrWhiteSpace(astro.ObjectName)) obsSummary.Append(astro.ObjectName);
                if (!double.IsNaN(astro.RightAscension)) obsSummary.AppendFormat(CultureInfo.InvariantCulture, " RA={0}", astro.ToHMSRightAscension() ?? "N/A");
                if (!double.IsNaN(astro.DeclinationDegrees)) obsSummary.AppendFormat(CultureInfo.InvariantCulture, " Dec={0}", astro.ToDMSDeclination() ?? "N/A");
                if (!double.IsNaN(astro.AltitudeDegrees)) obsSummary.AppendFormat(CultureInfo.InvariantCulture, " Alt={0:F1}", astro.AltitudeDegrees);
                if (!double.IsNaN(astro.AzimuthDegrees)) obsSummary.AppendFormat(CultureInfo.InvariantCulture, " Az={0:F1}", astro.AzimuthDegrees);
            }

            if (weather != null && weather.HasAnyData()) {
                if (obsSummary.Length > 0) obsSummary.Append(" | ");
                obsSummary.Append(weather.ToString());
            }

            string nmsCaption = (iptc != null && !string.IsNullOrEmpty(iptc.Caption)) ? iptc.Caption : null;
            string obsText = obsSummary.Length > 0 ? obsSummary.ToString() : null;

            string finalDescription;
            if (nmsCaption != null && obsText != null) {
                finalDescription = nmsCaption + " | " + obsText;
            } else if (nmsCaption != null) {
                finalDescription = nmsCaption;
            } else if (obsText != null) {
                finalDescription = obsText;
            } else {
                return;
            }

            WriteLangAlt(sb, "dc:description", finalDescription);
        }

        // Double check packet header format
        private static void WritePacketHeader(StringBuilder sb) {
            // The 'begin' attribute contains the UTF-8 BOM (U+FEFF) and the 'id'
            // is the W3C-defined XMP packet identifier string.
            sb.AppendLine(
                "<?xpacket begin=\"\xEF\xBB\xBF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
            // x:xmpmeta root element wraps all XMP content
            sb.AppendLine("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">");
            // rdf:RDF container holds all rdf:Description elements
            sb.AppendLine("  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        }

        private static void WriteDescriptionOpen(StringBuilder sb) {
            sb.AppendLine("    <rdf:Description rdf:about=\"\"");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "        xmlns:exif=\"{0}\"", ExifNs));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "        xmlns:xisf=\"{0}\"", XisfNs));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "        xmlns:dc=\"{0}\"", DcNs));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "        xmlns:photoshop=\"{0}\"", PhotoshopNs));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "        xmlns:Iptc4xmpCore=\"{0}\"", Iptc4xmpCore));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "        xmlns:Iptc4xmpExt=\"{0}\">", IptcExtNs));
        }

        private static void WritePacketFooter(StringBuilder sb) {
            sb.AppendLine("  </rdf:RDF>");
            sb.AppendLine("</x:xmpmeta>");
            // The 'w' end marker indicates the packet can be updated in-place
            sb.Append("<?xpacket end=\"w\"?>");
        }

        private static void WriteOptionalDouble(StringBuilder sb, string tagName, double value, string format) {
            if (double.IsNaN(value)) return;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <{0}>{1}</{0}>", tagName, value.ToString(format, CultureInfo.InvariantCulture)));
        }

        private static void WriteOptionalString(StringBuilder sb, string tagName, string value) {
            if (string.IsNullOrEmpty(value)) return;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <{0}>{1}</{0}>", tagName, EscapeXml(value)));
        }

        private static void WriteLangAlt(StringBuilder sb, string tagName, string value) {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      <{0}>", tagName));
            sb.AppendLine("        <rdf:Alt>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "          <rdf:li xml:lang=\"x-default\">{0}</rdf:li>", EscapeXml(value)));
            sb.AppendLine("        </rdf:Alt>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      </{0}>", tagName));
        }

        private static string EscapeXml(string value) {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private static int GetByteLength(string text) {
            return Encoding.UTF8.GetByteCount(text);
        }
    }
}