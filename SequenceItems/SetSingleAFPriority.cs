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

    [ExportMetadata("Name", "Set Single AF Priority")]
    [ExportMetadata("Description", "Sets the single-servo autofocus priority (focus or release).")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetSingleAFPriority : SequenceItem, IValidatable {
        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> singleAFPrioritySettings = new List<string>();

        public List<string> SingleAFPrioritySettings {
            get => singleAFPrioritySettings;
            set {
                singleAFPrioritySettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedSingleAFPrioritySetting;

        [JsonProperty]
        public string SelectedSingleAFPrioritySetting {
            get => selectedSingleAFPrioritySetting;
            set {
                selectedSingleAFPrioritySetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetSingleAFPriority(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetSingleAFPrioritySettingsList();
            }
        }

        private void SetSingleAFPrioritySettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_AFsPriority)) return;
            var e = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_AFsPriority);
            var list = new List<string>();
            for (int i = 0; i < e.Length; i++) list.Add(e[i].ToString());
            SingleAFPrioritySettings = list;
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetSingleAFPrioritySettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            SingleAFPrioritySettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetSingleAFPriority(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                SingleAFPrioritySettings = SingleAFPrioritySettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_AFsPriority)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var e = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_AFsPriority);
            e.Index = singleAFPrioritySettings.IndexOf(selectedSingleAFPrioritySetting);
            theCam.SetEnum(eNkMAIDCapability.kNkMAIDCapability_AFsPriority, e);
            return Task.CompletedTask;
        }
    }
}
