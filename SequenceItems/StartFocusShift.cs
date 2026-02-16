// =============================================================================
// FILE: SequenceItems/StartFocusShift.cs
// PURPOSE: NINA Advanced Sequencer instruction that initiates and monitors
//          Nikon focus shift shooting via the MAID SDK.
//
// Focus shift shooting captures a series of images while the camera
// automatically steps the focus distance between each shot. This is the
// primary technique for creating focus-stacked composite images in
// astrophotography and macro imaging. The number of shots, focus step
// width, and interval between exposures are configured in the camera's
// Photo Shooting Menu under "Focus shift shooting" prior to running
// this sequence item.
//
// SDK Flow:
// 1. CapStart kNkMAIDCapability_CaptureFocusShift with NkMAIDCaptureFocusShift
//    struct → camera begins shooting, returns initial remaining count
// 2. Poll kNkMAIDCapability_GetFocusShiftCaptureInfo via GetGeneric to read
//    NkMAIDGetFocusShiftCapInfo (status + remaining count) for UI updates
// 3. When ulStatus reports 0 (stopped), the sequence is complete
// 4. Cancellation sends CapStart on kNkMAIDCapability_TerminateStartFocusShift
//
// Design note: We use polling rather than the kNkMAIDEvent_OpenCaptureComplete
// event because the nikoncswrapper does not currently expose that event as a
// public delegate. Polling is robust here because the SDK confirms that
// GetFocusShiftCaptureInfo has NO cache (SDK section 14.1), meaning each
// call reads directly from the camera hardware.
//
// References:
// - MAID3Type0031_E.pdf sections 3.242, 3.244, 3.246, 3.247, 6.22, 14.1
// - nikoncswrapper: NikonDevice.Start(), NikonDevice.GetGeneric(),
//   NikonDevice.SupportsCapability()
// - NINA Plugin SDK: SequenceItem base class, IValidatable interface
//   https://github.com/isbeorn/nina
// =============================================================================

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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nikon;
using NikonCameraSettings.Utils;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;

namespace NikonCameraSettings.SequenceItems {

    // =========================================================================
    // StartFocusShift
    //
    // A NINA Advanced Sequencer instruction that triggers the Nikon camera's
    // built-in focus shift shooting mode. The camera must have focus shift
    // parameters (shot count, step width, interval) pre-configured in its
    // Photo Shooting Menu. This instruction starts the sequence, provides
    // live progress feedback in the sequencer UI, and waits for the camera
    // to report completion via status polling.
    //
    // The instruction supports cancellation, which sends the SDK's
    // TerminateStartFocusShift command to gracefully stop the camera.
    //
    // Visibility: Only shown in the sequencer if the connected Nikon camera
    // reports support for kNkMAIDCapability_CaptureFocusShift.
    // =========================================================================

    // MEF export metadata that defines how this instruction appears in NINA's sequencer
    // Reference: https://github.com/isbeorn/nina - NINA.Plugin README.md
    [ExportMetadata("Name", "Capture Focus Shift")]
    [ExportMetadata("Description", "Starts Nikon focus shift shooting and monitors progress. " +
        "Configure shot count, step width, and interval in the camera's Photo Shooting Menu first.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class StartFocusShift : SequenceItem, IValidatable {

        // =====================================================================
        // Fields
        // =====================================================================

        // Validation issues displayed in the NINA sequencer's validation panel
        private IList<string> issues = new List<string>();

        // NINA's camera mediator provides access to camera connection state
        private readonly ICameraMediator camera;

        // The underlying nikoncswrapper NikonDevice for direct SDK communication
        private NikonDevice theCam;

        // Tracks the initial total shot count for calculating progress percentage
        private ulong initialTotalShots;

        // =====================================================================
        // Bindable Properties (for XAML DataTemplate)
        // =====================================================================

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
        private ulong remainingShots;
        public ulong RemainingShots {
            get => remainingShots;
            set {
                remainingShots = value;
                // Triggers WPF binding update so the sequencer UI refreshes
                RaisePropertyChanged();
            }
        }

        // The total number of shots in the focus shift sequence (set at start)
        private ulong totalShots;
        public ulong TotalShots {
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

        // The number of shots completed so far, computed from total minus remaining.
        // This exists as a separate property to provide clean XAML ProgressBar binding
        // without requiring a custom IValueConverter for subtraction.
        private ulong completedShots;
        public ulong CompletedShots {
            get => completedShots;
            set {
                completedShots = value;
                // Triggers WPF binding update for the ProgressBar Value
                RaisePropertyChanged();
            }
        }

        // True while the focus shift sequence is actively running
        private bool isCapturing;
        public bool IsCapturing {
            get => isCapturing;
            set {
                isCapturing = value;
                // Triggers WPF binding update for visibility toggles in XAML
                RaisePropertyChanged();
            }
        }

        // =====================================================================
        // Constructor
        //
        // NINA's MEF composition injects ICameraMediator automatically.
        // Reference: NINA Plugin README.md - Constructor Injection section
        // =====================================================================
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

        // =====================================================================
        // Camera Connection Event Handlers
        // =====================================================================

        // When the camera connects, refresh the NikonDevice reference so that
        // SDK calls operate on the correct device instance
        private Task Camera_Connected(object sender, EventArgs e) {
            theCam = DeviceAccessor.GetNikonDevice(camera);
            return Task.CompletedTask;
        }

        // When the camera disconnects, clear the device reference to prevent
        // stale SDK calls that would throw NikonException
        private Task Camera_Disconnected(object sender, EventArgs e) {
            theCam = null;
            return Task.CompletedTask;
        }

        // =====================================================================
        // Execute - The main entry point called by NINA's sequencer engine
        //
        // This method orchestrates the entire focus shift capture workflow:
        // 1. Validate prerequisites
        // 2. Start focus shift shooting via the SDK
        // 3. Poll for progress updates until status == 0 (stopped)
        // 4. Handle cancellation gracefully
        //
        // The CancellationToken is provided by NINA and is triggered when the
        // user cancels the sequence or a safety condition fires.
        // =====================================================================
        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            // Re-acquire the NikonDevice in case the camera was reconnected
            theCam = DeviceAccessor.GetNikonDevice(camera);

            // Guard: ensure we have a valid device reference before proceeding
            if (theCam == null) {
                // Log the failure and show a user notification in NINA
                Logger.Error("FocusShift: No Nikon camera available.");
                Notification.ShowError("No Nikon camera connected. Cannot start focus shift shooting.");
                throw new SequenceItemSkippedException("No Nikon camera connected.");
            }

            // Guard: verify the camera firmware supports focus shift capability
            // Reference: NikonBase.SupportsCapability() in nikoncswrapper/Nikon.cs
            if (!theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_CaptureFocusShift)) {
                Logger.Error("StartFocusShift: Camera does not support focus shift shooting.");
                Notification.ShowError("This camera does not support focus shift shooting.");
                throw new SequenceItemSkippedException("Camera does not support focus shift shooting.");
            }

            // Guard: check that no other special shooting mode (interval timer or
            // existing focus shift) is currently active on the camera.
            // Per SDK section 3.242, starting focus shift during interval timer
            // returns kNkMAIDResult_NotSupported, but checking proactively gives
            // a better user experience with a clear error message.
            // Reference: SDK section 3.247 - SpecialShootingMode
            if (theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_SpecialShootingMode)) {
                try {
                    // Read the SpecialShootingMode enum value from the camera
                    NikonEnum specialMode = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_SpecialShootingMode);

                    // A non-zero current value means a special mode is already active
                    // 0 = Off, 1 = Interval Timer, 2 = Focus Shift
                    if (Convert.ToInt32(specialMode.Value) != 0) {
                        Logger.Warning("StartFocusShift: Special shooting mode already active " + $"(value={specialMode.Value}).");
                        Notification.ShowWarning("Another special shooting mode is already active. Please stop the current interval timer or focus shift sequence first.");
                        throw new SequenceItemSkippedException("Special shooting mode already active.");
                    }
                } catch (NikonException ex) {
                    // Non-fatal: if we can't read the mode, log and proceed -
                    // the SDK will return NotSupported if we can't actually start
                    Logger.Warning("StartFocusShift: Could not read SpecialShootingMode: " + ex.Message);
                }
            }

            // Mark the UI as actively capturing
            IsCapturing = true;
            StatusMessage = "Starting focus shift shooting...";

            try {
                // ============================================================
                // STEP 1: Start Focus Shift Shooting
                //
                // Allocate the NkMAIDCaptureFocusShift struct on the unmanaged
                // heap so the SDK can write the remaining shot count into it.
                // We use Marshal.AllocHGlobal because the SDK expects a native
                // pointer and may write to it during the CapStart call.
                //
                // Reference: SDK section 3.242 - StartFocusShift
                //   ulType = kNkMAIDCapType_Generic
                //   ulOperations = kNkMAIDCapOperation_Start
                //   Data = pointer to NkMAIDCaptureFocusShift
                // Reference: System.Runtime.InteropServices.Marshal
                //   https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshal.allochglobal
                // ============================================================

                // Calculate the byte size of the NkMAIDCaptureFocusShift struct
                int focusShiftStructSize = Marshal.SizeOf<NkMAIDCaptureFocusShift>();
                
                // Allocate unmanaged memory for the struct to start focus shift shooting and to read the info
                IntPtr pFocusShift = Marshal.AllocHGlobal(focusShiftStructSize);
                
                try {
                    // Zero-initialize the struct to prevent undefined values being
                    // sent to the SDK, then marshal it to the unmanaged pointer
                    NkMAIDCaptureFocusShift initStruct = new NkMAIDCaptureFocusShift();

                    initStruct.ulRemainingNumber = 0;
                    // Reference: https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshal.structuretoptr
                    Marshal.StructureToPtr(initStruct, pFocusShift, false);

                    // Issue the CapStart command to begin focus shift shooting.
                    // The camera returns immediately and begins shooting in the
                    // background. The struct is populated with the remaining count.
                    // Reference: NikonDevice.Start(cap, dataType, data) in nikoncswrapper/Nikon.cs
                    theCam.Start(eNkMAIDCapability.kNkMAIDCapability_CaptureFocusShift,
                                 eNkMAIDDataType.kNkMAIDDataType_GenericPtr, pFocusShift);

                    // Read back the struct populated by the SDK with the total number of shots the camera will take in this sequence
                    NkMAIDCaptureFocusShift result = Marshal.PtrToStructure<NkMAIDCaptureFocusShift>(pFocusShift);

                    // Store the initial total for progress percentage calculations
                    initialTotalShots = result.ulRemainingNumber;
                    TotalShots = initialTotalShots;
                    RemainingShots = initialTotalShots;

                    Logger.Info($"StartFocusShift: Started. Total shots: {initialTotalShots}");
                    StatusMessage = $"Focus shift active: {initialTotalShots} shots total.";
                } finally {
                    // Always free unmanaged memory to prevent memory leaks,
                    // even if the Start call throws an exception
                    Marshal.FreeHGlobal(pFocusShift);
                }

                // ============================================================
                // STEP 2: Monitor Progress via Polling Loop
                //
                // The SDK does not push per-shot progress events during focus
                // shift. Instead, we periodically call GetGeneric on
                // kNkMAIDCapability_GetFocusShiftCaptureInfo to read the
                // current status and remaining shot count from the camera.
                //
                // Critically, the SDK documentation (section 14.1) confirms that
                // GetFocusShiftCaptureInfo has NO cache in the module, meaning
                // each call reads directly from the camera hardware. This makes
                // polling a reliable method for monitoring progress.
                //
                // IMPORTANT: The camera may complete the focus shift with fewer
                // shots than requested. For example, if the user requests 15
                // shots but the camera's DOF algorithm determines that only 3
                // shots are needed to cover the depth of field, the camera will
                // stop after 3 shots. When this early completion occurs, the
                // kNkMAIDCapability_GetFocusShiftCaptureInfo capability becomes
                // invalid (the camera is no longer in focus shift mode), and
                // subsequent GetGeneric calls will fail.
                //
                // The failure manifests as a NullReferenceException thrown from
                // NikonWorkerThread.RethrowAsyncExceptionIfAny() — the SDK's
                // async completion callback stores an exception on the worker
                // thread, which is rethrown on the next synchronous SDK call.
                // This exception propagates as a raw System.NullReferenceException
                // (NOT a NikonException), so we must catch both types.
                //
                // Additionally, if we don't catch the NullReferenceException
                // promptly, it may be consumed by NINA's DeviceUpdateTimer
                // (which polls camera properties like Gain on a background
                // thread), causing errors in unrelated NINA subsystems.
                //
                // The loop exits when:
                //   a) ulStatus reports 0 (stopped) → capture finished normally
                //   b) ulRemainingNumber reaches 0 → all shots taken
                //   c) NullReferenceException caught → early completion detected
                //   d) The user cancels (CancellationToken triggered)
                //
                // Reference: SDK section 3.246 - GetFocusShiftCaptureInfo
                // Reference: SDK section 14.1 - Capabilities without cache
                // Reference: nikoncswrapper NikonWorkerThread.cs -
                //   RethrowAsyncExceptionIfAny() stores and rethrows exceptions
                //   from async callbacks on the next synchronous invocation
                // ============================================================

                // Calculate byte size of NkMAIDGetFocusShiftCapInfo (reused each poll)
                int infoStructSize = Marshal.SizeOf<NkMAIDGetFocusShiftCapInfo>();

                // Allocate unmanaged memory for the info struct once for all polls
                IntPtr pInfo = Marshal.AllocHGlobal(infoStructSize);

                // Track the last known remaining count so we can report accurate
                // completion numbers even when an early-completion exception
                // prevents reading the final state from the camera.
                ulong lastKnownRemaining = initialTotalShots;

                // Flag to indicate the camera completed early with fewer shots
                // than the user originally requested
                bool earlyCompletion = false;

                try {
                    // Track consecutive busy errors to detect if the camera is stuck
                    int consecutiveBusyCount = 0;

                    // The maximum consecutive busy retries before logging a warning
                    const int maxConsecutiveBusy = 30;

                    // Continue polling until completion or cancellation
                    while (!token.IsCancellationRequested) {
                        try {
                            // Zero the struct before each read to ensure clean state
                            NkMAIDGetFocusShiftCapInfo initInfo = new NkMAIDGetFocusShiftCapInfo();
                            Marshal.StructureToPtr(initInfo, pInfo, false);

                            // Read the focus shift capture info from the camera.
                            // This reads directly from hardware (no module cache).
                            // Reference: NikonDevice.GetGeneric(cap, dest) in
                            //   nikoncswrapper/Nikon.cs
                            theCam.GetGeneric(eNkMAIDCapability.kNkMAIDCapability_GetFocusShiftCaptureInfo, pInfo);

                            // Read the populated struct from unmanaged memory
                            NkMAIDGetFocusShiftCapInfo info = Marshal.PtrToStructure<NkMAIDGetFocusShiftCapInfo>(pInfo);

                            // Reset the busy counter since we got a successful read
                            consecutiveBusyCount = 0;

                            // Preserve the latest remaining count so we can use it
                            // for completion reporting if an exception occurs on
                            // a subsequent poll iteration
                            lastKnownRemaining = info.ulRemainingNumber;

                            // Update the UI-bound properties with current progress
                            RemainingShots = info.ulRemainingNumber;

                            // Calculate completed shots for the progress display.
                            // Guard against underflow in case the camera reports a
                            // remaining count larger than the initial total (which
                            // could happen if focus shift was restarted externally).
                            CompletedShots = (initialTotalShots > info.ulRemainingNumber) ? initialTotalShots - info.ulRemainingNumber : 0;

                            // Update the human-readable status message in the UI
                            StatusMessage = $"Focus shift: {CompletedShots} of " + $"{initialTotalShots} complete " + $"({info.ulRemainingNumber} remaining)";

                            int progshots;
                            int maxProgress;

                            try {
                                progshots = Convert.ToInt32(CompletedShots);
                                maxProgress = Convert.ToInt32(initialTotalShots);
                                // Report progress to NINA's application-wide status bar
                                // Down casting can get a bit risky.  
                                progress?.Report(new ApplicationStatus { Status = StatusMessage, Progress = progshots, MaxProgress = maxProgress,
                                                                         ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue });

                            } catch (OverflowException) {
                                // In the unlikely event of an overflow converting to int,
                                // log a warning and skip the progress update to avoid
                                // crashing NINA's UI thread. The status message will still
                                // show the correct counts.
                                Logger.Warning("StartFocusShift: Progress count overflow. " + $"CompletedShots={CompletedShots}, TotalShots={initialTotalShots}. Skipping progress update.");
                                // Make sure this doesn't create a fatal exception for this case.
                                theCam.ClearAsyncException();
                            }

                            // Check termination conditions:
                            // ulStatus == 0 means the camera has stopped shooting
                            // ulRemainingNumber == 0 means all shots are taken
                            if (info.ulStatus == 0 || (info.ulStatus == 1 && info.ulRemainingNumber == 0)) {
                                Logger.Info("StartFocusShift: Camera reports sequence " + $"complete (status={info.ulStatus}, remaining={info.ulRemainingNumber}).");
                                break;
                            }

                        } catch (NikonException ex) {
                            // DeviceBusy is expected during exposures - the camera
                            // cannot always respond to queries while the shutter
                            // is open
                            if (ex.ErrorCode == eNkMAIDResult.kNkMAIDResult_DeviceBusy) {
                                consecutiveBusyCount++;
                                Logger.Debug("StartFocusShift: Camera busy " + $"(attempt {consecutiveBusyCount}).");

                                // If the camera has been busy for an extended
                                // period, log a warning but continue - long
                                // exposures can block the bus for 30+ seconds
                                if (consecutiveBusyCount >= maxConsecutiveBusy) {
                                    Logger.Warning("StartFocusShift: Camera has been " + $"busy for {consecutiveBusyCount} " +
                                                   "consecutive polls. This may indicate a very long exposure or a problem.");
                                }
                            } else {
                                // For other SDK errors, log and continue polling.
                                // Transient errors during long focus shift
                                // sequences are expected (USB hiccups, etc.)
                                Logger.Warning("StartFocusShift: Polling error: " + $"{ex.ErrorCode} - {ex.Message}");
                            }
                            // Clear the exception
                            theCam.ClearAsyncException();
                        } catch (NullReferenceException) {
                            // ====================================================
                            // EARLY COMPLETION HANDLER
                            //
                            // A NullReferenceException from the SDK indicates that
                            // the focus shift completed and the camera exited focus
                            // shift mode BETWEEN our poll intervals. The exception
                            // originates from NikonWorkerThread
                            // .RethrowAsyncExceptionIfAny():
                            //
                            //   1. Camera finishes focus shift (e.g., DOF algorithm
                            //      determined 3 of 15 shots suffice)
                            //   2. SDK fires kNkMAIDEvent_OpenCaptureComplete
                            //      asynchronously on the worker thread
                            //   3. The async callback encounters a null reference
                            //      (capability context no longer valid) and stores
                            //      the exception on the worker thread
                            //   4. Our next GetGeneric call triggers
                            //      RethrowAsyncExceptionIfAny(), which rethrows
                            //      the stored exception as a raw
                            //      NullReferenceException (not NikonException)
                            //
                            // CRITICAL: We must catch this promptly. If we don't,
                            // the stored exception will be rethrown on the NEXT
                            // SDK call from ANY thread — including NINA's
                            // DeviceUpdateTimer polling NikonCamera.get_Gain(),
                            // causing unrelated errors in NINA's camera subsystem.
                            //
                            // Reference: nikoncswrapper NikonWorkerThread.cs
                            // Reference: SDK section 6.22 -
                            //   kNkMAIDEvent_OpenCaptureComplete
                            // ====================================================
                            Logger.Info("StartFocusShift: NullReferenceException from SDK worker thread — focus shift likely " +
                                        "completed early (camera DOF algorithm determined fewer shots were needed).");

                            // Set the early completion flag so the completion
                            // reporting section adjusts TotalShots to match
                            // the actual number captured
                            earlyCompletion = true;
                            theCam.ClearAsyncException();
                            // Attempt to confirm via SpecialShootingMode — if the
                            // camera is no longer in FocusShift mode, this is a
                            // definitive confirmation of early completion.
                            // Reference: SDK section 3.247 - SpecialShootingMode
                            try {
                                if (theCam.SupportsCapability(eNkMAIDCapability.kNkMAIDCapability_SpecialShootingMode)) {
                                    // Read the current shooting mode enum
                                    NikonEnum modeEnum = theCam.GetEnum(eNkMAIDCapability.kNkMAIDCapability_SpecialShootingMode);

                                    // Extract the currently selected value
                                    int modeValue = modeEnum.Index < modeEnum.Length ? Convert.ToInt32(modeEnum[modeEnum.Index]) : -1;

                                    // FocusShift == 2 per eNkMAIDSpecialShootingMode
                                    if (modeValue != (int)eNkMAIDSpecialShootingMode.kNkMAIDSpecialShootingMode_On_FocusShift) {
                                        Logger.Info("StartFocusShift: Confirmed — camera is no longer in focus " + $"shift mode (mode={modeValue}).");
                                    } else {
                                        Logger.Warning("StartFocusShift: Camera still reports focus shift mode despite NullReferenceException. Treating as complete to avoid further errors.");
                                    }
                                }
                            } catch (Exception modeEx) {
                                // If we can't read SpecialShootingMode either,
                                // the camera has definitely moved on from focus
                                // shift — this further confirms early completion
                                Logger.Debug("StartFocusShift: Could not read SpecialShootingMode after early " + $"completion: {modeEx.Message}");
                                theCam.ClearAsyncException();
                            }

                            // Exit the polling loop — the focus shift is done
                            break;
                        }

                        // Wait before the next poll iteration. 2 seconds provides
                        // a good balance between UI responsiveness and USB bus
                        // traffic. Focus shift exposures are typically 1–30+
                        // seconds each, so sub-second polling would produce
                        // unnecessary bus chatter.
                        try {
                            // Task.Delay with cancellation token ensures the delay
                            // is interrupted promptly when the user cancels
                            await Task.Delay(TimeSpan.FromSeconds(2), token);
                        } catch (TaskCanceledException) {
                            // Expected when the user cancels - exit the poll loop
                            break;
                        }
                    }
                } finally {
                    // Always free the unmanaged memory for the info struct
                    Marshal.FreeHGlobal(pInfo);
                }

                // ============================================================
                // STEP 3: Handle Cancellation
                //
                // If the user cancelled via NINA's UI, a safety trigger, or
                // a sequence condition, send the TerminateStartFocusShift
                // command to the camera to gracefully stop the focus shift.
                //
                // Reference: SDK section 3.244 - TerminateStartFocusShift
                //   ulType = kMAIDCapType_Process
                //   ulOperations = kNkMAIDCapOperation_Start
                //   Data = None (Process-type capability needs no data pointer)
                // ============================================================
                if (token.IsCancellationRequested) {
                    Logger.Info("StartFocusShift: Cancellation requested. Terminating focus shift on camera.");
                    StatusMessage = "Terminating focus shift...";

                    try {
                        // Send the termination command using the parameterless Start() overload since this is a Process-type capability
                        // Reference: NikonDevice.Start(cap) in nikoncswrapper/Nikon.cs
                        theCam.Start(eNkMAIDCapability.kNkMAIDCapability_TerminateCaptureFocusShift);
                        Logger.Info("StartFocusShift: Terminate command sent.");
                    } catch (NikonException ex) {
                        // NotSupported or other errors may occur if the camera already finished the sequence on its own, which is fine.
                        // The SDK also defines AlreadyTerminated (section 7.43) for this exact scenario.
                        Logger.Info("StartFocusShift: Terminate response: " + $"{ex.ErrorCode} - {ex.Message}");
                        theCam.ClearAsyncException();
                    } catch (NullReferenceException) {
                        // Same pattern as the polling loop — if the camera already exited focus shift mode, the terminate capability context
                        // is null and the SDK worker thread rethrows the stored async exception. This is benign: the camera already
                        // finished, so there is nothing to terminate.
                        Logger.Info("StartFocusShift: Terminate not needed — camera already exited focus shift mode.");
                        theCam.ClearAsyncException();
                    }

                    // Update the UI to reflect the cancelled state
                    StatusMessage = "Focus shift cancelled.";

                    // Clear NINA's progress bar
                    progress?.Report(new ApplicationStatus { Status = string.Empty });

                    // Throw OperationCanceledException so NINA's sequencer engine
                    // recognizes this as a user-initiated cancellation
                    token.ThrowIfCancellationRequested();

                }

                // ============================================================
                // STEP 4: Report Completion
                //
                // If we reach here, the polling loop exited normally (not
                // cancelled), meaning the camera completed the focus shift
                // sequence. The camera may have taken ALL requested shots or
                // may have stopped early if its DOF algorithm determined that
                // fewer shots were needed to cover the depth of field.
                //
                // For early completion, we adjust TotalShots downward to
                // match the actual number of shots captured. This ensures:
                //   - The XAML ProgressBar shows a full bar (100%) rather
                //     than a partially filled bar (e.g., 3/15)
                //   - The status message accurately reflects what happened
                //   - The NINA notification reports the true count
                // ============================================================

                // Calculate the actual number of shots captured using the
                // last known remaining count from the polling loop.
                // If early completion was detected (NullReferenceException),
                // lastKnownRemaining holds the last successfully read value.
                CompletedShots = (initialTotalShots > lastKnownRemaining) ? initialTotalShots - lastKnownRemaining : initialTotalShots;

                // For early completion, override TotalShots so the progress
                // bar shows 100% instead of a partial fill (e.g., 3/15).
                // This also updates the XAML ProgressBar.Maximum binding.
                if (earlyCompletion) {
                    // The camera's DOF algorithm decided fewer shots suffice.
                    // Adjust TotalShots to the actual count so the UI shows
                    // complete rather than a misleading partial progress.
                    TotalShots = initialTotalShots;
                    RemainingShots = initialTotalShots - CompletedShots;

                    Logger.Info($"StartFocusShift: Early completion — camera captured {CompletedShots} of {initialTotalShots} requested shots (DOF algorithm stopped early).");

                    StatusMessage = $"Focus shift complete! {CompletedShots} shots captured (camera determined {CompletedShots} of {initialTotalShots} requested were sufficient).";

                    // Show a success notification explaining early completion
                    Notification.ShowSuccess($"Focus shift complete: {CompletedShots} images captured ({initialTotalShots} requested — camera DOF algorithm stopped early).");
                } else {
                    // Normal completion — all requested shots were taken or
                    // the camera reported ulStatus == 0 / ulRemainingNumber == 0
                    ulong notTaken = (initialTotalShots > RemainingShots) ? initialTotalShots - CompletedShots : initialTotalShots;

                    StatusMessage = $"StartFocusShift: Completed. Shots - Captured: {CompletedShots}.  Remaining: {notTaken}.  Planned: {initialTotalShots}.";
                    Logger.Info(StatusMessage);

                    // Show a success notification in NINA's notification area
                    Notification.ShowSuccess(StatusMessage);
                }

                // Clear NINA's progress bar
                progress?.Report(new ApplicationStatus { Status = string.Empty });

            } finally {
                // Always reset the capturing state regardless of outcome.
                // This ensures the UI returns to its idle state even if an
                // unexpected exception occurred during execution.
                IsCapturing = false;
            }
        }

        // =====================================================================
        // Validation
        //
        // Called by NINA's sequencer to check if this instruction can run.
        // Issues are displayed as warning icons in the sequencer UI and
        // prevent the instruction from executing until resolved.
        //
        // Reference: IValidatable interface in NINA.Sequencer.Validations
        // =====================================================================
        public bool Validate() {
            // Start with a fresh issues list for this validation pass
            List<string> validationIssues = new List<string>();

            // Delegate camera connection and type validation to the shared utility.
            // This checks: camera connected, camera is Nikon, using Nikon driver.
            // Reference: NikonCameraSettings/Utils/DeviceAccessor.cs
            List<string> deviceErrors = DeviceAccessor.Validate(camera);
            validationIssues.AddRange(deviceErrors);

            // If the camera passed basic validation, check capability support
            if (deviceErrors.Count == 0) {
                // Re-acquire the device reference for fresh capability info
                theCam = DeviceAccessor.GetNikonDevice(camera);

                if (theCam != null) {
                    // Check if the camera firmware supports focus shift shooting.
                    // This capability is available on Z8, Z9, and other recent
                    // Nikon mirrorless cameras that support the Type0031 MAID module.
                    if (!theCam.SupportsCapability(
                            eNkMAIDCapability.kNkMAIDCapability_CaptureFocusShift)) {
                        validationIssues.Add(
                            "Camera does not support focus shift shooting. " +
                            "This feature requires a compatible Nikon mirrorless " +
                            "camera (e.g., Z8, Z9).");
                    }
                }
            }

            // Update the bindable Issues collection to trigger UI refresh
            Issues = validationIssues;

            // Return true if there are no issues (instruction is valid to run)
            return validationIssues.Count == 0;
        }

        // =====================================================================
        // Clone - Required by NINA's sequencer for copy/paste functionality
        //
        // Creates a deep copy of this instruction with the same configuration.
        // Reference: SequenceItem.Clone() in NINA.Sequencer
        // =====================================================================
        public override object Clone() {
            // Create a new instance using the MEF constructor pattern
            return new StartFocusShift(camera);
        }
    }
}