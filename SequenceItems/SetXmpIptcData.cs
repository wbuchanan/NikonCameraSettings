#region "copyright"

/*
    Copyright (c) 2026 William Buchanan (william@williambuchanan.net)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using Nikon;
using NikonCameraSettings.Models;
using NikonCameraSettings.Utils;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NikonCameraSettings.SequenceItems {

    [ExportMetadata("Name", "Set XMP-IPTC Astronomy Data")]
    [ExportMetadata("Description", "Writes GPS location, telescope pointing, and weather " +
        "conditions as a unified XMP-IPTC payload to a single Nikon camera preset slot.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetXmpIptcData : SequenceItem, IValidatable {

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;

        private readonly IProfileService profileService;

        private readonly ITelescopeMediator telescopeMediator;

        private readonly IWeatherDataMediator weatherMediator;

        private NikonDevice theCam;

        private bool isIptcSupported;

        public bool IsIptcSupported {
            get => isIptcSupported;
            private set {
                isIptcSupported = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(AreControlsEnabled));
            }
        }

        public bool AreControlsEnabled => !camera.GetInfo().Connected || IsIptcSupported;

        private void CheckIptcCapabilitySupport() {
            if (theCam != null) {
                IsIptcSupported = theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetInfo);
                Logger.Debug($"SetXmpIptcData: IPTC capability supported = {IsIptcSupported}");
            } else {
                IsIptcSupported = false;
            }
        }

       private uint presetSlot = IptcPresetWriter.DefaultXmpPresetSlot;

        [JsonProperty]
        public uint PresetSlot {
            get => presetSlot;
            set {
                presetSlot = Math.Max(IptcPresetWriter.XmpPresetSlotMin, Math.Min(value, IptcPresetWriter.XmpPresetSlotMax));
                RaisePropertyChanged();
            }
        }

        private bool activatePreset = true;

        [JsonProperty]
        public bool ActivatePreset {
            get => activatePreset;
            set {
                activatePreset = value;
                RaisePropertyChanged();
            }
        }

        public List<uint> AvailableSlots { get; } = new List<uint> {
            IptcPresetWriter.XmpPresetSlotMin,
            IptcPresetWriter.XmpPresetSlotMin + 1,
            IptcPresetWriter.XmpPresetSlotMax
        };

        public string GpsStatusDisplay {
            get {
                GpsData gps = ReadGpsFromProfile();
                return gps != null && gps.IsValid()
                    ? gps.ToString()
                    : "GPS not set (Options > General)";
            }
        }

        public string TelescopeStatusDisplay {
            get {
                bool connected = telescopeMediator?.GetInfo()?.Connected ?? false;
                return connected ? "Telescope connected" : "Telescope not connected";
            }
        }

        public string WeatherStatusDisplay {
            get {
                bool connected = weatherMediator?.GetInfo()?.Connected ?? false;
                return connected ? "Weather device connected" : "Weather device not connected";
            }
        }


        [ImportingConstructor]
        public SetXmpIptcData(ICameraMediator camera, IProfileService profileService,
            ITelescopeMediator telescopeMediator, IWeatherDataMediator weatherMediator) {
            this.camera = camera;
            this.profileService = profileService;
            this.telescopeMediator = telescopeMediator;
            this.weatherMediator = weatherMediator;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            CheckIptcCapabilitySupport();
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += Camera_Disconnected;
        }

        private Task Camera_Connected(object sender, EventArgs args) {
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            CheckIptcCapabilitySupport();
            RaisePropertyChanged(nameof(GpsStatusDisplay));
            RaisePropertyChanged(nameof(TelescopeStatusDisplay));
            RaisePropertyChanged(nameof(WeatherStatusDisplay));
            Logger.Debug("SetXmpIptcData: Camera connected, NikonDevice refreshed.");
            return Task.CompletedTask;
        }

        private Task Camera_Disconnected(object sender, EventArgs args) {
            theCam = null;
            IsIptcSupported = false;
            RaisePropertyChanged(nameof(AreControlsEnabled));
            Logger.Debug("SetXmpIptcData: Camera disconnected.");
            return Task.CompletedTask;
        }


        private GpsData ReadGpsFromProfile() {
            try {
                var settings = profileService?.ActiveProfile?.AstrometrySettings;
                if (settings == null) return null;
                return new GpsData(settings.Latitude, settings.Longitude, settings.Elevation);
            } catch (Exception ex) {
                Logger.Warning($"SetXmpIptcData: Error reading GPS from profile: {ex.Message}");
                return null;
            }
        }

        private AstrometricData ReadAstrometricDataFromTelescope() {
            return ReadAstrometricData(double.NaN, double.NaN);
        }

        private AstrometricData ReadAstrometricData(double latitude = double.NaN, double longitude = double.NaN) {
            try {
                var info = telescopeMediator?.GetInfo();
                if (info == null || !info.Connected) return null;
                double raHours = info.Coordinates?.RA ?? double.NaN;
                double decDegrees = info.Coordinates?.Dec ?? double.NaN;
                double altDegrees = info.Altitude;
                double azDegrees = info.Azimuth;
                double lstHours = info.SiderealTime;
                // Need to figure out where to find this
                string objectName = "";
                return new AstrometricData(raHours, decDegrees, altDegrees, azDegrees, lstHours, objectName, longitude, latitude);
            } catch (Exception ex) {
                Logger.Warning($"SetXmpIptcData: Error reading telescope data: {ex.Message}");
                return null;
            }
        }

        private WeatherData ReadWeatherData() {
            try {
                var info = weatherMediator?.GetInfo();
                if (info == null || !info.Connected) return null;
                return new WeatherData(info.Temperature, info.Humidity, info.DewPoint, info.Pressure, 
                                       info.WindSpeed, info.WindDirection, info.CloudCover, info.SkyQuality);
            } catch (Exception ex) {
                Logger.Warning($"SetXmpIptcData: Error reading weather data: {ex.Message}");
                return null;
            }
        }

        public bool Validate() {
            // Start with a fresh list of issues for each validation pass
            List<string> i = new List<string>();

            // Check if any camera is connected to NINA
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected.");
            } else {
                // Use the existing DeviceAccessor.Validate pattern to check for Nikon
                List<string> deviceErrors = DeviceAccessor.Validate(camera);
                // Add any device validation errors (e.g., "not a Nikon camera")
                i.AddRange(deviceErrors);

                // Layer 1 (Validation): check IPTC capability support on the camera.
                // This produces a user-visible warning in the sequencer UI.
                if (deviceErrors.Count == 0 && theCam != null &&
                    !theCam.SupportsCapability(
                        eNkMAIDCapability.kNkMAIDCapability_IPTCPresetInfo)) {
                    i.Add("Connected camera does not support IPTC preset data. " +
                          "This feature requires a compatible Nikon Z camera (Z 8, Z 9, etc.).");
                }
            }

            // Check that the preset slot is in the valid XMP-IPTC range
            if (PresetSlot < IptcPresetWriter.XmpPresetSlotMin ||
                PresetSlot > IptcPresetWriter.XmpPresetSlotMax) {
                i.Add($"Preset slot must be between {IptcPresetWriter.XmpPresetSlotMin} " +
                      $"and {IptcPresetWriter.XmpPresetSlotMax}.");
            }

            // Check that at least one data source will provide content
            GpsData gps = ReadGpsFromProfile();
            AstrometricData astro = ReadAstrometricData();
            WeatherData weather = ReadWeatherData();

            bool hasGps = gps != null && gps.IsValid();
            bool hasAstro = astro != null && astro.HasAnyData();
            bool hasWeather = weather != null && weather.HasAnyData();

            if (!hasGps && !hasAstro && !hasWeather) {
                i.Add("No data sources available. Configure GPS in Options > General, " +
                      "connect a telescope, or connect a weather device.");
            }

            // Update the Issues property (triggers UI refresh via RaisePropertyChanged)
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (theCam != null && !theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_IPTCPresetInfo)) {
                Logger.Warning("SetXmpIptcData: Camera does not support IPTC presets. Skipping.");
                return Task.CompletedTask;
            }

            GpsData gps = ReadGpsFromProfile();
            bool hasGps = gps != null && gps.IsValid();
            if (hasGps) {
                Logger.Info($"SetXmpIptcData: GPS data: {gps}");
            } else {
                Logger.Debug("SetXmpIptcData: No valid GPS data available.");
            }

            AstrometricData astro = ReadAstrometricData();
            bool hasAstro = astro != null && astro.HasAnyData();
            if (hasAstro) {
                Logger.Info($"SetXmpIptcData: Astrometric data: {astro}");
            } else {
                Logger.Debug("SetXmpIptcData: No telescope data available.");
            }

            WeatherData weather = ReadWeatherData();
            bool hasWeather = weather != null && weather.HasAnyData();
            if (hasWeather) {
                Logger.Info($"SetXmpIptcData: Weather data: {weather}");
            } else {
                Logger.Debug("SetXmpIptcData: No weather data available.");
            }

            if (!hasGps && !hasAstro && !hasWeather) {
                Logger.Warning("SetXmpIptcData: All data sources are empty. Skipping write.");
                return Task.CompletedTask;
            }

            if (theCam == null) {
                theCam = DeviceAccessor.GetNikonDevice(camera);
            }
            if (theCam == null) {
                Logger.Error("SetXmpIptcData: Could not obtain NikonDevice. " +
                    "Is a Nikon camera connected?");
                return Task.CompletedTask;
            }

            string xmpPayload;
            try {
                xmpPayload = XmpBuilder.BuildUnifiedXmp(
                    hasGps ? gps : null,
                    hasAstro ? astro : null,
                    hasWeather ? weather : null);
            } catch (ArgumentException ex) {
                Logger.Error($"SetXmpIptcData: XMP build error: {ex.Message}");
                return Task.CompletedTask;
            }

            int payloadSize = XmpBuilder.GetXmpByteCount(xmpPayload);
            Logger.Debug($"SetXmpIptcData: XMP payload size: {payloadSize} bytes " +
                $"(limit: {XmpBuilder.MaxXmpDataBytes} bytes)");

            if (payloadSize > XmpBuilder.MaxXmpDataBytes) {
                Logger.Error($"SetXmpIptcData: XMP payload ({payloadSize} bytes) exceeds " +
                    $"the 30 KB limit. Data will be truncated by the camera.");
            }

            try {
                IptcPresetWriter.WriteXmpIptcPreset(theCam, PresetSlot, "Astro", xmpPayload);
                if (ActivatePreset) {
                    IptcPresetWriter.ActivatePresetForAutoEmbed(theCam, PresetSlot);
                }
                string sections = string.Join(" + ", BuildSectionList(hasGps, hasAstro, hasWeather));
                Logger.Info($"SetXmpIptcData: Successfully wrote [{sections}] to " + $"preset slot {PresetSlot} ({payloadSize} bytes).");
            } catch (NikonException nex) {
                Logger.Error($"SetXmpIptcData: Nikon SDK error: {nex.ErrorCode} - {nex.Message}");
            } catch (Exception ex) {
                Logger.Error($"SetXmpIptcData: Unexpected error: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        private static List<string> BuildSectionList(bool hasGps, bool hasAstro, bool hasWeather) {
            List<string> sections = new List<string>();
            if (hasGps) sections.Add("GPS");
            if (hasAstro) sections.Add("Astrometric");
            if (hasWeather) sections.Add("Weather");
            return sections;
        }


        public override object Clone() {
            return new SetXmpIptcData(this.camera, this.profileService,
                this.telescopeMediator, this.weatherMediator) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                PresetSlot = PresetSlot,
                ActivatePreset = ActivatePreset,
            };
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(SetXmpIptcData)}, " +
                $"Slot: {PresetSlot}, AutoEmbed: {ActivatePreset}";
        }
    }
}