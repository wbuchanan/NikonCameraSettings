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
using System.Linq;
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

    [ExportMetadata("Name", "Set Metering Mode")]
    [ExportMetadata("Description", "Sets the light metering mode.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetMeteringMode : SequenceItem, IValidatable {

        private static Dictionary<string, uint> _setting = new Dictionary<string, uint>() {
            { "Matrix", (uint)0 },
            { "Center Weighted", (uint)1 },
            { "Spot", (uint)2 },
            { "Highlight", (uint)4 },
        };

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> meteringModeSettings = new List<string>();

        public List<string> MeteringModeSettings {
            get => meteringModeSettings;
            set {
                meteringModeSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedMeteringModeSetting;

        [JsonProperty]
        public string SelectedMeteringModeSetting {
            get => selectedMeteringModeSetting;
            set {
                selectedMeteringModeSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetMeteringMode(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetMeteringModeSettingsList();
            }
        }

        private void SetMeteringModeSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_MeteringMode)) return;
            MeteringModeSettings = _setting.Keys.ToList();
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetMeteringModeSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            MeteringModeSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetMeteringMode(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                MeteringModeSettings = MeteringModeSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_MeteringMode)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            theCam.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_MeteringMode, _setting[selectedMeteringModeSetting]);
            return Task.CompletedTask;
        }
    }
}
