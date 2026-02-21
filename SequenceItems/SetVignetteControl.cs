#region "copyright"

/*
    Copyright © 2026 William Buchanan (william@williambuchanan.net)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using Nikon;
using NikonCameraSettings.Utils;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NikonCameraSettings.SequenceItems {

    [ExportMetadata("Name", "Set Vignette Control")]
    [ExportMetadata("Description", "Sets the vignette control level.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetVignetteControl : SequenceItem, IValidatable {

        private static Dictionary<string, uint> _setting = new Dictionary<string, uint>() {
            { "High", (uint)0 },
            { "Normal", (uint)1 },
            { "Low", (uint)2 },
            { "Off", (uint)3 },
        };

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> vignetteControlSettings = new List<string>();

        public List<string> VignetteControlSettings {
            get => vignetteControlSettings;
            set {
                vignetteControlSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedVignetteControlSetting;

        [JsonProperty]
        public string SelectedVignetteControlSetting {
            get => selectedVignetteControlSetting;
            set {
                selectedVignetteControlSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetVignetteControl(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetVignetteControlSettingsList();
            }
        }

        private void SetVignetteControlSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_VignetteControl)) return;
            VignetteControlSettings = _setting.Keys.ToList();
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetVignetteControlSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            VignetteControlSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetVignetteControl(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                VignetteControlSettings = VignetteControlSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_VignetteControl)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            theCam.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_VignetteControl, _setting[selectedVignetteControlSetting]);
            return Task.CompletedTask;
        }
    }
}
