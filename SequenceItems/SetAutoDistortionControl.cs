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

    [ExportMetadata("Name", "Set Auto Distortion Control")]
    [ExportMetadata("Description", "Sets the auto distortion control setting.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetAutoDistortionControl : SequenceItem, IValidatable {
        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> autoDistortionSettings = new List<string>();

        public List<string> AutoDistortionSettings {
            get => autoDistortionSettings;
            set {
                autoDistortionSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedAutoDistortionSetting;

        [JsonProperty]
        public string SelectedAutoDistortionSetting {
            get => selectedAutoDistortionSetting;
            set {
                selectedAutoDistortionSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetAutoDistortionControl(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetAutoDistortionSettingsList();
            }
        }

        private void SetAutoDistortionSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_AutoDistortion)) return;
            var list = new List<string>() { "Off", "On" };
            AutoDistortionSettings = list;
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetAutoDistortionSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            AutoDistortionSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetAutoDistortionControl(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                AutoDistortionSettings = AutoDistortionSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_AutoDistortion)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            int idx = autoDistortionSettings.IndexOf(selectedAutoDistortionSetting);
            theCam.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_AutoDistortion, (uint)idx);
            return Task.CompletedTask;
        }
    }
}
