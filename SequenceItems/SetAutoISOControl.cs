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
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Nikon;
using NikonCameraSettings.Utils;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using NikonCameraSettings.Properties;

namespace NikonCameraSettings.SequenceItems {
    /*
     * This class is based on Christian Palm's SetAperture class, though applied to different settings.
     */

    [ExportMetadata("Name", "Turn Auto-ISO Control On/Off")]
    [ExportMetadata("Description", "Turns the auto-sensitivity control (Auto-ISO) on or off.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetAutoISOControl : SequenceItem, IValidatable {
        private IList<string> issues = new List<string>();


        private readonly ICameraMediator camera;

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> autoISOSettings = new List<string>();

        public List<string> AutoISOSettings {
            get => autoISOSettings;
            set {
                autoISOSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedISOSetting;

        [JsonProperty]
        public string SelectedISOSetting {
            get => selectedISOSetting;
            set {
                selectedISOSetting = value;
                RaisePropertyChanged();
            }
        }

        private NikonDevice theCam;

        [ImportingConstructor]
        public SetAutoISOControl(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetAutoISOSettingsList();
            }
        }

        private void SetAutoISOSettingsList() {
            AutoISOSettings = new List<string>() { "On", "Off" };
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetAutoISOSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            autoISOSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetMonitorOnOff(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                MonitorSettings = AutoISOSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            }
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_IsoControl)) i.Add("Auto-ISO Control not supported on this camera.");
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            theCam.SetBoolean(eNkMAIDCapability.kNkMAIDCapability_IsoControl, selectedISOSetting == "Off");
            return Task.CompletedTask;
        }
    }
}