# Nikon Camera Settings

This is a plugin for NINA that allows users to change a few camera settings from the advanced sequencer.  For cameras that support the functionality, users can change:

1. The location where images are stored:
  - On the disk in the camera only
  - On the computer running NINA only
  - In both locations
2. Changing image stabilization/vibration reduction:
  - Off
  - On _(Note: This setting is dependent on the lens mounted to the camera as well.)_
  - Normal _(Note: This setting is dependent on the lens mounted to the camera as well.)_
  - Sport _(Note: This setting is dependent on the lens mounted to the camera as well.)_
3. Turning the camera monitor on/off:
  - Off
  - On
4. Embedding GPS, Astrometric, and Weather data into the images (only cameras that support IPTC data) (NMS IPTC data settings will be migrated into XmpIptc data settings)
5. Triggering Focus Shift Shooting
6. Starting Pixel Mapping
7. Turning Auto ISO adjustment on/off.

More settings will be exposed over time.  These were just settings that I wanted to be able to modify for my more immediate needs; _it never hurts to make sure image stabilization is turned off as part of your startup block of your sequence just to be sure it is off_.
Prior to requesting additional settings get exposed, please know that I can only test on Nikon Z8 and Nikon Z9 bodies.  There may be settings that your camera uses that are not documented in the SDKs that I am working from, but I can look into the other documentation as needed.  However, I still won't be able to test the code and would need to rely on you to test things and verify that they work with that camera body.
Additionally, unless there is a compelling use case, I have next to zero interest in adding functionality related to movies or external flash.  If you have a compelling use case, I will definitely consider it, but know that I will prioritize still photography focused work first.

For now, here is my TODO list over the longer term.  

# TODO List
The list is not yet prioritized, but these are settings that I imagine others may find helpful/useful to manipulate from the advanced sequencer as well.

- [ ] Capture Dust Off Reference Photo : kNkMAIDCapability_CaptureDustImage
- [x] Active D Lighting : kNkMAIDCapability_Active_D_Lighting
- [x] Vignette Control : kNkMAIDCapability_VignetteControl
- [x] Diffraction Compensation : kNkMAIDCapability_DiffractionCompensation
- [x] Auto Distortion Control : kNkMAIDCapability_AutoDistortion
- [x] Single Auto Focus Priority : kNkMAIDCapability_AFsPriority
- [ ] Extended Shutter Speeds Manual : kNkMAIDCapability_ExtendedShutterSpeedsManual
- [x] Shooting Mode : kNkMAIDCapability_ShootingMode
- [ ] Continuous Shooting # : kNkMAIDCapability_ContinuousShootingNum
- [x] Enable Copyright : kNkMAIDCapability_EnableCopyright
- [x] Artist Name : kNkMAIDCapability_ArtistName
- [x] Copyright Info : kNkMAIDCapability_CopyrightInfo
- [x] User Comment : kNkMAIDCapability_UserComment
- [x] Enable Comment : kNkMAIDCapability_EnableComment
- [x] Metering Mode : kNkMAIDCapability_MeteringMode
- [x] Exposure Mode : kNkMAIDCapability_ExposureMode
- [ ] Focus Area Mode : kNkMAIDCapability_FocusAreaMode
- [x] Enable Bracketing : kNkMAIDCapability_EnableBracketing
- [x] Starlight View : kNkMAIDCapability_StarlightView
- [x] Shutter Sound : kNkMAIDCapability_ShutterSoundEffect
- [x] Silent Mode : kNkMAIDCapability_SilentMode
- [x] Lock Camera : kNkMAIDCapability_LockCamera
- [x] Live View View Mode : kNkMAIDCapability_ViewMode
- [ ] Show Effects of Settings View Mode : kNkMAIDCapability_ViewModeShowEffectsOfSettings
- [x] Exposure Delay : kNkMAIDCapability_ExposureDelayEx
- [x] Focus mode restrictions : kNkMAIDCapability_AFModeRestrictions
- [x] File Number Sequence : kNkMAIDCapability_NumberingMode
- [ ] Reset File Number : kNkMAIDCapability_ResetFileNumber
- [x] Set Autofocus Mode : kNkMAIDCapability_AFModeAtLiveView
- [x] Set Autofocus Subject Detection : kNkMAIDCapability_AFSubjectDetection
- [ ] Continuous Shooting - Low Speed : kNkMAIDCapability_ShootingSpeed
- [ ] Continuous Shooting - High Speed : kNkMAIDCapability_ShootingSpeedHigh
- [x] Card Slot 2 Save Mode : kNkMAIDCapability_Slot2ImageSaveMode
- [ ] Lock Shutter Speed Setting : kNkMAIDCapability_ShutterSpeedLockSetting
- [ ] Lock Aperture Setting : kNkMAIDCapability_ApertureLockSetting
- [x] Select Menu Bank : kNkMAIDCapability_MenuBank
- [ ] Set Shooting Bank Name : kNkMAIDCapability_ShootingBankName
- [ ] Select Custom Settings : kNkMAIDCapability_CustomSettings
- [ ] Set Custom Shooting Bank Name : kNkMAIDCapability_CustomBankName
- [x] Image size : kNkMAIDCapability_CCDDataMode
- [x] Color Space : kNkMAIDCapability_ImageColorSpace
- [x] ISO Control : kNkMAIDCapability_IsoControl
- [x] Noise Reduction : kNkMAIDCapability_NoiseReduction
- [x] HighISONR : kNkMAIDCapability_NoiseReductionHighISO
- [x] Picture Control : kNkMAIDCapability_PictureControl
- [ ] Picture Control Data : kNkMAIDCapability_PictureControlDataEx2
- [x] HDR Mode : kNkMAIDCapability_HDRMode
- [ ] HDR Smoothing : kNkMAIDCapability_HDRSmoothing
- [ ] HDR Save Individual Images : kNkMAIDCapability_HDRSaveIndividualImages
- [x] Continuous Auto Focus Priority : kNkMAIDCapability_AFcPriority
- [ ] AF Area Selector : kNkMAIDCapability_AFAreaSelector
- [ ] Focus Points Used : kNkMAIDCapability_AFAreaPoint
- [x] Exposure Compensation Stop Size : kNkMAIDCapability_EVInterval
- [ ] Continuous Shooting Limit : kNkMAIDCapability_ShootingLimitEx
- [ ] Auto Bracketing Vary : kNkMAIDCapability_BracketingVary
- [ ] Bracketing Factor : kNkMAIDCapability_BracketingFactor
- [ ] Bracket Order : kNkMAIDCapability_BracketingOrder
- [ ] Capture with out memory card : kNkMAIDCapability_ShootNoCard
- [ ] Set Datetime : kNkMAIDCapability_ClockDateTime
- [ ] AE Bracket Step : kNkMAIDCapability_AEBracketingStep
- [x] Bracketing Type : kNkMAIDCapability_BracketingType
- [ ] Exp Compensation : kNkMAIDCapability_ExposureComp
- [ ] ADL Bracket Type : kNkMAIDCapability_ADLBracketingType
- [ ] ADL Bracket Step : kNkMAIDCapability_ADLBracketingStep
- [ ] Remain Continuous Shooting : kNkMAIDCapability_RemainContinuousShooting
- [ ] Remaining shots that can be stored in current media : kNkMAIDCapability_RemainCountInMedia
- [ ] Control Contrast Based AF during live view : kNkMAIDCapability_ContrastAF
- [ ] Adjust focus position step : kNkMAIDCapability_MFDriveStep
- [ ] Adjust focus position : kNkMAIDCapability_MFDrive
- [ ] Change Focus Point : kNkMAIDCapability_ContrastAFArea
- [x] Flicker Reduction : kNkMAIDCapability_FlickerReductionSetting
- [ ] High Freq Flicker Reduction : kNkMAIDCapability_HighFrequencyFlickerReduction
- [ ] Save Current Camera Settings : kNkMAIDCapability_SaveCameraSetting
- [ ] Center-weighted metering area : kNkMAIDCapability_CWMeteringDiameter
- [ ] Precapture pre-release burst : kNkMAIDCapability_PreCapturePreReleaseBurst
- [ ] Precapture post-release burst : kNkMAIDCapability_PreCapturePostReleaseBurst
- [ ] Get Flicker Reduction Shutter Speed : kNkMAIDCapability_FlickerReductionShutterSpeed
- [ ] Set Flicker REduction Shutter Speed : kNkMAIDCapability_SetFlickerReductionShutterSpeed


# NINA Changes to consider
A quick but brief list of some stuff that might make NINA usage a bit more seamless with the Nikon UX.

## Autorotating Live View/Image Preview

The `kNkMAIDCapability_CameraInclination` capability provides information about the camera orientation:

Value | Meaning |
----- | ------- |
0     | Horizontal or Unsettled |
1     | Vertical (Grip top) |
2     | Vertical (Grip bottom) |
3     | Horizontal (up down) |

This could be used to exchange the image size parameters and to rotate the matrices containing the pixel data to autorotate the display of information.


## Handling of Extended ISO Ranges
Currently, NINA only provides from the base (64) through to ISO 25600 for options.  There are, however, a few additional ISO values that could be supported, potentially easily, that I may look into.

- [ ] Lo-1.0: 32
- [ ] Lo-0.7: 40
- [ ] Lo-0.5: 45
- [ ] Lo-0.3: 50
- [ ] Hi0.3: 32000
- [ ] Hi0.5: 36000
- [ ] Hi0.7: 40000
- [ ] Hi1.0: 51200
- [ ] Hi2.0: 102400

