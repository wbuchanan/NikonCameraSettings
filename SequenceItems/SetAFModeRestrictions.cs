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

    [ExportMetadata("Name", "Set AF Mode Restrictions")]
    [ExportMetadata("Description", "Sets the autofocus mode restrictions.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetAFModeRestrictions : SequenceItem, IValidatable {
        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> aFModeRestrictionsSettings = new List<string>();

        public List<string> AFModeRestrictionsSettings {
            get => aFModeRestrictionsSettings;
            set {
                aFModeRestrictionsSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedAFModeRestrictionsSetting;

        [JsonProperty]
        public string SelectedAFModeRestrictionsSetting {
            get => selectedAFModeRestrictionsSetting;
            set {
                selectedAFModeRestrictionsSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetAFModeRestrictions(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetAFModeRestrictionsSettingsList();
            }
        }

        private void SetAFModeRestrictionsSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_AFModeRestrictions)) return;
            var list = new List<string>() { "No Restrictions", "Single AF", "Continuous AF", "Manual Focus" };
            AFModeRestrictionsSettings = list;
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetAFModeRestrictionsSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            AFModeRestrictionsSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetAFModeRestrictions(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                AFModeRestrictionsSettings = AFModeRestrictionsSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_AFModeRestrictions)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            int idx = aFModeRestrictionsSettings.IndexOf(selectedAFModeRestrictionsSetting);
            theCam.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_AFModeRestrictions, (uint)idx);
            return Task.CompletedTask;
        }
    }
}
