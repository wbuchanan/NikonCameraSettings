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

    [ExportMetadata("Name", "Set Image Area/Format")]
    [ExportMetadata("Description", "Sets the image size/aspect ratio.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetImageSize : SequenceItem, IValidatable {

        private static Dictionary<string, uint> _setting = new Dictionary<string, uint>() {
            { "FX Format", (uint)3 },
            { "DX Format", (uint)4 },
            { "1:1", (uint)8 },
            { "16:9", (uint)9 },
        };

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> imageSizeSettings = new List<string>();

        public List<string> ImageSizeSettings {
            get => imageSizeSettings;
            set {
                imageSizeSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedImageSizeSetting;

        [JsonProperty]
        public string SelectedImageSizeSetting {
            get => selectedImageSizeSetting;
            set {
                selectedImageSizeSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetImageSize(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetImageSizeSettingsList();
            }
        }

        private void SetImageSizeSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_CCDDataMode)) return;
            ImageSizeSettings = _setting.Keys.ToList();
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetImageSizeSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            ImageSizeSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetImageSize(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                ImageSizeSettings = ImageSizeSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_CCDDataMode)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            theCam.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_CCDDataMode, _setting[selectedImageSizeSetting]);
            return Task.CompletedTask;
        }
    }
}
