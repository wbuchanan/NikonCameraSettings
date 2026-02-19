#region "copyright"

/*
    Copyright © 2026 William Buchanan (william@williambuchanan.net)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ASCOM.Common.Helpers;
using Newtonsoft.Json;
using Nikon;
using NikonCameraSettings.Utils;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NikonCameraSettings.SequenceItems {

    [ExportMetadata("Name", "Capture Focus Shift")]
    [ExportMetadata("Description", "Starts Nikon focus shift shooting and monitors progress. Configure shot count, step width, and interval in the camera's Photo Shooting Menu first.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class StartFocusShift : SequenceItem, IValidatable {

        // Validation issues displayed in the NINA sequencer's validation panel
        private IList<string> issues = new List<string>();

        // NINA's camera mediator provides access to camera connection state
        private readonly ICameraMediator camera;

        // The underlying nikoncswrapper NikonDevice for direct SDK communication
        private NikonDevice theCam;

        private NikonCamera nikonCamera;

        // Tracks the initial total shot count for calculating progress percentage
        private ulong initialTotalShots;

        // IValidatable.Issues - displayed in NINA's validation indicators
        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                // Notify the NINA UI that the validation state has changed
                RaisePropertyChanged();
            }
        }

        // The number of shots remaining, updated during capture for UI binding
        private int remainingShots;
        public int RemainingShots {
            get => remainingShots;
            set {
                remainingShots = value;
                // Triggers WPF binding update so the sequencer UI refreshes
                RaisePropertyChanged();
            }
        }

        // The total number of shots in the focus shift sequence (set at start)
        private int totalShots;
        public int TotalShots {
            get => totalShots;
            set {
                totalShots = value;
                // Triggers WPF binding update so the sequencer UI refreshes
                RaisePropertyChanged();
            }
        }

        // Human-readable status message displayed in the sequencer UI
        private string statusMessage = "Idle";
        public string StatusMessage {
            get => statusMessage;
            set {
                statusMessage = value;
                // Triggers WPF binding update so the sequencer UI refreshes
                RaisePropertyChanged();
            }
        }

        private int completedShots;
        public int CompletedShots {
            get => completedShots;
            set {
                completedShots = value;
                // Triggers WPF binding update for the ProgressBar Value
                RaisePropertyChanged();
            }
        }

        private ManualResetEvent ready = new ManualResetEvent(false);

        private int pollIntervalMs = 2000;

        public int PollInterval {
            get => pollIntervalMs;
            set {
                pollIntervalMs = value;
                RaisePropertyChanged();
            }
        }

        private int maxPollIterations = 500;

        public int MaxPollIterations {
            get => maxPollIterations;
            set {
                maxPollIterations = value;
                RaisePropertyChanged();
            }
        }

        
        [ImportingConstructor]
        public StartFocusShift(ICameraMediator camera) {
            // Store the camera mediator for connection state checks
            this.camera = camera;

            // Attempt to get the underlying NikonDevice via reflection accessor
            // Reference: NikonCameraSettings/Utils/DeviceAccessor.cs
            this.theCam = DeviceAccessor.GetNikonDevice(camera);
            
            // Subscribe to camera connect/disconnect events to refresh device reference
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += Camera_Disconnected;
        }

        private Task Camera_Connected(object sender, EventArgs e) {
            theCam = DeviceAccessor.GetNikonDevice(camera);
            return Task.CompletedTask;
        }

        private Task Camera_Disconnected(object sender, EventArgs e) {
            theCam = null;
            return Task.CompletedTask;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            // Re-acquire the NikonDevice in case the camera was reconnected
            theCam = DeviceAccessor.GetNikonDevice(camera);            

            Validate(); // Ensure we have the latest validation state before starting

            if (Issues.Count > 0) {
                return Task.CompletedTask;
            }

            try {
                StatusMessage = "Starting focus shift shooting...";
                StartShooting(theCam);
                StatusMessage = "Monitoring focus shift shooting...";
                MonitorShooting(theCam);
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            } catch (NikonException ex) {
                return Task.CompletedTask;
            } catch (TargetInvocationException ex) when (ex.InnerException is NikonException nikonEx) {
                Logger.Error("Error during focus shift shooting: " + nikonEx.Message);
                return Task.CompletedTask;
            } catch (Exception ex) {
                Logger.Error("Unexpected error during focus shift shooting: " + ex.Message);
                return Task.CompletedTask;
            } finally {
                StatusMessage = "Focus shift shooting complete.";
            }
        }
        
        private void StartShooting(NikonDevice shootingCamera) {
            
            int structSize = Marshal.SizeOf<NkMAIDCaptureFocusShift>();
            IntPtr ptr = Marshal.AllocHGlobal(structSize);

            try {
                ZeroMemory(ptr, structSize);
                shootingCamera.Start(eNkMAIDCapability.kNkMAIDCapability_CaptureFocusShift,
                             eNkMAIDDataType.kNkMAIDDataType_GenericPtr, ptr);
                NkMAIDCaptureFocusShift result = Marshal.PtrToStructure<NkMAIDCaptureFocusShift>(ptr);
                NkMAIDGetFocusShiftCapInfo info = QueryFocusShiftInfo(shootingCamera);
                // Gets the initial remaining number immediately to set as the total number of shots for the stack
                TotalShots = Convert.ToInt32(info.ulRemainingNumber);
            } catch (Exception ex) {
                Logger.Error("Starting Focus Shift Shooting failed: " + ex.Message);
            } finally {
                Marshal.FreeHGlobal(ptr);

            }
        }

        private void MonitorShooting(NikonDevice shootingCamera) {
            int previousRemaining = -1;
            int pollIteration = 0;
            while (true) {
                Thread.Sleep(pollIntervalMs);
                pollIteration++;
                try {
                    NkMAIDGetFocusShiftCapInfo info = QueryFocusShiftInfo(shootingCamera);
                    string shootingStatus = info.ulStatus == 1 ? "Shooting" : "Stopped";
                    string delta = "";
                    if (previousRemaining >= 0 && info.ulStatus == 1) {
                        int shotsInInterval = previousRemaining - Convert.ToInt32(info.ulRemainingNumber);
                        if (shotsInInterval > 0) {
                            delta = $"Δ Shots taken since last poll {shotsInInterval})";
                        }
                        // Updates the number of shots completed/captured
                        CompletedShots += shotsInInterval;
                    }
                    string statusMsg = "";
                    previousRemaining = Convert.ToInt32(info.ulRemainingNumber);
                    // Updates the remaining shots indicator
                    RemainingShots = previousRemaining;
                    // Once the camera reports stopping, break out of the loop
                    if (info.ulStatus == 0) break;
                } catch (NikonException ex) when (ex.ErrorCode == eNkMAIDResult.kNkMAIDResult_DeviceBusy) {
                    Logger.Info("Monitoring focus shift shooting - device busy (expected), will continue polling.");
                } catch (TargetInvocationException ex) when (ex.InnerException is NikonException nikonEx &&
                                                             nikonEx.ErrorCode == eNkMAIDResult.kNkMAIDResult_DeviceBusy) {
                    Logger.Info("Monitoring focus shift shooting - device busy (expected), will continue polling.");
                } catch (Exception ex) {
                    if (pollIteration > maxPollIterations) {
                        Logger.Error("Monitoring focus shift shooting: Error after 500th polling interval.  Stopping monitoring." + ex.Message);
                        break;
                    }
                }
            }
        }

        private NkMAIDGetFocusShiftCapInfo QueryFocusShiftInfo(NikonDevice shootingCamera) {
            int structSize = Marshal.SizeOf<NkMAIDGetFocusShiftCapInfo>();
            IntPtr ptr = Marshal.AllocHGlobal(structSize);
            try {
                ZeroMemory(ptr, structSize);
                BindingFlags nonPublic = BindingFlags.NonPublic | BindingFlags.Instance;
                PropertyInfo schedulerProp = typeof(NikonBase).GetProperty("Scheduler", nonPublic);
                object scheduler = schedulerProp.GetValue(shootingCamera);
                PropertyInfo objectProp = typeof(NikonBase).GetProperty("Object", nonPublic);
                object nikonObject = objectProp.GetValue(shootingCamera);
                MethodInfo getGenericMethod = nikonObject.GetType().GetMethod("GetGeneric", nonPublic, null, new Type[] { typeof(eNkMAIDCapability), typeof(IntPtr) }, null);
                if (getGenericMethod == null) {
                    Logger.Error("Could not find NikonObject.GetGeneric method via reflection.");
                    ObjectMethodDump(nikonObject);
                    throw new InvalidOperationException("Could not locate internal method NikonObject.GetGeneric via reflection. See logs for details.");
                }
                Action getAction = () => {
                    getGenericMethod.Invoke(nikonObject, new object[] { eNkMAIDCapability.kNkMAIDCapability_GetFocusShiftCaptureInfo, ptr });
                };
                MethodInfo invokeMethod = scheduler.GetType().GetMethod("Invoke", nonPublic, null, new Type[] { typeof(Action) }, null);
                if (invokeMethod == null) {
                    Logger.Error("Could not find NikonObject.Invoke method via reflection.");
                    ObjectMethodDump(scheduler);
                    throw new InvalidOperationException("Failed to locate NikonScheduler.Invoke(Action).  See logs for details.");
                }
                invokeMethod.Invoke(scheduler, new object[] { getAction });
                NkMAIDGetFocusShiftCapInfo info = Marshal.PtrToStructure<NkMAIDGetFocusShiftCapInfo>(ptr);
                return info;
            } catch (Exception ex) {
                Logger.Debug("Exception thrown while trying to query the status of the focus shift shooting: " + ex.Message);
                throw;
            } finally {
                Marshal.FreeHGlobal(ptr);
            }
        }

        // Tries to provide more information in the logs in case of reflection failures by dumping all methods on the target object
        private static void ObjectMethodDump(object nikonObject) {
            Logger.Debug("--- Dumping all methods on object type: " + nikonObject.GetType().FullName + " ---");
            foreach (MethodInfo mi in nikonObject.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                Logger.Debug("  Method: " + mi.ReturnType.Name + " " + mi.Name + "(" + string.Join(", ", Array.ConvertAll(mi.GetParameters(), p => p.ParameterType.Name + " " + p.Name)) + ")");
            }
            Logger.Debug("--- End method dump ---");
        }

        // Utility method to zero out unmanaged memory before use, to prevent any garbage data issues when marshalling structs
        private static void ZeroMemory(IntPtr ptr, int size) {
            for (int i = 0; i < size; i++) {
                Marshal.WriteByte(ptr, i, 0);
            }
        }

        // This seems to happen without overridingthe inherited method.
        private void OnCaptureComplete(NikonDevice sender, int data) {
            CompletedShots = Interlocked.Increment(ref completedShots);
        }

        public bool Validate() {
            List<string> validationIssues = new List<string>();
            List<string> deviceErrors = DeviceAccessor.Validate(camera);
            validationIssues.AddRange(deviceErrors);
            if (deviceErrors.Count == 0) {
                theCam = DeviceAccessor.GetNikonDevice(camera);

                if (theCam != null) {
                    if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_CaptureFocusShift)) {
                        validationIssues.Add("Camera does not support focus shift shooting. This feature requires a compatible Nikon mirrorless camera (e.g., Z8, Z9).");
                    }
                    if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_GetFocusShiftCaptureInfo)) {
                        validationIssues.Add("Camera does not support retrieving focus shift shooting capture info. This feature requires a compatible Nikon mirrorless camera (e.g., Z8, Z9).");
                    }
                    if (!camera.GetInfo().Connected) {
                        validationIssues.Add("Camera is not connected");
                    }
                    if (theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_SpecialShootingMode)) {
                        try {
                            NikonEnum specialMode = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_SpecialShootingMode);
                            if (Convert.ToInt32(specialMode.Value) == 1) {
                                validationIssues.Add("Interval timer shooting mode is active.  Please stop the current intervalometer and try again.");
                            }

                        } catch (NikonException ex) {
                            validationIssues.Add("Could not read camera shooting mode. " + $"Error details: {ex.ErrorCode} - {ex.Message}");
                            Logger.Info("StartFocusShift Validate: Could not read SpecialShootingMode: " + $"Error details: {ex.ErrorCode} - {ex.Message}");
                        }
                    }
                }
            }
            Issues = validationIssues;
            return validationIssues.Count == 0;
        }

        public override object Clone() {
            return new StartFocusShift(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
            };
        }
    }
}