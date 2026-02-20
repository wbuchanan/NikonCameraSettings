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
using NikonCameraSettings.Utils;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;

namespace NikonCameraSettings.SequenceItems {

    [ExportMetadata("Name", "Set Card Slot 2 Save Mode")]
    [ExportMetadata("Description", "Sets the card slot 2 image save mode (overflow, backup, raw+jpeg, etc.).")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetSlot2SaveMode : SequenceItem, IValidatable {
        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> slot2SaveModeSettings = new List<string>();

        public List<string> Slot2SaveModeSettings {
            get => slot2SaveModeSettings;
            set {
                slot2SaveModeSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedSlot2SaveModeSetting;

        [JsonProperty]
        public string SelectedSlot2SaveModeSetting {
            get => selectedSlot2SaveModeSetting;
            set {
                selectedSlot2SaveModeSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetSlot2SaveMode(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetSlot2SaveModeSettingsList();
            }
        }

        private void SetSlot2SaveModeSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_Slot2ImageSaveMode)) return;
            var e = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_Slot2ImageSaveMode);
            var list = new List<string>();
            for (int i = 0; i < e.Length; i++) list.Add(e[i].ToString());
            Slot2SaveModeSettings = list;
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetSlot2SaveModeSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            Slot2SaveModeSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetSlot2SaveMode(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                Slot2SaveModeSettings = Slot2SaveModeSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_Slot2ImageSaveMode)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var e = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_Slot2ImageSaveMode);
            e.Index = slot2SaveModeSettings.IndexOf(selectedSlot2SaveModeSetting);
            theCam.SetEnum(eNkMAIDCapability.kNkMAIDCapability_Slot2ImageSaveMode, e);
            return Task.CompletedTask;
        }
    }
}
