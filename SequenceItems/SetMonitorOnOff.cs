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

    [ExportMetadata("Name", "Turn Monitor On/Off")]
    [ExportMetadata("Description", "Sets the image stabilization/vibration reduction setting for Nikon cameras and compatible lenses.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetMonitorOnOff : SequenceItem, IValidatable {
        private IList<string> issues = new List<string>();

        public RelayCommand<object> RefreshCommand { get; set; }

        private readonly ICameraMediator camera;

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> monitorSettings = new List<string>();

        public List<string> MonitorSettings {
            get => monitorSettings;
            set {
                monitorSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedMonitorSetting;

        [JsonProperty]
        public string SelectedMonitorSetting {
            get => selectedMonitorSetting;
            set {
                selectedMonitorSetting = value;
                RaisePropertyChanged();
            }
        }

        private NikonDevice theCam;

        [ImportingConstructor]
        public SetMonitorOnOff(ICameraMediator camera) {
            RefreshCommand = new RelayCommand<object>((o) => SetMonitorSettingsList());
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetMonitorSettingsList();
            }
        }

        private void SetMonitorSettingsList() {
            List<string> a = new List<string>();
            if (!this.camera.GetInfo().Connected) {
                Logger.Debug("Camera not connected.  Unable to change monitor setting.");
                return;
            }
            Logger.Info("Getting monitor settings for Nikon");
            MonitorSettings = CamInfo.GetMonitorSettings();
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetMonitorSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            monitorSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetMonitorOnOff(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                MonitorSettings = MonitorSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            new CamInfo(theCam).SetMonitorOnOff(theCam, SelectedMonitorSetting);
            return Task.CompletedTask;
        }

        ~SetMonitorOnOff() {
            this.camera.Connected -= Camera_Connected;
            this.camera.Disconnected -= CameraDisconnected;
        }
    }
}