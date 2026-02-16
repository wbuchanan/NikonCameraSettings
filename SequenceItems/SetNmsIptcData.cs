// *****************************************************************************
// File: SequenceItems/SetNmsIptcData.cs
// Purpose: NINA Advanced Sequencer instruction that writes standard NMS-IPTC
//          metadata (14 fields) to a user-selected Nikon camera IPTC preset
//          slot (1-10) via the kNkMAIDCapability_IPTCPresetInfo SDK capability.
//
//          NMS-IPTC is the camera's native metadata format for presets 1-10.
//          The 14 fields (Caption, Event ID, Headline, Object Name, City, State,
//          Country, Category, Supplemental Categories, Byline, Byline Title,
//          Writer/Editor, Credit, Source) are entered by the user in the NINA
//          sequencer UI and embedded directly into images captured by the camera.
//
//          This sequence item complements SetGpsIptcData (XMP-IPTC slot 11) by
//          providing standard IPTC metadata that is recognized by all major photo
//          management applications (Adobe Lightroom, Bridge, Photo Mechanic, etc.).
//
// Architecture:
//   1. User enters IPTC field values in the sequencer UI (XAML DataTemplate)
//   2. NmsIptcData model validates and truncates fields to SDK limits
//   3. IptcPresetWriter.WriteNmsIptcPreset() marshals the binary structure
//   4. NikonDevice.SetGeneric() sends the data to the camera
//   5. Optionally activates the preset for auto-embedding during shooting
//
// References:
//   - Nikon SDK MAID3 Type0031 §3.260.2: IPTCPresetDataSet NMS-IPTC format
//   - Nikon SDK MAID3 Type0031 §3.263: IPTCPresetSelect auto-embed
//   - IPTC IIM Standard Rev 4.2: field definitions and semantics
//   - NikonCameraSettings SetGpsIptcData.cs: established plugin pattern
//   - NINA plugin template: https://github.com/isbeorn/nina.plugin.template
// *****************************************************************************

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
using NikonCameraSettings.Models;
using NikonCameraSettings.Utils;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;

namespace NikonCameraSettings.SequenceItems {

    // ---------------------------------------------------------------------------
    // MEF metadata attributes that define how this sequence item appears in the
    // NINA Advanced Sequencer's instruction palette. The item is placed in the
    // "Nikon Settings" category alongside SetGpsIptcData and other Nikon items.
    // Reference: NINA plugin template — MEF export pattern for ISequenceItem
    //   https://github.com/isbeorn/nina.plugin.template
    // ---------------------------------------------------------------------------
    [ExportMetadata("Name", "Set IPTC Data")]
    [ExportMetadata("Description", "Writes standard IPTC metadata (Caption, Headline, Byline, etc.) " +
        "to a Nikon camera's NMS-IPTC preset slot (1-10) for embedding into captured images.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Nikon Settings")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetNmsIptcData : SequenceItem, IValidatable {

        // ---------------------------------------------------------------------------
        // Validation issues list for the NINA sequencer UI.
        // Reference: NINA.Sequencer.Validations.IValidatable interface
        // ---------------------------------------------------------------------------
        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                // Notify the UI binding that the issues list has changed
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // NINA's camera mediator for obtaining the NikonDevice.
        // Reference: NINA.Equipment.Interfaces.Mediator.ICameraMediator
        // ---------------------------------------------------------------------------
        private readonly ICameraMediator camera;

        // ---------------------------------------------------------------------------
        // The NikonDevice handle for the connected camera.
        // Reference: NikonCameraSettings.Utils.DeviceAccessor
        // ---------------------------------------------------------------------------
        private NikonDevice theCam;

        // ===========================================================================
        //  USER-CONFIGURABLE PROPERTIES (serialized to JSON for sequence persistence)
        //
        //  These properties are bound to TextBox controls in the XAML DataTemplate.
        //  Each property corresponds to one of the 14 NMS-IPTC fields defined by
        //  the Nikon SDK MAID3 Type0031 §3.260.2 specification.
        //
        //  The [JsonProperty] attribute ensures values persist when the user saves
        //  and reloads their NINA sequence.
        // ===========================================================================

        // ---------------------------------------------------------------------------
        // Preset slot number (1-10) where the NMS-IPTC data will be written.
        // The user selects this in the UI. Defaults to 1 (first available slot).
        // Reference: Nikon SDK MAID3 Type0031 §3.260 "ulPresetNo: 1-10 for NMS-IPTC"
        // ---------------------------------------------------------------------------
        private int presetSlot = 1;

        [JsonProperty]
        public int PresetSlot {
            get => presetSlot;
            set {
                // Clamp the value to the valid range (1-10) to prevent SDK errors
                presetSlot = Math.Max(1, Math.Min(10, value));
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // Whether to activate this preset for auto-embedding during shooting.
        // When true, images captured after this instruction will have the NMS-IPTC
        // data automatically embedded by the camera firmware.
        // Note: Only one preset (1-13) can be auto-embedded at a time.
        // Reference: Nikon SDK MAID3 Type0031 §3.263 "IPTCPresetSelect"
        // ---------------------------------------------------------------------------
        private bool activatePreset = true;

        [JsonProperty]
        public bool ActivatePreset {
            get => activatePreset;
            set {
                activatePreset = value;
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:120 — Caption/Abstract: A textual description of the content.
        // UTF-8, max 2000 bytes. The largest NMS-IPTC field.
        // ---------------------------------------------------------------------------
        private string caption = "";

        [JsonProperty]
        public string Caption {
            get => caption;
            set {
                caption = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:12 — Event Identifier: A unique session or event identifier.
        // UTF-8, max 64 bytes.
        // ---------------------------------------------------------------------------
        private string eventId = "";

        [JsonProperty]
        public string EventId {
            get => eventId;
            set {
                eventId = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:105 — Headline: A brief synopsis of the content.
        // UTF-8, max 256 bytes.
        // ---------------------------------------------------------------------------
        private string headline = "";

        [JsonProperty]
        public string Headline {
            get => headline;
            set {
                headline = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:05 — Object Name (Title): The formal name/title of the image.
        // UTF-8, max 256 bytes. This is the primary title shown in photo catalogs.
        // ---------------------------------------------------------------------------
        private string objectName = "";

        [JsonProperty]
        public string ObjectName {
            get => objectName;
            set {
                objectName = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:90 — City: The city of the observing/imaging location.
        // UTF-8, max 256 bytes.
        // ---------------------------------------------------------------------------
        private string city = "";

        [JsonProperty]
        public string City {
            get => city;
            set {
                city = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:95 — Province/State: The state or province of the location.
        // UTF-8, max 256 bytes.
        // ---------------------------------------------------------------------------
        private string state = "";

        [JsonProperty]
        public string State {
            get => state;
            set {
                state = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:101 — Country/Primary Location Name: The country name.
        // UTF-8, max 256 bytes.
        // ---------------------------------------------------------------------------
        private string country = "";

        [JsonProperty]
        public string Country {
            get => country;
            set {
                country = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:15 — Category: A 3-character subject category code.
        // ASCII only, max 3 bytes (v2.00 firmware). Common codes: "SCI", "ENV".
        //
        // Named "IptcCategory" to avoid shadowing SequenceItem.Category which is
        // used by NINA's MEF metadata system for the instruction palette grouping.
        // ---------------------------------------------------------------------------
        private string iptcCategory = "";

        [JsonProperty]
        public string IptcCategory {
            get => iptcCategory;
            set {
                iptcCategory = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:20 — Supplemental Categories: Additional categorization text.
        // UTF-8, max 256 bytes. E.g., "Deep Sky", "Planetary", "Widefield".
        // ---------------------------------------------------------------------------
        private string suppCat = "";

        [JsonProperty]
        public string SuppCat {
            get => suppCat;
            set {
                suppCat = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:80 — Byline (Creator/Author): The photographer's name.
        // UTF-8, max 256 bytes.
        // ---------------------------------------------------------------------------
        private string byline = "";

        [JsonProperty]
        public string Byline {
            get => byline;
            set {
                byline = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:85 — Byline Title: The creator's job title or role.
        // UTF-8, max 256 bytes. E.g., "Astrophotographer".
        // ---------------------------------------------------------------------------
        private string bylineTitle = "";

        [JsonProperty]
        public string BylineTitle {
            get => bylineTitle;
            set {
                bylineTitle = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:122 — Writer/Editor: Who wrote or edited the caption.
        // UTF-8, max 256 bytes.
        // ---------------------------------------------------------------------------
        private string writerEditor = "";

        [JsonProperty]
        public string WriterEditor {
            get => writerEditor;
            set {
                writerEditor = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:110 — Credit: The provider or owner of the image.
        // UTF-8, max 256 bytes.
        // ---------------------------------------------------------------------------
        private string credit = "";

        [JsonProperty]
        public string Credit {
            get => credit;
            set {
                credit = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------------------
        // IPTC IIM 2:115 — Source: The original source of the image.
        // UTF-8, max 256 bytes. E.g., the telescope or observatory name.
        // ---------------------------------------------------------------------------
        private string source = "";

        [JsonProperty]
        public string Source {
            get => source;
            set {
                source = value ?? "";
                RaisePropertyChanged();
            }
        }

        // ===========================================================================
        //  CONSTRUCTOR AND EVENT HANDLERS
        // ===========================================================================

        // ---------------------------------------------------------------------------
        // Constructor with MEF dependency injection. NINA's MEF container
        // automatically resolves ICameraMediator when instantiating this item.
        // Reference: NINA plugin template — ImportingConstructor pattern
        //   https://github.com/isbeorn/nina.plugin.template
        // ---------------------------------------------------------------------------
        [ImportingConstructor]
        public SetNmsIptcData(ICameraMediator camera) {
            // Store the camera mediator for obtaining the NikonDevice
            this.camera = camera;
            // Attempt to get the NikonDevice if a camera is already connected
            theCam = DeviceAccessor.GetNikonDevice(this.camera);

            // -----------------------------------------------------------------------
            // Subscribe to camera connect/disconnect events to refresh the device
            // handle, following the same pattern as SetGpsIptcData.
            // Reference: NikonCameraSettings SetImageStabilization.cs
            // -----------------------------------------------------------------------
            this.camera.Connected += Camera_Connected;
            this.camera.Disconnected += Camera_Disconnected;
        }

        // ---------------------------------------------------------------------------
        // Handle camera connection: refresh the NikonDevice handle.
        // ---------------------------------------------------------------------------
        private Task Camera_Connected(object sender, EventArgs args) {
            // Obtain a fresh NikonDevice reference for the newly connected camera
            theCam = DeviceAccessor.GetNikonDevice(this.camera);
            Logger.Debug("SetNmsIptcData: Camera connected, NikonDevice refreshed.");
            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------------------
        // Handle camera disconnection: clear the NikonDevice handle.
        // ---------------------------------------------------------------------------
        private Task Camera_Disconnected(object sender, EventArgs args) {
            // Null out the device reference since the camera is no longer available
            theCam = null;
            Logger.Debug("SetNmsIptcData: Camera disconnected.");
            return Task.CompletedTask;
        }

        // ===========================================================================
        //  VALIDATION
        // ===========================================================================

        // ---------------------------------------------------------------------------
        // Validates that all preconditions are met before the sequence item executes.
        // NINA calls this method to check whether the instruction can run.
        //
        // Preconditions checked:
        //   1. A camera is connected
        //   2. The connected camera is a Nikon
        //   3. At least one IPTC field has content
        //   4. The preset slot is in the valid range (1-10)
        //
        // Reference: NINA.Sequencer.Validations.IValidatable interface
        // ---------------------------------------------------------------------------
        public bool Validate() {
            // Start with a fresh list of issues for each validation pass
            List<string> i = new List<string>();

            // Check if any camera is connected to NINA
            if (!camera.GetInfo().Connected) {
                i.Add("Camera is not connected.");
            } else {
                // Use the existing DeviceAccessor.Validate pattern to check for Nikon
                List<string> deviceErrors = DeviceAccessor.Validate(camera);
                // Add any device validation errors (e.g., "not a Nikon camera")
                i.AddRange(deviceErrors);
            }

            // Check that the preset slot is in the valid NMS-IPTC range
            if (PresetSlot < 1 || PresetSlot > 10) {
                i.Add("Preset slot must be between 1 and 10 for standard IPTC data.");
            }

            // Build the data model to check if any fields have content
            NmsIptcData data = BuildNmsIptcData();
            if (!data.HasContent()) {
                i.Add("No IPTC fields have been filled in. Enter at least one field value.");
            }

            // Update the Issues property (triggers UI refresh via RaisePropertyChanged)
            Issues = i;
            return i.Count == 0;
        }

        // ===========================================================================
        //  EXECUTION
        // ===========================================================================

        // ---------------------------------------------------------------------------
        // Executes the sequence item: builds NmsIptcData from the UI properties,
        // then writes it to the selected camera preset slot via IptcPresetWriter.
        //
        // Execution flow:
        //   1. Build the NmsIptcData model from the UI-bound properties
        //   2. Validate that at least one field has content
        //   3. Ensure we have a valid NikonDevice reference
        //   4. Write the NMS-IPTC data to the selected preset slot
        //   5. Optionally activate the preset for auto-embedding
        //
        // Reference: NINA.Sequencer.SequenceItem.Execute override pattern
        // ---------------------------------------------------------------------------
        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            // -----------------------------------------------------------------------
            // Step 1: Build the NmsIptcData model from the current UI property values.
            // The model validates and truncates fields to their maximum byte lengths.
            // -----------------------------------------------------------------------
            NmsIptcData data = BuildNmsIptcData();

            // -----------------------------------------------------------------------
            // Step 2: Validate that there is meaningful data to write.
            // -----------------------------------------------------------------------
            if (!data.HasContent()) {
                Logger.Warning("SetNmsIptcData: No IPTC fields have content. Skipping write.");
                return Task.CompletedTask;
            }

            // -----------------------------------------------------------------------
            // Step 3: Ensure we have a valid NikonDevice reference.
            // -----------------------------------------------------------------------
            if (theCam == null) {
                theCam = DeviceAccessor.GetNikonDevice(camera);
            }
            if (theCam == null) {
                Logger.Error("SetNmsIptcData: Could not obtain NikonDevice. Is a Nikon camera connected?");
                return Task.CompletedTask;
            }

            // -----------------------------------------------------------------------
            // Step 4: Write the NMS-IPTC data to the camera via IptcPresetWriter.
            // The writer handles binary marshaling and SDK communication.
            // -----------------------------------------------------------------------
            try {
                // Cast the int preset slot to uint for the SDK method
                uint slot = (uint)PresetSlot;

                // Build a profile name from the slot number for identification
                string profileName = $"NINA Slot {PresetSlot}";

                // Write the NMS-IPTC data to the selected preset slot
                IptcPresetWriter.WriteNmsIptcPreset(theCam, slot, profileName, data);

                // -------------------------------------------------------------------
                // Step 5: If configured, activate the preset for auto-embedding.
                // This tells the camera to embed this preset's IPTC data into every
                // image captured until a different preset is selected or it is turned off.
                // Note: This will override any previously active preset (e.g., GPS on slot 11).
                // Reference: Nikon SDK MAID3 Type0031 §3.263
                // -------------------------------------------------------------------
                if (ActivatePreset) {
                    IptcPresetWriter.ActivatePresetForAutoEmbed(theCam, slot);
                }

                Logger.Info($"SetNmsIptcData: Successfully wrote IPTC data to preset slot {PresetSlot}. {data}");
            } catch (NikonException nex) {
                Logger.Error($"SetNmsIptcData: Nikon SDK error: {nex.ErrorCode} - {nex.Message}");
            } catch (Exception ex) {
                Logger.Error($"SetNmsIptcData: Unexpected error: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        // ===========================================================================
        //  HELPER METHODS
        // ===========================================================================

        // ---------------------------------------------------------------------------
        // Constructs an NmsIptcData model from the current UI-bound property values.
        // The NmsIptcData constructor handles null-to-empty conversion and byte-length
        // truncation for each field per the Nikon SDK specifications.
        // ---------------------------------------------------------------------------
        private NmsIptcData BuildNmsIptcData() {
            return new NmsIptcData(
                caption: Caption,
                eventId: EventId,
                headline: Headline,
                objectName: ObjectName,
                city: City,
                state: State,
                country: Country,
                category: IptcCategory,
                suppCat: SuppCat,
                byline: Byline,
                bylineTitle: BylineTitle,
                writerEditor: WriterEditor,
                credit: Credit,
                source: Source);
        }

        // ---------------------------------------------------------------------------
        // Creates a deep clone of this sequence item for NINA's sequencer copy
        // operations. All 14 IPTC fields and configuration options are copied.
        // Reference: NikonCameraSettings SetGpsIptcData.cs — Clone pattern
        // ---------------------------------------------------------------------------
        public override object Clone() {
            return new SetNmsIptcData(this.camera) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                // Copy configuration options
                PresetSlot = PresetSlot,
                ActivatePreset = ActivatePreset,
                // Copy all 14 IPTC field values
                Caption = Caption,
                EventId = EventId,
                Headline = Headline,
                ObjectName = ObjectName,
                City = City,
                State = State,
                Country = Country,
                IptcCategory = IptcCategory,
                SuppCat = SuppCat,
                Byline = Byline,
                BylineTitle = BylineTitle,
                WriterEditor = WriterEditor,
                Credit = Credit,
                Source = Source,
            };
        }
    }
}