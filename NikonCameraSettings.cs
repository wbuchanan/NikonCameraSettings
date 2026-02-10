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
using NikonCameraSettings.Properties;
using NikonCameraSettings.Utils;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace NikonCameraSettings {

    /// <summary>
    /// This class exports the IPluginManifest interface and will be used for the general plugin information and options
    /// The base class "PluginBase" will populate all the necessary Manifest Meta Data out of the AssemblyInfo attributes. Please fill these accoringly
    ///
    /// An instance of this class will be created and set as datacontext on the plugin options tab in N.I.N.A. to be able to configure global plugin settings
    /// The user interface for the settings will be defined by a DataTemplate with the key having the naming convention "NikonCameraSettings_Options" where NikonCameraSettings corresponds to the AssemblyTitle - In this template example it is found in the Options.xaml
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class NikonCameraSettings : PluginBase, INotifyPropertyChanged {
        private static NikonCameraSettings instance;
        public static ICameraMediator Camera;
        public static IProfileService ProfileService;

        public event PropertyChangedEventHandler PropertyChanged;

        private NikonDevice nikond;

        public List<string> vrsettings = new List<string>();
        public List<string> monitorsettings = new List<string>();
        public List<string> storagelocationsettings = new List<string>();

        public string ImageStabilization {
            get { return Settings.Default.ImageStabilization; }
            set {
                Settings.Default.ImageStabilization = value;
                CoreUtil.SaveSettings(Settings.Default);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageStabilization)));
                // Should change the setting in the camera
                //if (!(this.nikond == null)) new CamInfo(this.nikond).SetImageStabilization(this.nikond, value);
            }
        }

        public string MonitorOnOff {
            get { return Settings.Default.MonitorOnOff; }
            set {
                Settings.Default.MonitorOnOff = value;
                CoreUtil.SaveSettings(Settings.Default);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MonitorOnOff)));
                //if (!(this.nikond == null)) new CamInfo(this.nikond).SetMonitorOnOff(this.nikond, value);
            }
        }

        public string StorageLocation {
            get { return Settings.Default.StorageLocation; }
            set {
                Settings.Default.StorageLocation = value;
                CoreUtil.SaveSettings(Settings.Default);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StorageLocation)));
                //if (!(this.nikond == null)) new CamInfo(this.nikond).SetStorageLocation(this.nikond, value);
            }
        }

        [ImportingConstructor]
        public NikonCameraSettings(ICameraMediator camera, IProfileService profileService) {
            instance = this;

            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }
            Camera = camera;
            ProfileService = profileService;
            this.nikond = DeviceAccessor.GetNikonDevice(camera);
            vrsettings = CamInfo.GetVRSettings();
            monitorsettings = CamInfo.GetMonitorSettings();
            storagelocationsettings = CamInfo.GetStorageLocationSettings();
        }

        public override Task Teardown() {
            /*try {
                NikonEnum monitorEnum = this.nikond.GetEnum((eNkMAIDCapability)CamInfo.CHANGE_MONITOR_OFF_STATUS);
                if (monitorEnum.Index == 1) {
                    monitorEnum.Index = 0;
                    // Turn the camera monitor back on
                    this.nikond.SetEnum((eNkMAIDCapability)CamInfo.CHANGE_MONITOR_OFF_STATUS, monitorEnum);
                }
            } catch (Exception e) {
                Logger.Error("There was an error turning the camera monitor back on: {e}");
            }*/
            return base.Teardown();
        }
    }
}