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

    [ExportMetadata("Name", "Set Bracketing Type")]
    [ExportMetadata("Description", "Sets the type of bracketing (AE, WB, ADL, etc.).")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetBracketingType : SequenceItem, IValidatable {

        private static Dictionary<string, uint> _setting = new Dictionary<string, uint>() {
            { "Both_3", (uint)4 },
            { "Both_5", (uint)5 },
            { "Both_7", (uint)6 },
            { "Both_9", (uint)7 },
            { "None", (uint)8 },
        };

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private static List<string> bracketingTypeSettings = new List<string>();

        public List<string> BracketingTypeSettings {
            get => bracketingTypeSettings;
            set {
                bracketingTypeSettings = value;
                RaisePropertyChanged();
            }
        }

        private string selectedBracketingTypeSetting;

        [JsonProperty]
        public string SelectedBracketingTypeSetting {
            get => selectedBracketingTypeSetting;
            set {
                selectedBracketingTypeSetting = value;
                RaisePropertyChanged();
            }
        }

        private readonly ICameraMediator camera;
        private NikonDevice theCam;

        [ImportingConstructor]
        public SetBracketingType(ICameraMediator camera) {
            this.camera = camera;
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;

            if (this.camera.GetInfo().Connected) {
                SetBracketingTypeSettingsList();
            }
        }

        private void SetBracketingTypeSettingsList() {
            if (!this.camera.GetInfo().Connected || theCam == null) return;
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_BracketingType)) return;
            var e = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_BracketingType);
            var list = new List<string>();
            for (int i = 0; i < e.Length; i++) list.Add(_setting.Keys.ToList()[i]);
            BracketingTypeSettings = list;
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            SetBracketingTypeSettingsList();
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            BracketingTypeSettings = new List<string>();
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new SetBracketingType(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                BracketingTypeSettings = BracketingTypeSettings,
            };
        }

        public bool Validate() {
            List<string> i = new List<string>();
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected");
            } else if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_BracketingType)) {
                i.Add("This capability is not supported on this camera.");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            theCam.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_BracketingType, _setting[SelectedBracketingTypeSetting]);
            return Task.CompletedTask;
        }
    }
}
