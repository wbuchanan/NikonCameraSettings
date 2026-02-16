// *****************************************************************************
// File: Utils/XmpBuilder.cs
// Purpose: Constructs well-formed XMP (Extensible Metadata Platform) XML
//          payloads for embedding astronomical observation metadata into
//          images captured by Nikon cameras via the SDK's XMP-IPTC preset
//          system (kNkMAIDCapability_IPTCPresetInfo, slots 11–13).
//
//          GPS data uses the standard exif: namespace (NOT dc:description).
//          Astrometric data uses dual encoding: FITS-standard keywords in a
//          custom fits: namespace for PixInsight/APP compatibility, plus AVM
//          (Astronomy Visualization Metadata) properties for tools like
//          WorldWide Telescope that understand the AVM standard.
//          Weather/environmental data uses a custom astro: namespace.
//
// Design decisions:
//   1. GPS in exif: namespace — this is the standard way to embed GPS in XMP.
//      Photo software (Lightroom, ExifTool, Bridge, Google Photos) reads
//      exif:GPSLatitude from XMP identically to binary EXIF GPS IFD tags.
//      The Nikon MAID SDK provides no capability to write binary EXIF IFD
//      directly — XMP-IPTC preset is the only metadata injection path.
//
//   2. Astrometric in fits: namespace — FITS keywords (OBJCTRA, OBJCTDEC,
//      RA, DEC, SITELAT, SITELONG, etc.) are the de facto standard for
//      astronomical image metadata. PixInsight's ImageSolver, AstroPixel-
//      Processor, and Siril all recognize these keywords. Using a dedicated
//      fits: namespace avoids collision with existing IPTC/EXIF properties.
//
//   3. Astrometric in avm: namespace — AVM 1.2 is the IAU/IVOA standard for
//      astronomical visualization metadata. Spatial.ReferenceValue carries
//      RA/Dec for WCS-aware applications. Dual encoding ensures both the
//      professional astronomy tools (via fits:) and outreach/visualization
//      tools (via avm:) can read the coordinates.
//
//   4. Weather in astro: namespace — there is no existing standard namespace
//      for observing conditions metadata. A custom namespace avoids polluting
//      recognized namespaces with non-standard properties.
//
// References:
//   - Adobe XMP Specification Part 1 (ISO 16684-1): XMP packet format
//   - Adobe XMP Specification Part 2 (ISO 16684-2): EXIF schema for XMP,
//     including GPSCoordinate type (§1.2.7.4) and GPS-related properties
//   - CIPA DC-008 (EXIF 2.32): GPS IFD tag definitions (§4.6.6)
//   - FITS Standard (NOST 100-2.0): keyword definitions
//     https://fits.gsfc.nasa.gov/fits_standard.html
//   - AVM 1.2 Standard: http://www.virtualastronomy.org/avm_metadata.php
//   - IPTC Photo Metadata Standard 2025.1: GPS-Latitude/Longitude/Altitude
//     XMP mapping to exif:GPSLatitude, exif:GPSLongitude, exif:GPSAltitude
//   - Nikon SDK MAID3 Type0031 §3.260.2: XMP-IPTC IPTCPresetDataSet format
//   - W3C RDF/XML Syntax: https://www.w3.org/TR/rdf-syntax-grammar/
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
using System;
using System.Globalization;
using System.Text;

namespace NikonCameraSettings.Utils {

    /// <summary>
    /// Static utility class that builds XMP XML payloads for different data
    /// categories (GPS, astrometric, weather). Each method returns a UTF-8
    /// string that fits within the Nikon SDK's 30 KB XMP data field in an
    /// IPTCPresetDataSet. The XML uses standard XMP packet wrappers and the
    /// appropriate namespace URIs so that desktop applications can read the
    /// metadata after images are downloaded from the camera.
    /// </summary>
    public static class XmpBuilder {

        // =====================================================================
        //  NAMESPACE CONSTANTS
        // =====================================================================

        // ---------------------------------------------------------------------------
        // The XMP EXIF namespace contains GPS-related properties. This is the
        // standard, universally recognized way to embed GPS in XMP metadata.
        // All major photo applications read exif:GPSLatitude et al. from XMP
        // and treat them identically to binary EXIF GPS IFD tags.
        // Reference: XMP Part 2, §2.27 "exif namespace"
        //            IPTC Photo Metadata Standard 2025.1 §12.10.4–12.10.7
        // ---------------------------------------------------------------------------
        private const string ExifNs = "http://ns.adobe.com/exif/1.0/";

        // ---------------------------------------------------------------------------
        // Dublin Core namespace — used only for a human-readable summary of
        // all embedded data. This is NOT the primary store for any data value;
        // it is a convenience for users browsing in photo catalog applications
        // (Adobe Bridge, Lightroom) where dc:description appears prominently.
        // Reference: XMP Part 1, §8.4 "Dublin Core namespace"
        // ---------------------------------------------------------------------------
        private const string DcNs = "http://purl.org/dc/elements/1.1/";

        // ---------------------------------------------------------------------------
        // Custom FITS namespace for astronomical keywords. Uses a namespace
        // URI that is clearly distinguishable from the official FITS standard
        // URI (which does not define an XMP binding). This namespace carries
        // FITS-format keyword values that PixInsight, AstroPixelProcessor, and
        // Siril can parse when reading XMP sidecar or embedded metadata.
        //
        // Keywords encoded: OBJECT, RA, DEC, OBJCTRA, OBJCTDEC, OBJCTALT,
        //   OBJCTAZ, RADESYS, EQUINOX, SITELAT, SITELONG, SITEELEV,
        //   DATE-OBS, AIRMASS, LST
        //
        // Reference: FITS Standard (NOST 100-2.0) §4.4.2.1 for keyword defs
        //            https://fits.gsfc.nasa.gov/fits_standard.html
        // ---------------------------------------------------------------------------
        private const string FitsNs = "http://fits.gsfc.nasa.gov/fits/1.0/";

        // ---------------------------------------------------------------------------
        // AVM (Astronomy Visualization Metadata) namespace. AVM 1.2 is the
        // IAU/IVOA standard for astronomical image metadata. WCS-aware
        // applications (WorldWide Telescope, Aladin) use Spatial.ReferenceValue,
        // Spatial.CoordinateFrame, etc. to overlay images on the sky.
        // Reference: http://www.virtualastronomy.org/avm_metadata.php
        // ---------------------------------------------------------------------------
        private const string AvmNs = "http://www.communicatingastronomy.org/avm/1.0/";

        // ---------------------------------------------------------------------------
        // Custom namespace for observing conditions / environmental data.
        // No existing standard namespace covers temperature, humidity, SQM,
        // pressure, etc. for astronomical observations, so we define our own.
        // ---------------------------------------------------------------------------
        private const string AstroNs = "http://astronomy.camera/1.0/";

        // =====================================================================
        //  PUBLIC API — Individual section builders
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Builds a complete XMP packet containing ONLY GPS location metadata.
        //
        // GPS data is stored in the standard exif: namespace properties:
        //   exif:GPSVersionID      → "2.3.0.0" (current EXIF GPS IFD version)
        //   exif:GPSLatitude       → "DD,MM.mmmmN" or "DD,MM.mmmmS"
        //   exif:GPSLongitude      → "DDD,MM.mmmmE" or "DDD,MM.mmmmW"
        //   exif:GPSAltitude       → rational "numerator/10" (meters)
        //   exif:GPSAltitudeRef    → "0" (above sea level) or "1" (below)
        //   exif:GPSMapDatum       → "WGS-84"
        //
        // These are machine-readable tags that photo software parses as GPS.
        // This method does NOT use dc:description for GPS storage.
        //
        // Reference: CIPA DC-008 §4.6.6 for EXIF GPS tag semantics
        //            XMP Part 2 §1.2.7.4 for XMP GPSCoordinate string format
        //            IPTC Photo Metadata Standard 2025.1 §12.10.4–12.10.7
        // ---------------------------------------------------------------------------
        public static string BuildGpsXmp(GpsData gps) {
            // Validate input to prevent constructing malformed XMP from null data
            if (gps == null) {
                throw new ArgumentNullException(nameof(gps), "GpsData must not be null.");
            }

            // Use a StringBuilder for efficient string concatenation of the XML
            StringBuilder sb = new StringBuilder(2048);

            // Write the standard XMP packet header and open the rdf:Description
            // with the exif: namespace declaration
            WritePacketHeader(sb);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "    <rdf:Description rdf:about=\"\" xmlns:exif=\"{0}\">", ExifNs));

            // Emit the GPS EXIF properties into the rdf:Description body
            WriteGpsSection(sb, gps);

            // Close the rdf:Description, rdf:RDF, x:xmpmeta, and xpacket
            sb.AppendLine("    </rdf:Description>");
            WritePacketFooter(sb);

            return sb.ToString();
        }

        // ---------------------------------------------------------------------------
        // Builds a complete XMP packet combining ALL available data categories
        // into a single payload. This is the primary method called by the
        // SetXmpIptcData sequence item. Each parameter may be null — only
        // sections with non-null, valid data are emitted.
        //
        // The resulting XMP packet contains up to four namespaced sections:
        //   1. exif: — GPS coordinates (observer location)
        //   2. fits: — FITS-standard astronomical keywords (for PixInsight/APP)
        //   3. avm:  — AVM spatial metadata (for WWT/Aladin visualization)
        //   4. astro: — Environmental/weather conditions
        //
        // Plus an optional dc:description with a human-readable summary.
        //
        // All sections share a single rdf:Description element with all
        // namespace declarations, because XMP best practice is to avoid
        // multiple rdf:Description blocks when possible (some older parsers
        // only read the first one).
        //
        // Reference: XMP Part 1 §7.4 "Combining XMP descriptions"
        //            Nikon SDK MAID3 Type0031 §3.260.2 "XMP Data Maximum 30KByte"
        // ---------------------------------------------------------------------------
        public static string BuildUnifiedXmp(GpsData gps, AstrometricData astro,
            WeatherData weather) {

            // At least one data source must be present
            bool hasGps = gps != null && gps.IsValid();
            bool hasAstro = astro != null && astro.HasAnyData();
            bool hasWeather = weather != null && weather.HasAnyData();

            if (!hasGps && !hasAstro && !hasWeather) {
                throw new ArgumentException(
                    "At least one data source (GPS, astrometric, or weather) " +
                    "must contain valid data.");
            }

            // Estimate capacity: GPS ~600B, astro ~1500B, weather ~800B, overhead ~500B
            StringBuilder sb = new StringBuilder(4096);

            // Write the standard XMP packet header
            WritePacketHeader(sb);

            // -----------------------------------------------------------------------
            // Open the rdf:Description with ALL namespace declarations.
            // Every namespace that might be used is declared here, even if the
            // corresponding section is null — unused namespace declarations are
            // harmless in XML and avoids conditional namespace emission complexity.
            // Reference: W3C RDF/XML Syntax §2.5 — namespace scoping rules
            // -----------------------------------------------------------------------
            sb.AppendLine("    <rdf:Description rdf:about=\"\"");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "        xmlns:exif=\"{0}\"", ExifNs));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "        xmlns:fits=\"{0}\"", FitsNs));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "        xmlns:avm=\"{0}\"", AvmNs));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "        xmlns:astro=\"{0}\"", AstroNs));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "        xmlns:dc=\"{0}\">", DcNs));

            // ===================================================================
            // SECTION 1: GPS COORDINATES (exif: namespace)
            // Machine-readable EXIF GPS tags — the standard way to embed GPS
            // in XMP. These are NOT stored in dc:description.
            // ===================================================================
            if (hasGps) {
                sb.AppendLine("      <!-- GPS location (EXIF standard tags) -->");
                WriteGpsSection(sb, gps);
            }

            // ===================================================================
            // SECTION 2: ASTROMETRIC DATA (fits: and avm: namespaces)
            // FITS keywords for PixInsight/APP + AVM for visualization tools.
            // ===================================================================
            if (hasAstro) {
                sb.AppendLine("      <!-- Astrometric data (FITS keywords + AVM) -->");
                WriteAstrometricSection(sb, astro, hasGps ? gps : null);
            }

            // ===================================================================
            // SECTION 3: WEATHER / ENVIRONMENTAL DATA (astro: namespace)
            // ===================================================================
            if (hasWeather) {
                sb.AppendLine("      <!-- Environmental / weather conditions -->");
                WriteWeatherSection(sb, weather);
            }

            // ===================================================================
            // SECTION 4: HUMAN-READABLE SUMMARY (dc:description)
            // This is purely supplementary — a convenience for catalog browsing.
            // All actual data values are in their proper machine-readable tags above.
            // ===================================================================
            sb.AppendLine("      <!-- Human-readable summary for catalog browsing -->");
            WriteDescriptionSummary(sb, hasGps ? gps : null,
                hasAstro ? astro : null, hasWeather ? weather : null);

            // Close the rdf:Description and packet
            sb.AppendLine("    </rdf:Description>");
            WritePacketFooter(sb);

            return sb.ToString();
        }

        // =====================================================================
        //  INTERNAL SECTION WRITERS — one per data category
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Writes GPS EXIF properties into an open rdf:Description body.
        // These are the standard machine-readable GPS tags that all photo
        // software recognizes. There is NO need to duplicate GPS data into
        // dc:description — the exif: properties ARE the GPS data.
        //
        // Format details:
        //   GPSLatitude/Longitude use the XMP GPSCoordinate format
        //   ("DD,MM.mmmmN" per XMP Part 2 §1.2.7.4), NOT DMS strings.
        //   The N/S/E/W cardinal is appended to the coordinate value itself;
        //   there is no separate GPSLatitudeRef tag in XMP (unlike binary EXIF
        //   where Tags 0x0001 and 0x0003 are separate).
        //
        //   GPSAltitude uses EXIF Rational format ("numerator/denominator").
        //   GPSAltitudeRef: "0" = above sea level, "1" = below sea level.
        //
        // Reference: CIPA DC-008 §4.6.6 Tags 0x0000–0x0012
        //            XMP Part 2 §1.2.7.4 GPSCoordinate type
        //            IPTC Photo Metadata Standard 2025.1 §12.10.4–12.10.7:
        //              GPS-Altitude      → exif:GPSAltitude
        //              GPS-Altitude Ref  → exif:GPSAltitudeRef
        //              GPS-Latitude      → exif:GPSLatitude
        //              GPS-Longitude     → exif:GPSLongitude
        // ---------------------------------------------------------------------------
        private static void WriteGpsSection(StringBuilder sb, GpsData gps) {
            // GPSVersionID: "2.3.0.0" is the current EXIF GPS IFD version
            // Reference: CIPA DC-008 §4.6.6 Tag 0x0000
            sb.AppendLine("      <exif:GPSVersionID>2.3.0.0</exif:GPSVersionID>");

            // GPSLatitude in XMP GPSCoordinate format: "DD,MM.mmmmN" or "DD,MM.mmmmS"
            // The cardinal direction is embedded in the value string itself, which is
            // the XMP convention (differs from binary EXIF where Ref is a separate tag).
            // Reference: XMP Part 2 §1.2.7.4; CIPA DC-008 §4.6.6 Tags 0x0001–0x0002
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <exif:GPSLatitude>{0}</exif:GPSLatitude>", gps.ToExifLatitude()));

            // GPSLongitude in XMP GPSCoordinate format: "DDD,MM.mmmmE" or "DDD,MM.mmmmW"
            // Reference: XMP Part 2 §1.2.7.4; CIPA DC-008 §4.6.6 Tags 0x0003–0x0004
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <exif:GPSLongitude>{0}</exif:GPSLongitude>", gps.ToExifLongitude()));

            // GPSAltitudeRef: 0 = above sea level, 1 = below sea level
            // Reference: CIPA DC-008 §4.6.6 Tag 0x0005
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <exif:GPSAltitudeRef>{0}</exif:GPSAltitudeRef>",
                gps.ToExifAltitudeRef()));

            // GPSAltitude as EXIF Rational (numerator/denominator) in meters
            // Reference: CIPA DC-008 §4.6.6 Tag 0x0006
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <exif:GPSAltitude>{0}</exif:GPSAltitude>", gps.ToExifAltitude()));

            // GPSMapDatum: WGS-84 is the standard used by GPS, NINA, and ASCOM
            // Reference: CIPA DC-008 §4.6.6 Tag 0x0012
            sb.AppendLine("      <exif:GPSMapDatum>WGS-84</exif:GPSMapDatum>");
        }

        // ---------------------------------------------------------------------------
        // Writes astrometric metadata using TWO complementary namespace encodings:
        //
        // A) fits: namespace — FITS-standard keywords for professional astronomy
        //    software (PixInsight ImageSolver, AstroPixelProcessor, Siril).
        //    Keywords follow FITS Standard (NOST 100-2.0) naming conventions:
        //
        //    Keyword       Format              Description
        //    ─────────     ──────────────────   ──────────────────────────────────
        //    OBJECT        Free-text            Target name (M31, NGC 7000, etc.)
        //    OBJCTRA       "HH MM SS.ss"        RA in sexagesimal (FITS §4.4.2.1)
        //    OBJCTDEC      "+DD MM SS.s"        Dec in sexagesimal (FITS §4.4.2.1)
        //    RA            Decimal degrees      RA as float (0–360°)
        //    DEC           Decimal degrees      Dec as float (-90° to +90°)
        //    OBJCTALT      Decimal degrees      Altitude above horizon
        //    OBJCTAZ       Decimal degrees      Azimuth from North
        //    RADESYS       "ICRS"               Reference system (IAU standard)
        //    EQUINOX       2000.0               Epoch of coordinates
        //    SITELAT       Decimal degrees      Observer latitude (+N/-S)
        //    SITELONG      Decimal degrees      Observer longitude (+E/-W)
        //    SITEELEV      Meters               Observer elevation above sea level
        //    AIRMASS       Dimensionless        Atmospheric path length (sec(z))
        //    LST           Decimal hours         Local Sidereal Time
        //
        // B) avm: namespace — AVM 1.2 Spatial metadata for visualization tools
        //    (WorldWide Telescope, Aladin Sky Atlas, ESASky).
        //    Spatial.ReferenceValue carries RA/Dec for WCS overlay.
        //
        // The dual encoding ensures maximum compatibility across the full range
        // of astronomical software, from research-grade to outreach tools.
        //
        // Reference: FITS Standard §4.4.2.1 for keyword format
        //            AVM 1.2 §4.1 for Spatial metadata
        //            PixInsight XISF spec for FITS keyword reading
        // ---------------------------------------------------------------------------
        private static void WriteAstrometricSection(StringBuilder sb,
            AstrometricData astro, GpsData gps) {

            // -----------------------------------------------------------------------
            // FITS KEYWORDS (fits: namespace)
            // -----------------------------------------------------------------------

            // OBJECT: The name of the celestial target being observed.
            // PixInsight reads this to label the image in its workspace.
            // Reference: FITS Standard §4.4.2.1 — OBJECT keyword
            if (!string.IsNullOrWhiteSpace(astro.ObjectName)) {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <fits:OBJECT>{0}</fits:OBJECT>",
                    EscapeXml(astro.ObjectName)));
            }

            // Equatorial coordinates — both sexagesimal (OBJCTRA/OBJCTDEC) and
            // decimal (RA/DEC) formats. PixInsight's ImageSolver tries OBJCTRA
            // first, then falls back to RA in decimal degrees.
            if (astro.HasEquatorialCoordinates()) {

                // OBJCTRA: Right Ascension in "HH MM SS.ss" sexagesimal format
                // This is the primary coordinate that PixInsight's plate solver reads.
                // Reference: FITS Standard §4.4.2.1 — OBJCTRA keyword
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <fits:OBJCTRA>{0}</fits:OBJCTRA>",
                    astro.ToFitsRightAscension()));

                // OBJCTDEC: Declination in "+DD MM SS.s" sexagesimal format
                // Reference: FITS Standard §4.4.2.1 — OBJCTDEC keyword
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <fits:OBJCTDEC>{0}</fits:OBJCTDEC>",
                    astro.ToFitsDeclination()));

                // RA: Right Ascension in decimal degrees (0–360°)
                // Backup numeric format for computational use and software that
                // prefers decimal over sexagesimal (e.g., astrometry.net).
                // Conversion: RA_degrees = RA_hours × 15
                // Reference: IAU convention — 1 hour of RA = 15° of arc
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <fits:RA>{0:F8}</fits:RA>",
                    astro.RightAscensionDegrees));

                // DEC: Declination in decimal degrees (-90° to +90°)
                // Reference: IAU convention — positive = north of celestial equator
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <fits:DEC>{0:F8}</fits:DEC>",
                    astro.DeclinationDegrees));

                // RADESYS: The celestial reference system for the coordinates.
                // ICRS (International Celestial Reference System) is the IAU
                // standard since 1998, superseding FK5. NINA/ASCOM mounts report
                // coordinates in J2000/ICRS by default.
                // Reference: IAU Resolution B2 (2006) — ICRS adoption
                sb.AppendLine("      <fits:RADESYS>ICRS</fits:RADESYS>");

                // EQUINOX: The epoch of the equatorial coordinate system.
                // J2000.0 corresponds to 2000 January 1.5 TT (JD 2451545.0).
                // Reference: FITS Standard — EQUINOX keyword
                sb.AppendLine("      <fits:EQUINOX>2000.0</fits:EQUINOX>");
            }

            // Horizontal coordinates (Alt/Az) — observer-relative position
            if (astro.HasHorizontalCoordinates()) {

                // OBJCTALT: Altitude in degrees above the observer's horizon
                // 0° = horizon, 90° = zenith. Used for airmass calculations.
                // Reference: FITS keyword convention (non-standard but widely used)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <fits:OBJCTALT>{0:F4}</fits:OBJCTALT>",
                    astro.AltitudeDegrees));

                // OBJCTAZ: Azimuth in degrees from true North, measured eastward
                // N=0°, E=90°, S=180°, W=270°
                // Reference: ASCOM ITelescopeV3.Azimuth convention
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <fits:OBJCTAZ>{0:F4}</fits:OBJCTAZ>",
                    astro.AzimuthDegrees));

                // AIRMASS: Atmospheric path length relative to zenith.
                // Computed as sec(z) where z is the zenith distance (90° - altitude).
                // Only valid when altitude > 0° (object above horizon). The simple
                // secant formula is accurate to ~1% for altitudes above 10°.
                // For very low altitudes, Hardie's formula or Kasten & Young would
                // be more precise, but sec(z) is the FITS convention.
                // Reference: Hardie (1962) "Astronomical Techniques" §6.3
                //            FITS keyword convention — AIRMASS
                if (astro.AltitudeDegrees > 0.0) {
                    // Convert altitude to zenith distance in radians for sec(z)
                    double zenithDistRad = (90.0 - astro.AltitudeDegrees) * Math.PI / 180.0;
                    // Airmass = 1/cos(zenith_distance) = sec(zenith_distance)
                    double airmass = 1.0 / Math.Cos(zenithDistRad);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "      <fits:AIRMASS>{0:F4}</fits:AIRMASS>", airmass));
                }
            }

            // Local Sidereal Time — useful for transit calculations
            if (!double.IsNaN(astro.SiderealTimeHours)) {
                // LST: Local Sidereal Time in decimal hours (0–24h)
                // When LST equals a target's RA, that target is at meridian transit.
                // Reference: Meeus "Astronomical Algorithms" §12
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <fits:LST>{0:F6}</fits:LST>", astro.SiderealTimeHours));
            }

            // Observer location in FITS keywords — these duplicate GPS exif: data
            // in a format that FITS-reading astronomical software expects.
            // SITELAT/SITELONG use decimal degrees (positive N/E).
            if (gps != null && gps.IsValid()) {
                // SITELAT: Observer geodetic latitude in decimal degrees
                // Reference: FITS keyword convention (used by PixInsight, MaxIm DL)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <fits:SITELAT>{0:F8}</fits:SITELAT>", gps.Latitude));

                // SITELONG: Observer geodetic longitude in decimal degrees
                // Positive = East, Negative = West (IAU convention)
                // Reference: FITS keyword convention
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <fits:SITELONG>{0:F8}</fits:SITELONG>", gps.Longitude));

                // SITEELEV: Observer elevation above sea level in meters
                // Reference: FITS keyword convention
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <fits:SITEELEV>{0:F2}</fits:SITEELEV>", gps.Elevation));
            }

            // -----------------------------------------------------------------------
            // AVM SPATIAL METADATA (avm: namespace)
            // Provides WCS (World Coordinate System) information for visualization
            // tools. This is the IAU/IVOA standard for astronomical image metadata.
            // Reference: AVM 1.2 §4.1 — Spatial metadata group
            // -----------------------------------------------------------------------
            if (astro.HasEquatorialCoordinates()) {
                // Spatial.ReferenceValue: RA and Dec as a comma-separated pair
                // in decimal degrees. AVM requires RA in degrees (0–360), not hours.
                // Reference: AVM 1.2 §4.1.5 — Spatial.ReferenceValue
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <avm:Spatial.ReferenceValue>{0:F8},{1:F8}</avm:Spatial.ReferenceValue>",
                    astro.RightAscensionDegrees, astro.DeclinationDegrees));

                // Spatial.CoordinateFrame: The celestial reference frame.
                // "ICRS" is the IAU standard, preferred over "FK5" since 1998.
                // Reference: AVM 1.2 §4.1.3 — Spatial.CoordinateFrame
                sb.AppendLine("      <avm:Spatial.CoordinateFrame>ICRS</avm:Spatial.CoordinateFrame>");

                // Spatial.Equinox: Epoch of the coordinate system.
                // Reference: AVM 1.2 §4.1.4 — Spatial.Equinox
                sb.AppendLine("      <avm:Spatial.Equinox>J2000</avm:Spatial.Equinox>");

                // Spatial.ReferencePixel: Marks where in the image the RA/Dec applies.
                // Without full WCS plate solving, we indicate center-of-frame as
                // best approximation. This is a hint — the actual plate solution
                // can refine it later in PixInsight or astrometry.net.
                // We don't emit Spatial.Scale or Spatial.Rotation since those require
                // plate solving that happens downstream, not at capture time.
                // Reference: AVM 1.2 §4.1.6 — Spatial.ReferencePixel
            }
        }

        // ---------------------------------------------------------------------------
        // Writes weather/environmental metadata using the custom astro: namespace.
        // Only non-NaN values are emitted, because NINA's weather mediator returns
        // NaN for properties not supported by the connected weather device.
        //
        // Properties encoded:
        //   astro:Temperature      — Ambient temperature in °C
        //   astro:Humidity         — Relative humidity as percentage (0–100)
        //   astro:DewPoint         — Dew point temperature in °C
        //   astro:Pressure         — Barometric pressure in hPa (millibars)
        //   astro:WindSpeed        — Wind speed in m/s
        //   astro:WindDirection    — Wind direction in degrees (0=N, 90=E)
        //   astro:SkyQuality       — Sky quality meter reading in mag/arcsec²
        //   astro:SkyBrightness    — Sky brightness in lux
        //   astro:SkyTemperature   — Sky/cloud sensor temperature in °C
        //   astro:CloudCover       — Cloud cover as percentage (0–100)
        //   astro:StarFWHM         — Seeing estimate in arcseconds
        //
        // Reference: ASCOM ObservingConditions interface for property definitions
        //            https://ascom-standards.org/Help/Developer/html/T_ASCOM_DeviceInterface_IObservingConditions.htm
        // ---------------------------------------------------------------------------
        private static void WriteWeatherSection(StringBuilder sb, WeatherData weather) {
            // Temperature in degrees Celsius — affects CCD dark current,
            // focuser thermal expansion, and atmospheric refraction calculations.
            // Also stored as FITS keyword TEMPERAT by downstream processors.
            WriteOptionalDouble(sb, "astro:Temperature", weather.Temperature, "F1");

            // Relative humidity (0–100%) — affects dew formation on optics.
            // When ambient temperature approaches dew point (gap <3°C),
            // condensation risk is high and dew heaters should activate.
            WriteOptionalDouble(sb, "astro:Humidity", weather.Humidity, "F1");

            // Dew point temperature in °C — critical for heater control decisions.
            // Computed from temperature and humidity by NINA's weather device driver.
            WriteOptionalDouble(sb, "astro:DewPoint", weather.DewPoint, "F1");

            // Barometric pressure in hPa (millibars) — affects atmospheric
            // refraction corrections that adjust telescope pointing near the horizon.
            // Standard sea-level pressure is ~1013.25 hPa.
            WriteOptionalDouble(sb, "astro:Pressure", weather.Pressure, "F1");

            // Wind speed in m/s — affects tracking/guiding quality.
            // Winds >8 m/s typically cause noticeable tracking errors.
            WriteOptionalDouble(sb, "astro:WindSpeed", weather.WindSpeed, "F1");

            // Wind direction in degrees from North (0=N, 90=E, 180=S, 270=W).
            // Useful for determining wind-shadow effects and dome slaving.
            WriteOptionalDouble(sb, "astro:WindDirection", weather.WindDirection, "F0");

            // Cloud cover percentage — 0% = clear, 100% = overcast.
            // Reported by cloud sensors (Boltwood, AAG CloudWatcher, etc.).
            WriteOptionalDouble(sb, "astro:CloudCover", weather.CloudCover, "F0");

            // Sky Quality Meter reading in magnitudes per square arcsecond.
            // Higher values = darker sky (less light pollution):
            //   ~22.0 = excellent dark site (Bortle 1–2)
            //   ~21.0 = good rural site (Bortle 3–4)
            //   ~19.5 = suburban (Bortle 5–6)
            //   ~18.0 = urban (Bortle 7–8)
            // Reference: Unihedron SQM — http://www.unihedron.com/projects/sqm/
            WriteOptionalDouble(sb, "astro:SkyQuality", weather.SkyQuality, "F2");

            // NOTE: The WeatherData model currently has 8 properties matching the
            // ASCOM IObservingConditions core set. If additional properties are
            // needed (SkyBrightness, SkyTemperature, StarFWHM), add them to the
            // WeatherData model and constructor first, then uncomment here:
            //
            // WriteOptionalDouble(sb, "astro:SkyBrightness", weather.SkyBrightness, "F4");
            // WriteOptionalDouble(sb, "astro:SkyTemperature", weather.SkyTemperature, "F1");
            // WriteOptionalDouble(sb, "astro:StarFWHM", weather.StarFWHM, "F2");
        }

        // ---------------------------------------------------------------------------
        // Writes a dc:description element containing a human-readable summary of
        // all embedded metadata. This is purely for convenience when browsing
        // images in catalog applications (Lightroom, Bridge). The actual data
        // values are in their respective machine-readable tags above.
        // ---------------------------------------------------------------------------
        private static void WriteDescriptionSummary(StringBuilder sb,
            GpsData gps, AstrometricData astro, WeatherData weather) {

            // Build a concise summary string
            StringBuilder desc = new StringBuilder();

            // Include GPS summary if available
            if (gps != null) {
                desc.Append(gps.ToString());
            }

            // Include astrometric summary if available (RA/Dec, target name)
            if (astro != null && astro.HasAnyData()) {
                if (desc.Length > 0) desc.Append(" | ");
                desc.Append(astro.ToString());
            }

            // Include weather summary if available (temperature, SQM)
            if (weather != null && weather.HasAnyData()) {
                if (desc.Length > 0) desc.Append(" | ");
                desc.Append(weather.ToString());
            }

            // Only emit the dc:description if we have content
            if (desc.Length > 0) {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <dc:description>{0}</dc:description>",
                    EscapeXml(desc.ToString())));
            }
        }

        // =====================================================================
        //  INTERNAL HELPERS — XMP packet structure
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Writes the standard XMP packet header: the <?xpacket begin> processing
        // instruction, the x:xmpmeta wrapper, and the rdf:RDF container element.
        // These three elements are mandatory for all valid XMP packets.
        // Reference: XMP Part 1, §7.3.1 "XMP Packet Header"
        //            XMP Part 1, §7.3.2 "XMP Packet Body"
        // ---------------------------------------------------------------------------
        private static void WritePacketHeader(StringBuilder sb) {
            // The 'begin' attribute contains the UTF-8 BOM (U+FEFF) and the 'id'
            // is the W3C-defined XMP packet identifier string.
            sb.AppendLine("<?xpacket begin=\"\xEF\xBB\xBF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");

            // x:xmpmeta root element wraps all XMP content
            sb.AppendLine("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">");

            // rdf:RDF container holds all rdf:Description elements
            sb.AppendLine("  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        }

        // ---------------------------------------------------------------------------
        // Closes the rdf:RDF, x:xmpmeta, and writes the XMP packet trailer.
        // The 'end="w"' attribute marks the packet as writable (updatable).
        // Reference: XMP Part 1, §7.3.3 "XMP Packet Trailer"
        // ---------------------------------------------------------------------------
        private static void WritePacketFooter(StringBuilder sb) {
            sb.AppendLine("  </rdf:RDF>");
            sb.AppendLine("</x:xmpmeta>");
            // The 'w' end marker indicates the packet can be updated in-place
            sb.Append("<?xpacket end=\"w\"?>");
        }

        // ---------------------------------------------------------------------------
        // Conditionally writes a single XMP property element for a double value.
        // Only emits the element if the value is not NaN (i.e., the data is
        // available from the connected device). This avoids cluttering the XMP
        // payload with empty or placeholder elements.
        // ---------------------------------------------------------------------------
        private static void WriteOptionalDouble(StringBuilder sb, string tagName,
            double value, string format) {
            // Skip NaN values — these indicate "not supported" or "not available"
            // from the NINA weather/telescope mediator
            if (double.IsNaN(value)) return;

            // Emit the property element with InvariantCulture to ensure decimal
            // point (not comma) regardless of the host system's locale
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <{0}>{1}</{0}>", tagName, value.ToString(format,
                    CultureInfo.InvariantCulture)));
        }

        // ---------------------------------------------------------------------------
        // Escapes XML special characters in string values to prevent malformed XML.
        // Required for user-supplied strings like ObjectName or Description that
        // could contain &, <, >, or quote characters.
        // Reference: XML 1.0 §2.4 — Character Data and Markup
        // ---------------------------------------------------------------------------
        private static string EscapeXml(string value) {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            // Replace in specific order: & must be first to avoid double-escaping
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        // =====================================================================
        //  PUBLIC UTILITIES
        // =====================================================================

        // ---------------------------------------------------------------------------
        // Returns the UTF-8 byte count of an XMP string. Used to validate that
        // the payload fits within the Nikon SDK's 30 KB limit for the XMP data
        // field in an IPTCPresetDataSet.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "XMP Data Maximum 30KByte"
        // ---------------------------------------------------------------------------
        public static int GetXmpByteCount(string xmpXml) {
            // Guard against null input
            if (string.IsNullOrEmpty(xmpXml)) return 0;
            // Calculate UTF-8 byte length plus null terminator required by SDK
            return Encoding.UTF8.GetByteCount(xmpXml) + 1;
        }

        // ---------------------------------------------------------------------------
        // Maximum size of the XMP data field in bytes, as documented in
        // Nikon SDK MAID3 Type0031 §3.260.2 for the IPTCPresetDataSet.
        // ---------------------------------------------------------------------------
        public const int MaxXmpDataBytes = 30720;
    }
}
