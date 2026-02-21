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

    [ExportMetadata("Name", "Set High ISO Noise Reduction")]
    [ExportMetadata("Description", "Sets the high ISO noise reduction level.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetHighISONoiseReduction : SequenceItem, IValidatable {

        private static Dictionary<string, uint> _setting = new Dictionary<string, uint>() {
            { "Off", (uint)0 },
            { "On (Normal)", (uint)1 },
            { "On (High)", (uint)2 },
            { "On (Low)", (uint)3 },
        };

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> highISONoiseReductionSettings = new List<string>();

        public List<string> HighISONoiseReductionSettings {
            get => highISONoiseReductionSettings;
            set {
                highISONoiseReductionSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedHighISONoiseReductionSetting;

        [JsonProperty]
        public string SelectedHighISONoiseReductionSetting {
            get => selectedHighISONoiseReductionSetting;
            set {
                selectedHighISONoiseReductionSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetHighISONoiseReduction(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetHighISONoiseReductionSettingsList();
            }
        }

        private void SetHighISONoiseReductionSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_NoiseReductionHighISO)) return;
            HighISONoiseReductionSettings = _setting.Keys.ToList();
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetHighISONoiseReductionSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            HighISONoiseReductionSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetHighISONoiseReduction(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                HighISONoiseReductionSettings = HighISONoiseReductionSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_NoiseReductionHighISO)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            theCam.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_NoiseReductionHighISO,
                (uint)_setting[selectedHighISONoiseReductionSetting]);
            return Task.CompletedTask;
        }
    }
}
