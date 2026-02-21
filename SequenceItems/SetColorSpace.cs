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

    [ExportMetadata("Name", "Set Color Space")]
    [ExportMetadata("Description", "Sets the color space (sRGB or Adobe RGB).")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetColorSpace : SequenceItem, IValidatable {
        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> colorSpaceSettings = new List<string>();

        public List<string> ColorSpaceSettings {
            get => colorSpaceSettings;
            set {
                colorSpaceSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedColorSpaceSetting;

        [JsonProperty]
        public string SelectedColorSpaceSetting {
            get => selectedColorSpaceSetting;
            set {
                selectedColorSpaceSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetColorSpace(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetColorSpaceSettingsList();
            }
        }

        private void SetColorSpaceSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_ImageColorSpace)) return;
            var list = new List<string>() { "sRGB", "AdobeRGB" };
            ColorSpaceSettings = list;
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetColorSpaceSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            ColorSpaceSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetColorSpace(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                ColorSpaceSettings = ColorSpaceSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_ImageColorSpace)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            theCam.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_ImageColorSpace, (uint)colorSpaceSettings.IndexOf(selectedColorSpaceSetting));
            return Task.CompletedTask;
        }
    }
}
