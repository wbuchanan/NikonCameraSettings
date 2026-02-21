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
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NikonCameraSettings.SequenceItems {

    [ExportMetadata("Name", "Set Shooting Mode")]
    [ExportMetadata("Description", "Sets the shooting mode (single, continuous low, continuous high, self-timer, etc.).")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetShootingMode : SequenceItem, IValidatable {

        private static Dictionary<string, uint> _setting = new Dictionary<string, uint>() {
           { "Single", (uint)0 },
           { "Continuous Low", (uint)1 },
           { "Continuous High", (uint)2 },
           { "Self-Timer", (uint)3 },
           { "Mirror-Up", (uint)4 },
           { "Remote Timer-Instant", (uint)5 },
           { "Remote Timer-2sec", (uint)6 },
           { "Live View", (uint)7 },
           { "Quiet", (uint)8 },
           { "Remote Control", (uint)9 },
           { "Quiet C", (uint)10 },
           { "High Speed Frame Capture (C30)", (uint)12 },
           { "High Speed Frame Capture (C120)", (uint)13 },
           { "High Speed Frame Capture (C60)", (uint)14 },
           { "High Speed Frame Capture (C15)", (uint)15 },
        };


        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> shootingModeSettings = new List<string>();

        public List<string> ShootingModeSettings {
            get => shootingModeSettings;
            set {
                shootingModeSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedShootingModeSetting;

        [JsonProperty]
        public string SelectedShootingModeSetting {
            get => selectedShootingModeSetting;
            set {
                selectedShootingModeSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetShootingMode(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetShootingModeSettingsList();
            }
        }

        private void SetShootingModeSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_ShootingMode)) return;
            var e = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_ShootingMode);
            var list = new List<string>();
            for (int i = 0; i < e.Length; i++) {
                list.Add(_setting.Where(p => p.Value == (uint)e[i]).Select(p => p.Key).FirstOrDefault<string>());
            }
            ShootingModeSettings = list;
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetShootingModeSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            ShootingModeSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetShootingMode(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                ShootingModeSettings = ShootingModeSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_ShootingMode)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var e = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_ShootingMode);
            e.Index = shootingModeSettings.IndexOf(selectedShootingModeSetting);
            theCam.SetEnum(eNkMAIDCapability.kNkMAIDCapability_ShootingMode, e);
            return Task.CompletedTask;
        }
    }
}
