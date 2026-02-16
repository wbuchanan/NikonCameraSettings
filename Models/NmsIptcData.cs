// *****************************************************************************
// File: Models/NmsIptcData.cs
// Purpose: Immutable data model representing the 14 standard NMS-IPTC (Nikon
//          Metadata System IPTC) fields that can be written to a Nikon camera's
//          IPTC preset slots 1-10 via the kNkMAIDCapability_IPTCPresetInfo SDK
//          capability. Each field is validated against the maximum byte length
//          defined in the Nikon SDK MAID3 Type0031 §3.260.2 specification.
//
//          NMS-IPTC is the camera's native metadata format for presets 1-10.
//          Unlike XMP-IPTC (presets 11-13) which uses a single XML blob, NMS-IPTC
//          stores each metadata field as an individual AUINT8 (Aligned UINT8)
//          element in the binary IPTCPresetDataSet structure.
//
// Field Specifications (from Nikon SDK MAID3 Type0031 §3.260.2):
//   Field          Encoding  Max Bytes (excl. NULL)
//   ─────────────  ────────  ─────────────────────
//   Caption        UTF-8     2000
//   EventID        UTF-8     64
//   Headline       UTF-8     256
//   ObjectName     UTF-8     256
//   City           UTF-8     256
//   State          UTF-8     256
//   Country        UTF-8     256
//   Category       ASCII     3 (v2.00) / 256 (v2.01)
//   SuppCat        UTF-8     256
//   Byline         UTF-8     256
//   BylineTitle    UTF-8     256
//   WriterEditor   UTF-8     256
//   Credit         UTF-8     256
//   Source         UTF-8     256
//
// References:
//   - Nikon SDK MAID3 Type0031 §3.260.2: IPTCPresetDataSet NMS-IPTC layout
//   - IPTC IIM (Information Interchange Model) Standard Rev 4.2: field semantics
//   - nikoncswrapper NikonNativeEnums.cs: eNkMAIDIPTCPresetInfo (presets 1-10)
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
using System.Text;

namespace NikonCameraSettings.Models {

    /// <summary>
    /// Immutable snapshot of the 14 standard NMS-IPTC metadata fields. Each
    /// property corresponds to one AUINT8 element in the Nikon SDK's
    /// IPTCPresetDataSet structure for preset slots 1-10.
    ///
    /// The class enforces maximum byte lengths at construction time by
    /// truncating oversized values to prevent SDK InvalidData errors.
    /// All string properties are guaranteed to be non-null (empty string
    /// is used for unset fields, which serializes as a zero-length AUINT8).
    /// </summary>
    public sealed class NmsIptcData {

        // ---------------------------------------------------------------------------
        // Maximum byte counts for each field as documented in Nikon SDK MAID3
        // Type0031 §3.260.2. These are the maximum data bytes EXCLUDING the
        // null terminator. The camera firmware rejects data exceeding these limits.
        //
        // Note: Category has two limits depending on firmware version.
        //   [Z 8]     DatasetVersion 0x00C8 (v2.00): max 3 ASCII bytes
        //   [Z 8_FU1] DatasetVersion 0x00C9 (v2.01): max 256 bytes
        // We use the v2.00 limit (3 bytes) for maximum compatibility.
        // ---------------------------------------------------------------------------
        public const int MaxCaptionBytes = 2000;
        public const int MaxEventIdBytes = 64;
        public const int MaxHeadlineBytes = 256;
        public const int MaxObjectNameBytes = 256;
        public const int MaxCityBytes = 256;
        public const int MaxStateBytes = 256;
        public const int MaxCountryBytes = 256;
        // Category uses the conservative v2.00 limit for broadest camera support
        public const int MaxCategoryBytes = 3;
        public const int MaxSuppCatBytes = 256;
        public const int MaxBylineBytes = 256;
        public const int MaxBylineTitleBytes = 256;
        public const int MaxWriterEditorBytes = 256;
        public const int MaxCreditBytes = 256;
        public const int MaxSourceBytes = 256;

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:120 — Caption/Abstract: A textual description of the content.
        // In astrophotography, this is typically used for a description of the
        // imaging session, target name, or observing conditions summary.
        // Encoding: UTF-8, max 2000 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Caption"
        // ---------------------------------------------------------------------------
        public string Caption { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:12 — Event Identifier: A unique identifier for the event
        // that the image is associated with. Astrophotographers may use this for
        // an imaging session ID or observing run identifier.
        // Encoding: UTF-8, max 64 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "EventID"
        // ---------------------------------------------------------------------------
        public string EventId { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:105 — Headline: A brief synopsis or summary of the content.
        // For astrophotography, this could be the target designation (e.g. "M42")
        // or a short description of the captured object.
        // Encoding: UTF-8, max 256 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Headline"
        // ---------------------------------------------------------------------------
        public string Headline { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:05 — Object Name (Title): The formal name of the content.
        // This is the primary title field used by most photo management software.
        // Encoding: UTF-8, max 256 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "ObjectName"
        // ---------------------------------------------------------------------------
        public string ObjectName { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:90 — City: The city where the image was created.
        // For astrophotography, this is the nearest city to the observing site.
        // Encoding: UTF-8, max 256 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "City"
        // ---------------------------------------------------------------------------
        public string City { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:95 — Province/State: The state or province where the image
        // was created. Used to identify the observing site's administrative region.
        // Encoding: UTF-8, max 256 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "State"
        // ---------------------------------------------------------------------------
        public string State { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:101 — Country/Primary Location Name: The country where the
        // image was created.
        // Encoding: UTF-8, max 256 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Country"
        // ---------------------------------------------------------------------------
        public string Country { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:15 — Category: A three-character code identifying the subject
        // category. For astrophotography, common codes include "SCI" (Science) or
        // "ENV" (Environment). Limited to 3 ASCII bytes on v2.00 firmware.
        // Encoding: ASCII (not UTF-8), max 3 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Category"
        // ---------------------------------------------------------------------------
        public string Category { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:20 — Supplemental Categories: Additional category descriptors
        // beyond the primary 3-character category code. This allows free-text
        // sub-categorization (e.g., "Deep Sky", "Planetary", "Widefield").
        // Encoding: UTF-8, max 256 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "SuppCat"
        // ---------------------------------------------------------------------------
        public string SuppCat { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:80 — Byline (Creator/Author): The name of the photographer
        // or creator of the image.
        // Encoding: UTF-8, max 256 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Byline"
        // ---------------------------------------------------------------------------
        public string Byline { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:85 — Byline Title: The job title or role of the creator.
        // For example, "Astrophotographer", "Observatory Director", etc.
        // Encoding: UTF-8, max 256 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "BylineTitle"
        // ---------------------------------------------------------------------------
        public string BylineTitle { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:122 — Writer/Editor: The name of the person who wrote or
        // edited the caption/description content.
        // Encoding: UTF-8, max 256 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "WriterEditor"
        // ---------------------------------------------------------------------------
        public string WriterEditor { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:110 — Credit: The provider or original owner of the image.
        // This may be the photographer's name, organization, or agency.
        // Encoding: UTF-8, max 256 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Credit"
        // ---------------------------------------------------------------------------
        public string Credit { get; }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:115 — Source: The original source from which the image was
        // obtained. For astrophotography, this might be the telescope/observatory
        // name or the imaging setup description.
        // Encoding: UTF-8, max 256 bytes.
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Source"
        // ---------------------------------------------------------------------------
        public string Source { get; }

        // ---------------------------------------------------------------------------
        // Constructs a new NmsIptcData with all 14 fields. Each value is sanitized:
        //   - Null values are replaced with empty strings
        //   - Values exceeding their maximum byte count are truncated
        //
        // The truncation uses TruncateToMaxBytes() which is encoding-aware to
        // avoid splitting multi-byte UTF-8 characters at the boundary.
        //
        // Parameters are ordered to match the binary layout in the SDK's
        // IPTCPresetDataSet (§3.260.2), which simplifies the marshaling code.
        // ---------------------------------------------------------------------------
        public NmsIptcData(
            string caption = "",
            string eventId = "",
            string headline = "",
            string objectName = "",
            string city = "",
            string state = "",
            string country = "",
            string category = "",
            string suppCat = "",
            string byline = "",
            string bylineTitle = "",
            string writerEditor = "",
            string credit = "",
            string source = "") {

            // Sanitize and truncate each field to its maximum allowed byte length
            Caption = TruncateToMaxBytes(caption ?? "", MaxCaptionBytes, Encoding.UTF8);
            EventId = TruncateToMaxBytes(eventId ?? "", MaxEventIdBytes, Encoding.UTF8);
            Headline = TruncateToMaxBytes(headline ?? "", MaxHeadlineBytes, Encoding.UTF8);
            ObjectName = TruncateToMaxBytes(objectName ?? "", MaxObjectNameBytes, Encoding.UTF8);
            City = TruncateToMaxBytes(city ?? "", MaxCityBytes, Encoding.UTF8);
            State = TruncateToMaxBytes(state ?? "", MaxStateBytes, Encoding.UTF8);
            Country = TruncateToMaxBytes(country ?? "", MaxCountryBytes, Encoding.UTF8);
            // Category is ASCII-only per the SDK spec; strip non-ASCII characters first
            Category = TruncateToMaxBytes(StripNonAscii(category ?? ""), MaxCategoryBytes, Encoding.ASCII);
            SuppCat = TruncateToMaxBytes(suppCat ?? "", MaxSuppCatBytes, Encoding.UTF8);
            Byline = TruncateToMaxBytes(byline ?? "", MaxBylineBytes, Encoding.UTF8);
            BylineTitle = TruncateToMaxBytes(bylineTitle ?? "", MaxBylineTitleBytes, Encoding.UTF8);
            WriterEditor = TruncateToMaxBytes(writerEditor ?? "", MaxWriterEditorBytes, Encoding.UTF8);
            Credit = TruncateToMaxBytes(credit ?? "", MaxCreditBytes, Encoding.UTF8);
            Source = TruncateToMaxBytes(source ?? "", MaxSourceBytes, Encoding.UTF8);
        }

        // ---------------------------------------------------------------------------
        // Checks whether at least one field has content. Returns true if any field
        // is non-empty, indicating there is meaningful data to write to the camera.
        // An NmsIptcData with all empty fields would produce a valid but pointless
        // IPTC preset — this method lets callers skip the write in that case.
        // ---------------------------------------------------------------------------
        public bool HasContent() {
            // Check each field — return true as soon as we find non-empty content
            return !string.IsNullOrEmpty(Caption)
                || !string.IsNullOrEmpty(EventId)
                || !string.IsNullOrEmpty(Headline)
                || !string.IsNullOrEmpty(ObjectName)
                || !string.IsNullOrEmpty(City)
                || !string.IsNullOrEmpty(State)
                || !string.IsNullOrEmpty(Country)
                || !string.IsNullOrEmpty(Category)
                || !string.IsNullOrEmpty(SuppCat)
                || !string.IsNullOrEmpty(Byline)
                || !string.IsNullOrEmpty(BylineTitle)
                || !string.IsNullOrEmpty(WriterEditor)
                || !string.IsNullOrEmpty(Credit)
                || !string.IsNullOrEmpty(Source);
        }

        // ---------------------------------------------------------------------------
        // Returns a human-readable summary of the populated fields. Only non-empty
        // fields are included to keep the output concise. This is used for logging
        // and for the sequence item's UI display.
        // ---------------------------------------------------------------------------
        public override string ToString() {
            // Use a StringBuilder to build the summary efficiently
            StringBuilder sb = new StringBuilder();

            // Append each non-empty field with its label
            AppendIfNotEmpty(sb, "Caption", Caption);
            AppendIfNotEmpty(sb, "EventID", EventId);
            AppendIfNotEmpty(sb, "Headline", Headline);
            AppendIfNotEmpty(sb, "ObjectName", ObjectName);
            AppendIfNotEmpty(sb, "City", City);
            AppendIfNotEmpty(sb, "State", State);
            AppendIfNotEmpty(sb, "Country", Country);
            AppendIfNotEmpty(sb, "Category", Category);
            AppendIfNotEmpty(sb, "SuppCat", SuppCat);
            AppendIfNotEmpty(sb, "Byline", Byline);
            AppendIfNotEmpty(sb, "BylineTitle", BylineTitle);
            AppendIfNotEmpty(sb, "Writer/Editor", WriterEditor);
            AppendIfNotEmpty(sb, "Credit", Credit);
            AppendIfNotEmpty(sb, "Source", Source);

            // Return a "no data" indicator if all fields are empty
            return sb.Length > 0 ? sb.ToString().TrimEnd(',', ' ') : "(no data)";
        }

        // ---------------------------------------------------------------------------
        // Helper: appends "label=value, " to the StringBuilder if value is non-empty.
        // Used by ToString() to build a compact summary of populated fields.
        // ---------------------------------------------------------------------------
        private static void AppendIfNotEmpty(StringBuilder sb, string label, string value) {
            // Only append fields that contain actual content
            if (!string.IsNullOrEmpty(value)) {
                // Truncate displayed value to 30 chars to keep summaries manageable
                string display = value.Length > 30 ? value.Substring(0, 30) + "..." : value;
                sb.Append($"{label}={display}, ");
            }
        }

        // ---------------------------------------------------------------------------
        // Truncates a string so its encoded byte representation does not exceed
        // maxBytes. This is encoding-aware: for UTF-8, it avoids splitting
        // multi-byte characters by decrementing one character at a time until
        // the encoded result fits within the limit.
        //
        // This approach is necessary because UTF-8 characters can be 1-4 bytes,
        // so simply truncating by character count could still exceed the byte limit
        // (e.g., a string of 256 emoji characters would be ~1024 UTF-8 bytes).
        //
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 — "Maximum NNNByte+NULL"
        //   means the data portion can be at most NNN bytes, then +1 for null.
        // ---------------------------------------------------------------------------
        private static string TruncateToMaxBytes(string value, int maxBytes, Encoding encoding) {
            // Empty strings need no truncation
            if (string.IsNullOrEmpty(value)) return value;

            // Fast path: if the string fits within the byte limit, return as-is
            if (encoding.GetByteCount(value) <= maxBytes) return value;

            // Slow path: remove characters from the end until the byte count fits.
            // We iterate backward because removing from the end preserves the
            // beginning of the string, which is typically the most meaningful part.
            int charCount = value.Length;
            while (charCount > 0 && encoding.GetByteCount(value, 0, charCount) > maxBytes) {
                charCount--;
            }

            // Return the truncated substring
            return value.Substring(0, charCount);
        }

        // ---------------------------------------------------------------------------
        // Strips non-ASCII characters from a string. The Category field in the
        // Nikon SDK's NMS-IPTC format is specified as ASCII-only (not UTF-8).
        // Reference: Nikon SDK MAID3 Type0031 §3.260.2 "Category: ASCII"
        // ---------------------------------------------------------------------------
        private static string StripNonAscii(string value) {
            // Empty strings need no processing
            if (string.IsNullOrEmpty(value)) return value;

            // Build a new string containing only ASCII characters (0x00-0x7F)
            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value) {
                // ASCII characters have code points 0-127
                if (c <= 127) {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------------------
        // Value equality: two NmsIptcData instances are equal if all 14 fields
        // are identical. Used by change tracking to avoid redundant SDK writes.
        // ---------------------------------------------------------------------------
        public override bool Equals(object obj) {
            // Reference equality check (same instance)
            if (ReferenceEquals(this, obj)) return true;
            // Type check
            if (obj is not NmsIptcData other) return false;

            // Compare all 14 fields using ordinal string comparison
            return string.Equals(Caption, other.Caption, StringComparison.Ordinal)
                && string.Equals(EventId, other.EventId, StringComparison.Ordinal)
                && string.Equals(Headline, other.Headline, StringComparison.Ordinal)
                && string.Equals(ObjectName, other.ObjectName, StringComparison.Ordinal)
                && string.Equals(City, other.City, StringComparison.Ordinal)
                && string.Equals(State, other.State, StringComparison.Ordinal)
                && string.Equals(Country, other.Country, StringComparison.Ordinal)
                && string.Equals(Category, other.Category, StringComparison.Ordinal)
                && string.Equals(SuppCat, other.SuppCat, StringComparison.Ordinal)
                && string.Equals(Byline, other.Byline, StringComparison.Ordinal)
                && string.Equals(BylineTitle, other.BylineTitle, StringComparison.Ordinal)
                && string.Equals(WriterEditor, other.WriterEditor, StringComparison.Ordinal)
                && string.Equals(Credit, other.Credit, StringComparison.Ordinal)
                && string.Equals(Source, other.Source, StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------------------
        // Hash code consistent with the Equals override. Combines hashes of all
        // 14 fields using a simple XOR-shift pattern for reasonable distribution.
        // Reference: https://learn.microsoft.com/dotnet/api/system.object.gethashcode
        // ---------------------------------------------------------------------------
        public override int GetHashCode() {
            // Use unchecked to allow integer overflow without exceptions
            unchecked {
                // Start with a prime number seed for better hash distribution
                int hash = 17;
                // Combine each field's hash code using multiply-and-add
                hash = hash * 31 + (Caption?.GetHashCode() ?? 0);
                hash = hash * 31 + (EventId?.GetHashCode() ?? 0);
                hash = hash * 31 + (Headline?.GetHashCode() ?? 0);
                hash = hash * 31 + (ObjectName?.GetHashCode() ?? 0);
                hash = hash * 31 + (City?.GetHashCode() ?? 0);
                hash = hash * 31 + (State?.GetHashCode() ?? 0);
                hash = hash * 31 + (Country?.GetHashCode() ?? 0);
                hash = hash * 31 + (Category?.GetHashCode() ?? 0);
                hash = hash * 31 + (SuppCat?.GetHashCode() ?? 0);
                hash = hash * 31 + (Byline?.GetHashCode() ?? 0);
                hash = hash * 31 + (BylineTitle?.GetHashCode() ?? 0);
                hash = hash * 31 + (WriterEditor?.GetHashCode() ?? 0);
                hash = hash * 31 + (Credit?.GetHashCode() ?? 0);
                hash = hash * 31 + (Source?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
