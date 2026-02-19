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
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nikon;
using NikonCameraSettings.Models;
using NikonCameraSettings.Utils;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;

namespace NikonCameraSettings.SequenceItems {

    [ExportMetadata("Name", "Set IPTC Data")]
    [ExportMetadata("Description", "Writes standard IPTC metadata (Caption, Headline, Byline, etc.) " +
        "to a Nikon camera's NMS-IPTC preset slot (1-10) for embedding into captured images.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetNmsIptcData : SequenceItem, IValidatable {

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                // Notify the UI binding that the issues list has changed
                RaisePropertyChanged();
            }
        }
        private readonly ICameraMediator camera;
        private NikonDevice theCam;
        private int presetSlot = 1;

        [JsonProperty]
        public int PresetSlot {
            get => presetSlot;
            set {
                // Clamp the value to the valid range (1-10) to prevent SDK errors
                presetSlot = Math.Max(1, Math.Min(10, value));
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

        private string caption = "";
        private string altText = "";
        private string city = "";
        private string copyright = "";
        private string country = "";
        private string countryCode = "";
        private string objectName = "";
        private string creator = "";
        private string eventName = "";
        private string headline = "";
        private string state = "";
        private string jobTitle = "";
        private string captionWriter = "";
        private string creditLine = "";
        private string source = "";
        private string profileName = "";


        [JsonProperty]
        public string Caption {
            get => caption;
            set {
                caption = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string AltText {
            get => altText;
            set {
                altText = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string City {
            get => city;
            set {
                city = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string Copyright {
            get => copyright;
            set {
                copyright = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string Country {
            get => country;
            set {
                country = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string CountryCode {
            get => countryCode;
            set {
                countryCode = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string ObjectName {
            get => objectName;
            set {
                objectName = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string Creator {
            get => creator;
            set {
                creator = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string EventName {
            get => eventName;
            set {
                eventName = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string Headline {
            get => headline;
            set {
                headline = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string State {
            get => state;
            set {
                state = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string JobTitle {
            get => jobTitle;
            set {
                jobTitle = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string CaptionWriter {
            get => captionWriter;
            set {
                captionWriter = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string CreditLine {
            get => creditLine;
            set {
                creditLine = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string Source {
            get => source;
            set {
                source = value ?? "";
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string ProfileName {
            get => profileName;
            set {
                profileName = value ?? "";
                RaisePropertyChanged();
            }
        }


        [ImportingConstructor]
        public SetNmsIptcData(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += Camera_Disconnected;
        }

        private Task Camera_Connected(object sender, EventArgs args) {
            // Obtain a fresh NikonDevice reference for the newly connected camera
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            Logger.Debug("SetNmsIptcData: Camera connected, NikonDevice refreshed.");
            return Task.CompletedTask;
        }

        private Task Camera_Disconnected(object sender, EventArgs args) {
            // Null out the device reference since the camera is no longer available
            theCam = null;
            Logger.Debug("SetNmsIptcData: Camera disconnected.");
            return Task.CompletedTask;
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected.");
            } else {
                List<string> deviceErrors = DeviceAccessor.Validate(camera);
                i.AddRange(deviceErrors);
            }
            // Add a test for Setting the IPTC data 
            
            if (PresetSlot < 1 || PresetSlot > 10) {
                i.Add("Preset slot must be between 1 and 10 for standard IPTC data.");
            }

            NmsIptcData data = BuildNmsIptcData();
            if (!data.HasContent()) {
                i.Add("No IPTC fields have been filled in. Enter at least one field value.");
            }

            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            NmsIptcData data = BuildNmsIptcData();
            if (!data.HasContent()) {
                Logger.Warning("SetNmsIptcData: No IPTC fields have content. Skipping write.");
                return Task.CompletedTask;
            }

            if (theCam == null) {
                theCam = DeviceAccessor.GetNikonDevice(camera);
            }
            if (theCam == null) {
                Logger.Error("SetNmsIptcData: Could not obtain NikonDevice. Is a Nikon camera connected?");
                return Task.CompletedTask;
            }

            try {
                uint slot = (uint)PresetSlot;
                string profileName = $"{ProfileName}";
                // Need to check this method after the changes
                IptcPresetWriter.WriteNmsIptcPreset(theCam, slot, profileName, data);
                if (ActivatePreset) {
                    IptcPresetWriter.ActivatePresetForAutoEmbed(theCam, slot);
                }
                Logger.Info($"SetNmsIptcData: Successfully wrote IPTC data to preset slot {PresetSlot}. {data}");
            } catch (NikonException nex) {
                Logger.Error($"SetNmsIptcData: Nikon SDK error: {nex.ErrorCode} - {nex.Message}");
            } catch (Exception ex) {
                Logger.Error($"SetNmsIptcData: Unexpected error: {ex.Message}");
            }

            return Task.CompletedTask;
        }


        private NmsIptcData BuildNmsIptcData() {
            return new NmsIptcData(
                caption: Caption,
                altText: AltText,
                city: City,
                copyright: Copyright,
                country: Country,
                countryCode: CountryCode,
                objectName: ObjectName,
                creator: Creator,
                eventName: EventName,
                headline: Headline,
                state: State,
                jobTitle: JobTitle,
                captionWriter: CaptionWriter,
                creditLine: CreditLine,
                source: Source);
        }


        public override object Clone() {
            return new SetNmsIptcData(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                PresetSlot = PresetSlot,
                ActivatePreset = ActivatePreset,
                Caption = Caption,
                AltText = AltText,
                City = City,
                Copyright = Copyright,
                Country = Country,
                CountryCode = CountryCode,
                ObjectName = ObjectName,
                Creator = Creator,
                EventName = EventName,
                Headline = Headline,
                State = State,
                JobTitle = JobTitle,
                CaptionWriter = CaptionWriter,
                CreditLine = CreditLine,
                Source = Source,
                ProfileName = ProfileName,
            };
        }
    }
}