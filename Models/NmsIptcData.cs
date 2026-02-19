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

    // Eventually this all needs to get moved into Xmp.
    public sealed class NmsIptcData {

        public const int MaxCaptionBytes = 2000;
        public const int MaxAltText = -1;
        public const int MaxCityBytes = 32;
        public const int MaxCopyrightBytes = 128;
        public const int MaxCountryBytes = -1;
        public const int MaxCountryCodeBytes = 12;
        public const int MaxObjectNameBytes = 64;
        public const int MaxCreatorBytes = -1;
        public const int MaxEventNameBytes = -1;
        public const int MaxHeadlineBytes = 256;
        public const int MaxStateBytes = 32;
        public const int MaxJobTitleBytes = 32;
        public const int MaxCaptionWriterBytes = 32;
        public const int MaxCreditLineBytes = 32;
        public const int MaxSourceBytes = 32;
        public const string cn = "©";

        public string Caption { get; }
        public string AltText { get; }
        public string City { get; }
        private string _copyright;
        public string Copyright { 
            get {
                // Convenience so end users don't need to add the copyright symbol or year.
                return cn + DateTime.Now.Year.ToString() + " " + _copyright;
            }
            set {
                _copyright = value;
            }
        }
        public string Country { get; }
        public string CountryCode { get; }
        public string ObjectName { get; }
        public string Creator { get; }
        public string EventName { get; }
        public string Headline { get; }
        public string State { get; }
        public string JobTitle { get; }
        public string CaptionWriter { get; }
        public string CreditLine { get; }
        public string Source { get; }
        
        public NmsIptcData(
            string caption = "",
            string altText = "",
            string city = "",
            string copyright = "",
            string country = "",
            string countryCode = "",
            string objectName = "",
            string creator = "",
            string eventName = "",
            string headline = "",
            string state = "",
            string jobTitle = "",
            string captionWriter = "",
            string creditLine = "",
            string source = "") {

            // Sanitize and truncate each field to its maximum allowed byte length
            Caption = TruncateToMaxBytes(caption ?? "", MaxCaptionBytes, Encoding.UTF8);
            AltText = TruncateToMaxBytes(altText ?? "", MaxAltText, Encoding.UTF8);
            City = TruncateToMaxBytes(city ?? "", MaxCityBytes, Encoding.UTF8);
            Copyright = TruncateToMaxBytes(copyright ?? "", MaxCopyrightBytes, Encoding.UTF8);
            Country = TruncateToMaxBytes(country ?? "", MaxCountryBytes, Encoding.UTF8);
            CountryCode = TruncateToMaxBytes(countryCode ?? "", MaxCountryCodeBytes, Encoding.UTF8);
            ObjectName = TruncateToMaxBytes(objectName ?? "", MaxObjectNameBytes, Encoding.UTF8);
            Creator = TruncateToMaxBytes(creator ?? "", MaxCreatorBytes, Encoding.UTF8);
            EventName = TruncateToMaxBytes(eventName ?? "", MaxEventNameBytes, Encoding.UTF8);
            Headline = TruncateToMaxBytes(headline ?? "", MaxHeadlineBytes, Encoding.UTF8);
            State = TruncateToMaxBytes(state ?? "", MaxStateBytes, Encoding.UTF8);
            JobTitle = TruncateToMaxBytes(jobTitle ?? "", MaxJobTitleBytes, Encoding.UTF8);
            CaptionWriter = TruncateToMaxBytes(captionWriter ?? "", MaxCaptionWriterBytes, Encoding.UTF8);
            CreditLine = TruncateToMaxBytes(creditLine ?? "", MaxCreditLineBytes, Encoding.UTF8);
            Source = TruncateToMaxBytes(source ?? "", MaxSourceBytes, Encoding.UTF8);
        }

        public bool HasContent() {
            // Check each field — return true as soon as we find non-empty content
            return !string.IsNullOrEmpty(Caption) || !string.IsNullOrEmpty(AltText) ||
                   !string.IsNullOrEmpty(City) || !string.IsNullOrEmpty(Copyright) ||
                   !string.IsNullOrEmpty(Country) || !string.IsNullOrEmpty(CountryCode) ||
                   !string.IsNullOrEmpty(ObjectName) || !string.IsNullOrEmpty(Creator) ||
                   !string.IsNullOrEmpty(EventName) || !string.IsNullOrEmpty(Headline) ||
                   !string.IsNullOrEmpty(State) || !string.IsNullOrEmpty(JobTitle) ||
                   !string.IsNullOrEmpty(CaptionWriter) || !string.IsNullOrEmpty(CreditLine) ||
                   !string.IsNullOrEmpty(Source);
        }

        public override string ToString() {
            StringBuilder sb = new StringBuilder();
            AppendIfNotEmpty(sb, "Caption: ", Caption);
            AppendIfNotEmpty(sb, "AltText: ", AltText);
            AppendIfNotEmpty(sb, "Copyright Notice: ", Copyright);
            AppendIfNotEmpty(sb, "EventName: ", EventName);
            AppendIfNotEmpty(sb, "Headline: ", Headline);
            AppendIfNotEmpty(sb, "ObjectName: ", ObjectName);
            AppendIfNotEmpty(sb, "City: ", City);
            AppendIfNotEmpty(sb, "State: ", State);
            AppendIfNotEmpty(sb, "Country: ", Country);
            AppendIfNotEmpty(sb, "Country Code: ", CountryCode);
            AppendIfNotEmpty(sb, "Caption Writer: ", CaptionWriter);
            AppendIfNotEmpty(sb, "Credit Line: ", CreditLine);
            AppendIfNotEmpty(sb, "Source: ", Source);
            return sb.Length > 0 ? sb.ToString().TrimEnd(',', ' ') : "(no data)";
        }

        private static void AppendIfNotEmpty(StringBuilder sb, string label, string value) {
            if (!string.IsNullOrEmpty(value)) {
                string display = value.Length > 30 ? value.Substring(0, 30) + "..." : value;
                sb.Append($"{label}={display}, ");
            }
        }

        private static string TruncateToMaxBytes(string value, int maxBytes, Encoding encoding) {
            if (string.IsNullOrEmpty(value)) return value;
            if (maxBytes < 0) return value;
            if (encoding.GetByteCount(value) <= maxBytes) return value;
            int charCount = value.Length;
            while (charCount > 0 && encoding.GetByteCount(value, 0, charCount) > maxBytes) {
                charCount--;
            }
            return value.Substring(0, charCount);
        }

        public override bool Equals(object obj) {
            if (ReferenceEquals(this, obj)) return true;
            if (obj is not NmsIptcData other) return false;

            // Compare all 14 fields using ordinal string comparison
            return  string.Equals(Caption, other.Caption, StringComparison.Ordinal) &&
                    string.Equals(AltText, other.AltText, StringComparison.Ordinal) &&
                    string.Equals(City, other.City, StringComparison.Ordinal) &&
                    string.Equals(Copyright, other.Copyright, StringComparison.Ordinal) &&
                    string.Equals(Country, other.Country, StringComparison.Ordinal) &&
                    string.Equals(CountryCode, other.CountryCode, StringComparison.Ordinal) &&
                    string.Equals(ObjectName, other.ObjectName, StringComparison.Ordinal) &&
                    string.Equals(Creator, other.Creator, StringComparison.Ordinal) && 
                    string.Equals(EventName, other.EventName, StringComparison.Ordinal) &&
                    string.Equals(Headline, other.Headline, StringComparison.Ordinal) && 
                    string.Equals(State, other.State, StringComparison.Ordinal) &&
                    string.Equals(JobTitle, other.JobTitle, StringComparison.Ordinal) &&
                    string.Equals(CaptionWriter, other.CaptionWriter, StringComparison.Ordinal) &&
                    string.Equals(CreditLine, other.CreditLine, StringComparison.Ordinal) &&
                    string.Equals(Source, other.Source, StringComparison.Ordinal);
        }

        public override int GetHashCode() {
            unchecked {
                int hash = 17;
                hash = hash * 31 + (Caption?.GetHashCode() ?? 0);
                hash = hash * 31 + (AltText?.GetHashCode() ?? 0);
                hash = hash * 31 + (City?.GetHashCode() ?? 0);
                hash = hash * 31 + (Copyright?.GetHashCode() ?? 0);
                hash = hash * 31 + (Country?.GetHashCode() ?? 0);
                hash = hash * 31 + (CountryCode?.GetHashCode() ?? 0);
                hash = hash * 31 + (ObjectName?.GetHashCode() ?? 0);
                hash = hash * 31 + (Creator?.GetHashCode() ?? 0);
                hash = hash * 31 + (EventName?.GetHashCode() ?? 0);
                hash = hash * 31 + (Headline?.GetHashCode() ?? 0);  
                hash = hash * 31 + (State?.GetHashCode() ?? 0);
                hash = hash * 31 + (JobTitle?.GetHashCode() ?? 0);  
                hash = hash * 31 + (CaptionWriter?.GetHashCode() ?? 0);
                hash = hash * 31 + (CreditLine?.GetHashCode() ?? 0);
                hash = hash * 31 + (Source?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public bool HasAnyData() {
            return  !string.IsNullOrEmpty(Caption) || !string.IsNullOrEmpty(AltText) ||
                    !string.IsNullOrEmpty(City) || !string.IsNullOrEmpty(Copyright) ||
                    !string.IsNullOrEmpty(Country) || !string.IsNullOrEmpty(CountryCode) ||
                    !string.IsNullOrEmpty(ObjectName) || !string.IsNullOrEmpty(Creator) ||
                    !string.IsNullOrEmpty(EventName) || !string.IsNullOrEmpty(Headline) ||
                    !string.IsNullOrEmpty(State) || !string.IsNullOrEmpty(JobTitle) ||
                    !string.IsNullOrEmpty(CaptionWriter) || !string.IsNullOrEmpty(CreditLine) ||
                    !string.IsNullOrEmpty(Source);
        }
    }
}
