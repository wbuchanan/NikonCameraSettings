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
using NINA.Image.ImageData;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;

namespace NikonCameraSettings.SequenceItems {

    [ExportMetadata("Name", "Set AF Subject Detection")]
    [ExportMetadata("Description", "Sets the autofocus subject detection mode.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetAFSubjectDetection : SequenceItem, IValidatable {

        private static Dictionary<string, uint> subjDetection = new Dictionary<string, uint>() {
            { "Off", (uint)0 },
            { "Auto", (uint)1 },
            { "People", (uint)2 },
            { "Animal", (uint)3 },
            { "Vehicle", (uint)4 },
            { "Birds", (uint)5 },
            { "Airplanes", (uint)6 },
        };

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> aFSubjectDetectionSettings = new List<string>();

        public List<string> AFSubjectDetectionSettings {
            get => aFSubjectDetectionSettings;
            set {
                aFSubjectDetectionSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedAFSubjectDetectionSetting;

        [JsonProperty]
        public string SelectedAFSubjectDetectionSetting {
            get => selectedAFSubjectDetectionSetting;
            set {
                selectedAFSubjectDetectionSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetAFSubjectDetection(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetAFSubjectDetectionSettingsList();
            }
        }

        private void SetAFSubjectDetectionSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_AFSubjectDetection)) return;
            var e = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_AFSubjectDetection);
            var list = new List<string>();
            // This should limit the selections to only those valid for the camera
            for (int i = 0; i < e.Length; i++) list.Add(subjDetection.Keys.ToList()[i]);
            AFSubjectDetectionSettings = list;
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetAFSubjectDetectionSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            AFSubjectDetectionSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetAFSubjectDetection(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                AFSubjectDetectionSettings = AFSubjectDetectionSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_AFSubjectDetection)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            theCam.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_AFSubjectDetection, subjDetection[SelectedAFSubjectDetectionSetting]);
            return Task.CompletedTask;
        }
    }
}
