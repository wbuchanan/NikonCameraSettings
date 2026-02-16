#region "copyright"

/*
    Copyright © 2026 William Buchanan (william@williambuchanan.net)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.ComponentModel.Composition;
using System.Data;
using System.Xml;
using System.Collections.Generic;
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

namespace NikonCameraSettings.SequenceItems {
    /*
     * This class is based on Christian Palm's SetAperture class, though applied to different settings.
     */

    [ExportMetadata("Name", "Start Pixel Mapping")]
    [ExportMetadata("Description", "Begins the Pixel Mapping process in the camera.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class StartPixelMapping : SequenceItem, IValidatable {
        private IList<string> issues = new List<string>();

        private readonly ICameraMediator camera;

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private NikonDevice theCam;

        [ImportingConstructor]
        public StartPixelMapping(ICameraMediator camera) {
            this.camera = camera;
            this.theCam = DeviceAccessor.GetNikonDevice(camera);
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += CameraDisconnected;
        }

        private Task Camera_Connected(object arg1, EventArgs args) {
            return Task.CompletedTask;
        }

        private Task CameraDisconnected(object arg1, EventArgs args) {
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new StartPixelMapping(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
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


        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {

            var pixelMappingTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnPixelMappingComplete(NikonDevice sender, int result) {
                pixelMappingTcs.TrySetResult(result);
            }

            theCam.PixelMappingComplete += OnPixelMappingComplete;

            try {

                progress?.Report(new ApplicationStatus() {
                    Status = "Starting pixel mapping..."
                });

                theCam.Start(eNkMAIDCapability.kNkMAIDCapability_InitiatePixelMapping);

                progress?.Report(new ApplicationStatus() {
                    Status = "Pixel mapping in progress — waiting for camera..."
                });

                using (token.Register(() => pixelMappingTcs.TrySetCanceled())) {

                    Task timeoutTask = Task.Delay(TimeSpan.FromMinutes(5), CancellationToken.None);

                    Task completedTask = await Task.WhenAny(pixelMappingTcs.Task, timeoutTask);

                    if (completedTask == timeoutTask) {
                        Logger.Error("Pixel mapping timed out after 5 minutes without receiving " +
                                   "kNkMAIDEvent_PixelMappingComplete from the camera.");
                        throw new SequenceEntityFailedException(
                            "Pixel mapping timed out after 5 minutes. The camera may have " +
                            "encountered an error. Check the camera display for any error " +
                            "messages and ensure the USB connection is stable. You should also restart the camera.");
                    }

                    int pixelMappingResult = await pixelMappingTcs.Task;

                    if (pixelMappingResult == 1) {
                        Logger.Info("Pixel mapping completed successfully.");
                        progress?.Report(new ApplicationStatus() {
                            Status = "Pixel mapping completed successfully"
                        });
                    } else {
                        Logger.Error($"Pixel mapping failed with result code: {pixelMappingResult}");
                        throw new SequenceEntityFailedException(
                            $"Pixel mapping failed (camera reported result code: {pixelMappingResult}). " +
                            "Check the camera display for error details. Common causes include " +
                            "overheating, low battery, or firmware errors.");
                    }
                }

            } catch (NikonException ex) {
                Logger.Error($"Nikon SDK error initiating pixel mapping: {ex.Message}");
                throw new SequenceEntityFailedException(
                    $"Failed to start pixel mapping: {ex.Message}", ex);
            } catch (OperationCanceledException) {
                Logger.Warning("Pixel mapping was cancelled by the user.");
                throw;
            } finally {
                theCam.PixelMappingComplete -= OnPixelMappingComplete;
            }
        }







    }
}