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

    [ExportMetadata("Name", "Set Live View Mode")]
    [ExportMetadata("Description", "Sets the live view display mode.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetLiveViewMode : SequenceItem, IValidatable {
        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> liveViewModeSettings = new List<string>();

        public List<string> LiveViewModeSettings {
            get => liveViewModeSettings;
            set {
                liveViewModeSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedLiveViewModeSetting;

        [JsonProperty]
        public string SelectedLiveViewModeSetting {
            get => selectedLiveViewModeSetting;
            set {
                selectedLiveViewModeSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetLiveViewMode(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetLiveViewModeSettingsList();
            }
        }

        private void SetLiveViewModeSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_ViewMode)) return;
            var e = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_ViewMode);
            var list = new List<string>();
            for (int i = 0; i < e.Length; i++) list.Add(e[i].ToString());
            LiveViewModeSettings = list;
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetLiveViewModeSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            LiveViewModeSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetLiveViewMode(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                LiveViewModeSettings = LiveViewModeSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_ViewMode)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var e = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_ViewMode);
            e.Index = liveViewModeSettings.IndexOf(selectedLiveViewModeSetting);
            theCam.SetEnum(eNkMAIDCapability.kNkMAIDCapability_ViewMode, e);
            return Task.CompletedTask;
        }
    }
}
