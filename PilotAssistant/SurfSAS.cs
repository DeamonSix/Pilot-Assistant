﻿using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

namespace PilotAssistant
{
    using PID;
    using Utility;
    using Presets;

    [Flags]
    public enum SASList
    {
        Pitch,
        Roll,
        Yaw
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class SurfSAS : MonoBehaviour
    {
        private static SurfSAS instance;
        public static SurfSAS Instance
        {
            get { return instance; }
        }

        public static PID_Controller[] SASControllers = new PID_Controller[3];

        static bool bInit = false;
        bool bArmed = false;
        bool[] bActive = new bool[3]; // activate on per axis basis
        bool[] bPause = new bool[3]; // pause on a per axis basis
        public bool bStockSAS = true;

        public float[] fadeReset = new float[3];

        float activationFadeRoll = 1;
        float activationFadePitch = 1;
        float activationFadeYaw = 1;

        float fadeRollMult = 0.98f;
        float fadePitchMult = 0.98f;
        float fadeYawMult = 0.98f;

        bool rollState = false; // false = surface mode, true = vector mode

        Rect SASwindow = new Rect(10, 505, 200, 30);

        bool[] stockPIDDisplay = { true, false, false };

        string newPresetName = "";
        Rect SASPresetwindow = new Rect(550, 50, 50, 50);
        bool bShowPresets = false;

        public static double[] defaultPitchGains = { 0.15, 0.0, 0.06, -1, 1, -1, 1, 3 };
        public static double[] defaultRollGains = { 0.1, 0.0, 0.06, -1, 1, -1, 1, 3 };
        public static double[] defaultYawGains = { 0.15, 0.0, 0.06, -1, 1, -1, 1, 3 };

        public static double[] defaultPresetPitchGains = { 0.15, 0.0, 0.06, 3, 10 };
        public static double[] defaultPresetRollGains = { 0.1, 0.0, 0.06, 3, 10 };
        public static double[] defaultPresetYawGains = { 0.15, 0.0, 0.06, 3, 10 };

        public void Start()
        {
            instance = this;

            StartCoroutine(Initialise());

            RenderingManager.AddToPostDrawQueue(5, drawGUI);
            FlightData.thisVessel.OnAutopilotUpdate += new FlightInputCallback(SurfaceSAS);
            GameEvents.onVesselChange.Add(vesselSwitch);
        }

        private void vesselSwitch(Vessel v)
        {
            FlightData.thisVessel.OnAutopilotUpdate -= new FlightInputCallback(SurfaceSAS);
            FlightData.thisVessel = v;
            FlightData.thisVessel.OnAutopilotUpdate += new FlightInputCallback(SurfaceSAS);

            StartCoroutine(Initialise());
        }

        // need to wait for Stock SAS to be ready, hence the Coroutine
        IEnumerator Initialise()
        {
            if (FlightData.thisVessel == null)
                FlightData.thisVessel = FlightGlobals.ActiveVessel;

            // wait for SAS to init
            if (FlightData.thisVessel.Autopilot.SAS.pidLockedPitch == null)
                yield return null;
            
            bPause[0] = bPause[1] = bPause[2] = false;
            ActivitySwitch(false);

            if (!bInit)
            {
                SASControllers[(int)SASList.Pitch] = new PID_Controller(defaultPitchGains);
                SASControllers[(int)SASList.Roll] = new PID_Controller(defaultRollGains);
                SASControllers[(int)SASList.Yaw] = new PID_Controller(defaultYawGains);

                if (!PresetManager.Instance.craftPresetList.ContainsKey("default"))
                    PresetManager.Instance.craftPresetList.Add("default", new CraftPreset("default", null, new SASPreset(SASControllers, "SSAS"), new SASPreset(FlightData.thisVessel.Autopilot.SAS, "stock"), bStockSAS));
                else
                {
                    if (PresetManager.Instance.craftPresetList["default"].SSASPreset == null)
                        PresetManager.Instance.craftPresetList["default"].SSASPreset = new SASPreset(SASControllers, "SSAS");
                    if (PresetManager.Instance.craftPresetList["default"].StockPreset == null)
                        PresetManager.Instance.craftPresetList["default"].StockPreset = new SASPreset(FlightData.thisVessel.Autopilot.SAS, "stock");
                }
                PresetManager.saveDefaults();

                GeneralUI.InitColors();
                bInit = true;
            }
            PresetManager.loadCraftSSASPreset();
            PresetManager.loadCraftStockPreset();
        }

        public void OnDestroy()
        {
            bInit = false;
            bArmed = false;
            ActivitySwitch(false);

            RenderingManager.RemoveFromPostDrawQueue(5, drawGUI);
            FlightData.thisVessel.OnAutopilotUpdate -= new FlightInputCallback(SurfaceSAS);
            GameEvents.onVesselChange.Remove(vesselSwitch);
        }

        public void Update()
        {
            bool mod = GameSettings.MODIFIER_KEY.GetKey();
            // Arm Hotkey
            if (mod && GameSettings.SAS_TOGGLE.GetKeyDown())
            {
                bArmed = !bArmed;
                bStockSAS = false;
                if (ActivityCheck())
                {
                    ActivitySwitch(false);
                }
            }

            // SAS activated by user
            if (bArmed && !ActivityCheck() && GameSettings.SAS_TOGGLE.GetKeyDown() && !mod)
            {
                if (!bStockSAS)
                {
                    ActivitySwitch(true);
                    setStockSAS(false);
                    updateTarget();
                }
            }
            else if (ActivityCheck() && GameSettings.SAS_TOGGLE.GetKeyDown() && !mod)
            {
                ActivitySwitch(false);
                setStockSAS(bStockSAS);
            }

            if (GameSettings.SAS_HOLD.GetKeyDown())
                updateTarget();
        }

        public void drawGUI()
        {
            GUI.skin = GeneralUI.UISkin;
            GeneralUI.Styles();

            // SAS toggle button
            if (bArmed)
            {
                if (SurfSAS.ActivityCheck())
                    GUI.backgroundColor = GeneralUI.ActiveBackground;
                else
                    GUI.backgroundColor = GeneralUI.InActiveBackground;

                if (GUI.Button(new Rect(Screen.width / 2 + 50, Screen.height - 200, 50, 30), "SSAS"))
                {
                    ActivitySwitch(!ActivityCheck());
                    updateTarget();
                    if (ActivityCheck())
                        setStockSAS(false);
                }
                GUI.backgroundColor = GeneralUI.stockBackgroundGUIColor;
            }
            
            // Main and preset window stuff
            if (!AppLauncherFlight.bDisplaySAS)
                return;
            Draw();
        }

        private void SurfaceSAS(FlightCtrlState state)
        {
            if (bArmed)
            {
                FlightData.updateAttitude();

                pauseManager(state);

                float vertResponse = 0;
                if (bActive[(int)SASList.Pitch])
                    vertResponse = -1 * (float)Utils.GetSAS(SASList.Pitch).ResponseD(FlightData.pitch);

                float hrztResponse = 0;
                if (bActive[(int)SASList.Yaw] && (FlightData.thisVessel.latitude < 88 && FlightData.thisVessel.latitude > -88))
                {
                    if (Utils.GetSAS(SASList.Yaw).SetPoint - FlightData.heading >= -180 && Utils.GetSAS(SASList.Yaw).SetPoint - FlightData.heading <= 180)
                        hrztResponse = -1 * (float)Utils.GetSAS(SASList.Yaw).ResponseD(FlightData.heading);
                    else if (Utils.GetSAS(SASList.Yaw).SetPoint - FlightData.heading < -180)
                        hrztResponse = -1 * (float)Utils.GetSAS(SASList.Yaw).ResponseD(FlightData.heading - 360);
                    else if (Utils.GetSAS(SASList.Yaw).SetPoint - FlightData.heading > 180)
                        hrztResponse = -1 * (float)Utils.GetSAS(SASList.Yaw).ResponseD(FlightData.heading + 360);
                }
                else
                {
                    Utils.GetSAS(SASList.Yaw).SetPoint = FlightData.heading;
                }

                double rollRad = Math.PI / 180 * FlightData.roll;

                if ((!bPause[(int)SASList.Pitch] || !bPause[(int)SASList.Yaw]) && (bActive[(int)SASList.Pitch] || bActive[(int)SASList.Yaw]))
                {                    
                    state.pitch = (vertResponse * (float)Math.Cos(rollRad) - hrztResponse * (float)Math.Sin(rollRad)) / activationFadePitch;
                    state.yaw = (vertResponse * (float)Math.Sin(rollRad) + hrztResponse * (float)Math.Cos(rollRad)) / activationFadeYaw;
                }
                rollResponse();
            }
        }

        private void updateTarget()
        {
            if (rollState)
                Utils.GetSAS(SASList.Roll).SetPoint = 0;
            else
                Utils.GetSAS(SASList.Roll).SetPoint = FlightData.roll;

            Utils.GetSAS(SASList.Pitch).SetPoint = FlightData.pitch;
            Utils.GetSAS(SASList.Yaw).SetPoint = FlightData.heading;

            rollTarget = FlightData.thisVessel.ReferenceTransform.right;

            activationFadeRoll = fadeReset[(int)SASList.Roll];
            activationFadePitch = fadeReset[(int)SASList.Pitch];
            activationFadeYaw = fadeReset[(int)SASList.Yaw];
        }

        private void pauseManager(FlightCtrlState state)
        {
            if (state.pitch != 0 && !bPause[(int)SASList.Pitch])
                bPause[(int)SASList.Pitch] = bPause[(int)SASList.Yaw] = true;
            else if (state.pitch == 0 && bPause[(int)SASList.Pitch])
            {
                if (state.yaw == 0)
                    bPause[(int)SASList.Pitch] = bPause[(int)SASList.Yaw] = false;

                if (bActive[(int)SASList.Pitch])
                {
                    activationFadePitch = fadeReset[(int)SASList.Pitch];
                    if (!pitchEnum)
                        StartCoroutine(FadeInPitch());
                }
            }
            
            if (state.roll != 0 && !bPause[(int)SASList.Roll])
                bPause[(int)SASList.Roll] = true;
            else if (state.roll == 0 && bPause[(int)SASList.Roll])
            {
                bPause[(int)SASList.Roll] = false;
                if (bActive[(int)SASList.Roll])
                {
                    activationFadeRoll = fadeReset[(int)SASList.Roll];
                    if (!rollEnum)
                        StartCoroutine(FadeInRoll());
                }
            }

            if (state.yaw != 0 && !bPause[(int)SASList.Yaw])
                bPause[(int)SASList.Yaw] = bPause[(int)SASList.Pitch]= true;
            else if (state.yaw == 0 && bPause[(int)SASList.Yaw])
            {
                if (state.pitch == 0)
                    bPause[(int)SASList.Pitch] = bPause[(int)SASList.Yaw] = false;

                if (bActive[(int)SASList.Yaw])
                {
                    activationFadeYaw = fadeReset[(int)SASList.Yaw];
                    if (!yawEnum)
                        StartCoroutine(FadeInYaw());
                }
            }
        }

        bool pitchEnum = false;
        IEnumerator FadeInPitch()
        {
            pitchEnum = true;
            while (activationFadePitch > 1)
            {
                yield return new WaitForFixedUpdate();

                float nextFade = activationFadePitch * 0.98f;
                float thresh = Mathf.Floor(activationFadePitch);
                if (nextFade < thresh)
                {
                    Utils.GetSAS(SASList.Yaw).SetPoint = FlightData.heading;
                    Utils.GetSAS(SASList.Pitch).SetPoint = FlightData.pitch;
                }
                activationFadePitch = nextFade;
            }
            activationFadePitch = 1;
            pitchEnum = false;
        }

        bool rollEnum = false;
        IEnumerator FadeInRoll()
        {
            rollEnum = true;
            while (activationFadeRoll > 1)
            {
                yield return new WaitForFixedUpdate();

                float nextFade = activationFadeRoll * 0.98f;
                float thresh = Mathf.Floor(activationFadeRoll);
                if (nextFade < thresh)
                {
                    if (rollState)
                        rollTarget = FlightData.thisVessel.ReferenceTransform.right;
                    else
                        Utils.GetSAS(SASList.Roll).SetPoint = FlightData.roll;
                }
                activationFadeRoll = nextFade;
            }
            activationFadeRoll = 1;
            rollEnum = false;
        }

        bool yawEnum = false;
        IEnumerator FadeInYaw()
        {
            yawEnum = true;
            while (activationFadeYaw > 1)
            {
                yield return new WaitForFixedUpdate();

                float nextFade = activationFadeYaw * 0.98f;
                float thresh = Mathf.Floor(activationFadeYaw);
                if (nextFade < thresh)
                {
                    Utils.GetSAS(SASList.Yaw).SetPoint = FlightData.heading;
                    Utils.GetSAS(SASList.Pitch).SetPoint = FlightData.pitch;
                }
                activationFadeYaw = nextFade;
            }
            activationFadeYaw = 1;
            yawEnum = false;
        }

        internal static void ActivitySwitch(bool enable)
        {
            if (enable)
                instance.bActive[(int)SASList.Pitch] = instance.bActive[(int)SASList.Roll] = instance.bActive[(int)SASList.Yaw] = true;
            else
                instance.bActive[(int)SASList.Pitch] = instance.bActive[(int)SASList.Roll] = instance.bActive[(int)SASList.Yaw] = false;
        }

        internal static bool ActivityCheck()
        {
            if (instance.bActive[(int)SASList.Pitch] || instance.bActive[(int)SASList.Roll] || instance.bActive[(int)SASList.Yaw])
                return true;
            else
                return false;
        }

        internal static void setStockSAS(bool state)
        {
            FlightData.thisVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, state);
            FlightData.thisVessel.ctrlState.killRot = state; // incase anyone checks the ctrl state (should be using checking vessel.ActionGroup[KSPActionGroup.SAS])
        }


        static Vector3d rollTarget = Vector3d.zero;
        private void rollResponse()
        {
            if (!bPause[(int)SASList.Roll] && bActive[(int)SASList.Roll])
            {
                bool rollStateWas = rollState;
                // switch tracking modes
                if (rollState) // currently in vector mode
                {
                    if (FlightData.pitch < 25 && FlightData.pitch > -25)
                        rollState = false; // fall back to surface mode
                }
                else // surface mode
                {
                    if (FlightData.pitch > 30 || FlightData.pitch < -30)
                        rollState = true; // go to vector mode
                }

                // Above 30 degrees pitch, rollTarget should always lie on the horizontal plane of the vessel
                // Below 25 degrees pitch, use the surf roll logic
                // hysteresis on the switch ensures it doesn't bounce back and forth and lose the lock
                if (rollState)
                {
                    if (!rollStateWas)
                    {
                        Utils.GetSAS(SASList.Roll).SetPoint = 0;
                        Utils.GetSAS(SASList.Roll).skipDerivative = true;
                        rollTarget = FlightData.thisVessel.ReferenceTransform.right;
                    }

                    Vector3 proj = FlightData.thisVessel.ReferenceTransform.up * Vector3.Dot(FlightData.thisVessel.ReferenceTransform.up, rollTarget)
                        + FlightData.thisVessel.ReferenceTransform.right * Vector3.Dot(FlightData.thisVessel.ReferenceTransform.right, rollTarget);
                    double roll = Vector3.Angle(proj, rollTarget) * Math.Sign(Vector3.Dot(FlightData.thisVessel.ReferenceTransform.forward, rollTarget));

                    FlightData.thisVessel.ctrlState.roll = (float)Utils.GetSAS(SASList.Roll).ResponseD(roll) / activationFadeRoll;
                }
                else
                {
                    if (rollStateWas)
                    {
                        Utils.GetSAS(SASList.Roll).SetPoint = FlightData.roll;
                        Utils.GetSAS(SASList.Roll).skipDerivative = true;
                    }

                    if (Utils.GetSAS(SASList.Roll).SetPoint - FlightData.roll >= -180 && Utils.GetSAS(SASList.Roll).SetPoint - FlightData.roll <= 180)
                        FlightData.thisVessel.ctrlState.roll = (float)Utils.GetSAS(SASList.Roll).ResponseD(FlightData.roll) / activationFadeRoll;
                    else if (Utils.GetSAS(SASList.Roll).SetPoint - FlightData.roll > 180)
                        FlightData.thisVessel.ctrlState.roll = (float)Utils.GetSAS(SASList.Roll).ResponseD(FlightData.roll + 360) / activationFadeRoll;
                    else if (Utils.GetSAS(SASList.Roll).SetPoint - FlightData.roll < -180)
                        FlightData.thisVessel.ctrlState.roll = (float)Utils.GetSAS(SASList.Roll).ResponseD(FlightData.roll - 360) / activationFadeRoll;
                }
            }
        }

        #region GUI

        public void Draw()
        {
            if (AppLauncherFlight.bDisplaySAS)
                SASwindow = GUILayout.Window(78934856, SASwindow, drawSASWindow, "SAS Module", GUILayout.Height(0));

            if (bShowPresets)
            {
                SASPresetwindow = GUILayout.Window(78934857, SASPresetwindow, drawPresetWindow, "SAS Presets", GUILayout.Height(0));
                SASPresetwindow.x = SASwindow.x + SASwindow.width;
                SASPresetwindow.y = SASwindow.y;
            }
        }

        private void drawSASWindow(int id)
        {
            if (GUI.Button(new Rect(SASwindow.width - 16, 2, 14, 14), ""))
            {
                AppLauncherFlight.bDisplaySAS = false;
            }

            bShowPresets = GUILayout.Toggle(bShowPresets, bShowPresets ? "Hide SAS Presets" : "Show SAS Presets");

            bStockSAS = GUILayout.Toggle(bStockSAS, bStockSAS ? "Mode: Stock SAS" : "Mode: SSAS");

            if (!bStockSAS)
            {
                GUI.backgroundColor = GeneralUI.HeaderButtonBackground;
                if (GUILayout.Button(bArmed ? "Disarm SAS" : "Arm SAS"))
                {
                    bArmed = !bArmed;
                    if (!bArmed)
                        ActivitySwitch(false);

                    if (bArmed)
                        Messaging.statusMessage(8);
                    else
                        Messaging.statusMessage(9);
                }
                GUI.backgroundColor = GeneralUI.stockBackgroundGUIColor;

                if (bArmed)
                {
                    Utils.GetSAS(SASList.Pitch).SetPoint = Utils.Clamp((float)GeneralUI.TogPlusNumBox("Pitch:", ref bActive[(int)SASList.Pitch], FlightData.pitch, Utils.GetSAS(SASList.Pitch).SetPoint, 80), -90, 90);
                    Utils.GetSAS(SASList.Yaw).SetPoint = GeneralUI.TogPlusNumBox("Heading:", ref bActive[(int)SASList.Yaw], FlightData.heading, Utils.GetSAS(SASList.Yaw).SetPoint, 80, 60, 360, 0);
                    if (!rollState) // editable
                        Utils.GetSAS(SASList.Roll).SetPoint = GeneralUI.TogPlusNumBox("Roll:", ref bActive[(int)SASList.Roll], FlightData.roll, Utils.GetSAS(SASList.Roll).SetPoint, 80, 60, 180, -180);
                    else // not editable b/c vector mode
                    {
                        GUILayout.BeginHorizontal();
                        bActive[(int)SASList.Roll] = GUILayout.Toggle(bActive[(int)SASList.Roll], "Roll:", GeneralUI.toggleButton, GUILayout.Width(80));
                        GUILayout.TextField(FlightData.roll.ToString("N2"), GUILayout.Width(60));
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.Box("", GUILayout.Height(10));
                    drawPIDValues(SASList.Pitch, "Pitch");
                    drawPIDValues(SASList.Roll, "Roll");
                    drawPIDValues(SASList.Yaw, "Yaw");
                }
            }
            else
            {
                VesselAutopilot.VesselSAS sas = FlightData.thisVessel.Autopilot.SAS;

                drawPIDValues(sas.pidLockedPitch, "Pitch", SASList.Pitch);
                drawPIDValues(sas.pidLockedRoll, "Roll", SASList.Roll);
                drawPIDValues(sas.pidLockedYaw, "Yaw", SASList.Yaw);


            }

            GUI.DragWindow();
        }

        private void drawPIDValues(SASList controllerID, string inputName)
        {
            PID.PID_Controller controller = Utils.GetSAS(controllerID);
            controller.bShow = GUILayout.Toggle(controller.bShow, inputName, GeneralUI.toggleButton);

            if (controller.bShow)
            {
                controller.PGain = GeneralUI.labPlusNumBox("Kp:", controller.PGain.ToString("G3"), 45);
                controller.IGain = GeneralUI.labPlusNumBox("Ki:", controller.IGain.ToString("G3"), 45);
                controller.DGain = GeneralUI.labPlusNumBox("Kd:", controller.DGain.ToString("G3"), 45);
                controller.Scalar = GeneralUI.labPlusNumBox("Scalar:", controller.Scalar.ToString("G3"), 45);
                fadeReset[(int)controllerID] = Math.Max((float)GeneralUI.labPlusNumBox("Slide:", fadeReset[(int)controllerID].ToString("G3"), 45), 1);
            }
        }

        private void drawPIDValues(PIDclamp controller, string inputName, SASList controllerID)
        {
            stockPIDDisplay[(int)controllerID] = GUILayout.Toggle(stockPIDDisplay[(int)controllerID], inputName, GeneralUI.toggleButton);

            if (stockPIDDisplay[(int)controllerID])
            {
                controller.kp = GeneralUI.labPlusNumBox("Kp:", controller.kp.ToString("G3"), 45);
                controller.ki = GeneralUI.labPlusNumBox("Ki:", controller.ki.ToString("G3"), 45);
                controller.kd = GeneralUI.labPlusNumBox("Kd:", controller.kd.ToString("G3"), 45);
                controller.clamp = Math.Max(GeneralUI.labPlusNumBox("Scalar:", controller.clamp.ToString("G3"), 45), 0.01);
            }
        }

        private void drawPresetWindow(int id)
        {
            if (GUI.Button(new Rect(SASPresetwindow.width - 16, 2, 14, 14), ""))
            {
                bShowPresets = false;
            }

            if (bStockSAS)
                drawStockPreset();
            else
                drawSurfPreset();
        }

        private void drawSurfPreset()
        {
            if (PresetManager.Instance.activeSASPreset != null)
            {
                GUILayout.Label(string.Format("Active Preset: {0}", PresetManager.Instance.activeSASPreset.name));
                if (PresetManager.Instance.activeSASPreset.name != "SSAS")
                {
                    if (GUILayout.Button("Update Preset"))
                        PresetManager.updateSASPreset(false, SASControllers);
                }
                GUILayout.Box("", GUILayout.Height(10), GUILayout.Width(180));
            }

            GUILayout.BeginHorizontal();
            newPresetName = GUILayout.TextField(newPresetName);
            if (GUILayout.Button("+", GUILayout.Width(25)))
                PresetManager.newSASPreset(ref newPresetName, SASControllers);
            GUILayout.EndHorizontal();

            GUILayout.Box("", GUILayout.Height(10), GUILayout.Width(180));

            if (GUILayout.Button("Reset to Defaults"))
                PresetManager.loadSASPreset(PresetManager.Instance.craftPresetList["default"].SSASPreset);

            GUILayout.Box("", GUILayout.Height(10), GUILayout.Width(180));

            foreach (SASPreset p in PresetManager.Instance.SASPresetList)
            {
                if (p.bStockSAS)
                    continue;

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(p.name))
                    PresetManager.loadSASPreset(p);
                else if (GUILayout.Button("x", GUILayout.Width(25)))
                    PresetManager.deleteSASPreset(p);
                GUILayout.EndHorizontal();
            }
        }

        private void drawStockPreset()
        {
            if (PresetManager.Instance.activeStockSASPreset != null)
            {
                GUILayout.Label(string.Format("Active Preset: {0}", PresetManager.Instance.activeStockSASPreset.name));
                if (PresetManager.Instance.activeStockSASPreset.name != "stock")
                {
                    if (GUILayout.Button("Update Preset"))
                        PresetManager.updateSASPreset(true);
                }
                GUILayout.Box("", GUILayout.Height(10), GUILayout.Width(180));
            }

            GUILayout.BeginHorizontal();
            newPresetName = GUILayout.TextField(newPresetName);
            if (GUILayout.Button("+", GUILayout.Width(25)))
                PresetManager.newSASPreset(ref newPresetName);
            GUILayout.EndHorizontal();

            GUILayout.Box("", GUILayout.Height(10), GUILayout.Width(180));

            if (GUILayout.Button("Reset to Defaults"))
                PresetManager.loadStockSASPreset(PresetManager.Instance.craftPresetList["default"].StockPreset);

            GUILayout.Box("", GUILayout.Height(10), GUILayout.Width(180));

            foreach (SASPreset p in PresetManager.Instance.SASPresetList)
            {
                if (!p.bStockSAS)
                    continue;

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(p.name))
                    PresetManager.loadStockSASPreset(p);
                else if (GUILayout.Button("x", GUILayout.Width(25)))
                    PresetManager.deleteSASPreset(p);
                GUILayout.EndHorizontal();
            }
        }
        #endregion

        public float FadeRollMult
        {
            get { return fadeRollMult; }
            set {
                fadeRollMult = Utils.Clamp(value, 0, 1);
            }
        }

        public float FadePitchMult
        {
            get { return fadePitchMult; }
            set {
                fadePitchMult = Utils.Clamp(value, 0, 1);
            }
        }

        public float FadeYawMult
        {
            get { return fadeYawMult; }
            set {
                fadeYawMult = Utils.Clamp(value, 0, 1);
            }
        }
    }
}