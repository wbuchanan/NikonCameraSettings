#region "copyright"

/*
    Copyright © 2026 William Buchanan (william@williambuchanan.net)
    Copyright © 2025 Christian Palm (christian@palm-family.de)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Nikon;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NikonCameraSettings.Utils {

    public static class DeviceAccessor {
        /*
         * This method is largely based on the work of Christian Palm and I want to make sure
         * that his work is appropriately credited and attributed.  In the LensAF plugin,
         * see the Utility class definition in LensAF.Util.
         */

        public static NikonDevice GetNikonDevice(ICameraMediator mediator) {
            BindingFlags boundFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.GetField;
            try {
                List<string> errors = Validate(mediator);
                if (errors.Count > 0) {
                    return null;
                }
                IDevice cam = mediator.GetDevice() is PersistSettingsCameraDecorator decorator ? decorator.Camera : mediator.GetDevice();
                FieldInfo field = typeof(NikonCamera).GetField("_camera", boundFlags);
                object device = field.GetValue((NikonCamera)cam);
                return (NikonDevice)device;
            } catch (Exception e) {
                Logger.Error(e);
                Notification.ShowError(e.Message);
                return null;
            }
        }

        public static List<string> Validate(ICameraMediator mediator) {
            List<string> errors = new List<string>();
            bool cameraConnected = mediator.GetInfo().Connected;
            if (!cameraConnected) {
                errors.Add("No camera connected.");
                return errors;
            }
            if (!(mediator.GetDevice().Category == "Nikon" && cameraConnected)) {
                errors.Add("Connected camera is not a Nikon.");
            } else {
                IDevice cam = mediator.GetDevice() is PersistSettingsCameraDecorator decorator ? decorator.Camera : mediator.GetDevice();
                if (cam is not NikonCamera) {
                    errors.Add("No Nikon camera connected with default Nikon driver.");
                }
            }
            return errors;
        }
    }
}