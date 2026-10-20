using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace WTTG2.SafeSkyCopy
{
    [BepInPlugin("pmxp.wttg2.safe-sky-copy", "Safe skyBREAK Copy", "1.0.1")]
    public sealed class SafeSkyCopy : BaseUnityPlugin
    {
        private ConfigEntry<bool> featureEnabled, hotkeysEnabled, panelEnabled, debugLogging;
        private ConfigEntry<KeyCode> panelKey, copyKey;
        private ConfigEntry<float> refreshSeconds;
        private FieldInfo helperField, linesField;
        private skyBreakBehavior sky;
        private TerminalHelperBehavior helper;
        private bool panelOpen, faulted;
        private float nextDiscover, nextRefresh;
        private Rect panelRect = new Rect(32, 80, 700, 500);
        private Vector2 scroll;
        private string transcript = string.Empty;
        private readonly List<string> rows = new List<string>();
        private string notice = "Open skyBREAK, then refresh. Only text already displayed is copied.";
        private GUIStyle textStyle;
        private int windowId;
        private GameObject clickShield;
        private InputField restoreInput;
        private bool pendingRefresh, pendingClose;

        private void Awake()
        {
            featureEnabled = Config.Bind("General", "Enabled", true, "Enable this plugin. F8 reloads the config; restart is unnecessary. No Harmony patches are installed.");
            hotkeysEnabled = Config.Bind("Input", "EnableHotkeys", true, "Enable the panel key and Ctrl+Shift+copy key. Disable to turn off all input handling (restart to re-enable).");
            panelEnabled = Config.Bind("UI", "EnablePanel", true, "Allow the separate copy panel. The game's terminal UI is never edited.");
            panelKey = Config.Bind("Input", "PanelKey", KeyCode.F8, "Toggle copy panel and reload config.");
            copyKey = Config.Bind("Input", "CopyKey", KeyCode.C, "Ctrl+Shift+this key copies the current skyBREAK transcript.");
            refreshSeconds = Config.Bind("UI", "RefreshSeconds", 0.5f, "Refresh interval while the panel is open, clamped to 0.1-5 seconds.");
            debugLogging = Config.Bind("Diagnostics", "DebugLogging", false, "Log row counts and binding events, never terminal text, network addresses or passwords.");
            windowId = GetInstanceID();
            try
            {
                helperField = typeof(skyBreakBehavior).GetField("myTerminalHelper", BindingFlags.Instance | BindingFlags.NonPublic);
                linesField = typeof(TerminalHelperBehavior).GetField("currentTerminalLines", BindingFlags.Instance | BindingFlags.NonPublic);
                if (helperField == null || linesField == null) throw new MissingFieldException("Unsupported skyBREAK layout; read-only bindings not found.");
                Logger.LogInfo("Safe skyBREAK Copy 1.0.1 ready. F8: selection panel; Ctrl+Shift+C: copy transcript. Harmony patches: 0.");
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void Update()
        {
            if (faulted) return;
            try
            {
                if (pendingClose || !featureEnabled.Value || !panelEnabled.Value || !hotkeysEnabled.Value)
                {
                    pendingClose = false;
                    ClosePanel();
                }
                if (pendingRefresh)
                {
                    pendingRefresh = false;
                    if (featureEnabled.Value && panelOpen) RefreshSnapshot(true);
                }
                if (!hotkeysEnabled.Value) return;
                if (panelOpen && Time.unscaledTime >= nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + Mathf.Clamp(refreshSeconds.Value, 0.1f, 5f);
                    RefreshSnapshot(false);
                }
                if (!Application.isFocused) return;
                if (Input.GetKeyDown(panelKey.Value))
                {
                    Config.Reload();
                    if (!featureEnabled.Value || !panelEnabled.Value || !hotkeysEnabled.Value) { ClosePanel(); return; }
                    panelOpen = !panelOpen;
                    if (panelOpen) { RefreshSnapshot(true); SetPanelInput(true); nextRefresh = Time.unscaledTime + 0.5f; }
                    else { SetPanelInput(false); ClearSnapshot(); }
                }
                if (!featureEnabled.Value) { ClosePanel(); return; }
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (ctrl && shift && Input.GetKeyDown(copyKey.Value))
                {
                    RefreshSnapshot(true);
                    Copy(transcript);
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void LateUpdate()
        {
            if (faulted || !panelOpen || !hotkeysEnabled.Value) return;
            try { SuspendTerminalInput(); }
            catch (Exception ex) { Fail(ex); }
        }

        private void SetPanelInput(bool open)
        {
            if (open)
            {
                if (clickShield == null)
                {
                    clickShield = new GameObject("SafeSkyCopy click shield", typeof(Canvas), typeof(GraphicRaycaster));
                    clickShield.transform.SetParent(transform, false);
                    Canvas canvas = clickShield.GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 32760;
                    GameObject imageObject = new GameObject("Block clicks behind copy panel", typeof(RectTransform), typeof(Image));
                    imageObject.transform.SetParent(clickShield.transform, false);
                    RectTransform rect = imageObject.GetComponent<RectTransform>();
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
                    imageObject.GetComponent<Image>().color = new Color(0, 0, 0, 0);
                }
                clickShield.SetActive(true);
                SuspendTerminalInput();
            }
            else
            {
                if (clickShield != null) clickShield.SetActive(false);
                if (restoreInput != null && restoreInput.gameObject.activeInHierarchy && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
                {
                    EventSystem.current.SetSelectedGameObject(restoreInput.gameObject);
                    restoreInput.ActivateInputField();
                }
                restoreInput = null;
            }
        }

        private void SuspendTerminalInput()
        {
            if (helper == null || helper.TerminalInput == null || helper.TerminalInput.inputLine == null) return;
            InputField input = helper.TerminalInput.inputLine;
            if (!input.isFocused) return;
            restoreInput = input;
            input.DeactivateInputField();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == input.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private bool Bind(bool force)
        {
            if (sky != null && helper != null && sky.Window != null && sky.Window.activeInHierarchy && helper.terminalContent != null && helper.terminalContent.activeInHierarchy) return true;
            sky = null; helper = null;
            if (!force && Time.unscaledTime < nextDiscover) return false;
            nextDiscover = Time.unscaledTime + 1f;
            skyBreakBehavior[] candidates = Resources.FindObjectsOfTypeAll<skyBreakBehavior>();
            foreach (skyBreakBehavior candidate in candidates)
            {
                if (candidate == null || candidate.Window == null || !candidate.Window.activeInHierarchy) continue;
                TerminalHelperBehavior found = helperField.GetValue(candidate) as TerminalHelperBehavior;
                if (found == null || found.terminalContent == null || !found.terminalContent.activeInHierarchy) continue;
                sky = candidate; helper = found;
                if (debugLogging.Value) Logger.LogInfo("Bound to an open skyBREAK window.");
                return true;
            }
            return false;
        }

        private void RefreshSnapshot(bool force)
        {
            if (!Bind(force))
            {
                ClearSnapshot();
                notice = "skyBREAK is closed or not ready. Open it normally, then press Refresh.";
                return;
            }
            IList lines = linesField.GetValue(helper) as IList;
            List<string> fresh = new List<string>();
            StringBuilder buffer = new StringBuilder();
            if (lines != null)
            {
                foreach (object item in lines)
                {
                    TerminalLineObject line = item as TerminalLineObject;
                    if (line == null || !line.gameObject.activeInHierarchy || line.textLine == null) continue;
                    Text text = line.textLine.GetComponent<Text>();
                    if (text == null || !text.enabled || !text.gameObject.activeInHierarchy || text.color.a <= 0.001f) continue;
                    // Read the rendered UI.Text, never the private myLine (which holds future animation text).
                    CanvasGroup[] groups = text.GetComponentsInParent<CanvasGroup>(false);
                    float opacity = 1f;
                    foreach (CanvasGroup group in groups)
                    {
                        opacity *= group.alpha;
                        if (group.ignoreParentGroups) break;
                    }
                    if (opacity <= 0.001f) continue;
                    string value = TextTools.Plain(text.text, text.supportRichText);
                    if (value.Length == 0) continue;
                    if (buffer.Length > 0) buffer.Append('\n');
                    buffer.Append(value);
                    fresh.Add(value);
                }
            }
            string updated = buffer.ToString();
            // Keep the current selection stable unless the terminal actually changes.
            if (updated != transcript)
            {
                transcript = updated; rows.Clear(); rows.AddRange(fresh);
                if (debugLogging.Value) Logger.LogInfo("Read displayed terminal output: " + rows.Count + " rows.");
            }
            notice = rows.Count == 0 ? "No displayed output yet." : "Select any fragment below and press Ctrl+C, or use a Copy button.";
        }

        private void Copy(string value)
        {
            if (string.IsNullOrEmpty(value)) { notice = "Nothing to copy. Open skyBREAK and wait for output."; return; }
            GUIUtility.systemCopyBuffer = value;
            notice = "Copied to the Windows clipboard.";
            if (debugLogging.Value) Logger.LogInfo("Clipboard updated from displayed output.");
        }

        private void OnGUI()
        {
            if (faulted || !panelOpen || !featureEnabled.Value || !panelEnabled.Value) return;
            try
            {
                if (textStyle == null)
                {
                    textStyle = new GUIStyle(GUI.skin.textArea);
                    textStyle.wordWrap = true;
                    textStyle.richText = false;
                    textStyle.fontSize = 14;
                }
                float maxWidth = Mathf.Max(320, Screen.width - 16);
                float maxHeight = Mathf.Max(200, Screen.height - 16);
                panelRect.width = Mathf.Min(700, maxWidth);
                panelRect.height = Mathf.Min(500, maxHeight);
                panelRect.x = Mathf.Clamp(panelRect.x, 0, Mathf.Max(0, Screen.width - panelRect.width));
                panelRect.y = Mathf.Clamp(panelRect.y, 0, Mathf.Max(0, Screen.height - panelRect.height));
                panelRect = GUI.Window(windowId, panelRect, DrawPanel, "skyBREAK Copy - F8 to close");
            }
            catch (ExitGUIException) { throw; }
            catch (Exception ex) { Fail(ex); }
        }

        private void DrawPanel(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(100))) pendingRefresh = true;
            if (GUILayout.Button("Copy all", GUILayout.Width(100))) Copy(transcript);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(80))) pendingClose = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(notice);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            // Return value intentionally ignored: selection and Ctrl+C are allowed, editing the snapshot is not.
            GUI.SetNextControlName("SkyCopyTranscript");
            GUILayout.TextArea(transcript, textStyle, GUILayout.MinHeight(100), GUILayout.ExpandWidth(true));
            GUILayout.Space(8);
            for (int i = 0; i < rows.Count; i++)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Copy row " + (i + 1), GUILayout.Width(105))) Copy(rows[i]);
                string[] addresses = TextTools.Bssids(rows[i]);
                foreach (string address in addresses) if (GUILayout.Button(address, GUILayout.Width(155))) Copy(address);
                string preview = rows[i].Replace('\n', ' ');
                if (preview.Length > 100) preview = preview.Substring(0, 100) + "...";
                GUILayout.Label(preview);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Only displayed output. No commands, network data, launch handlers or saves are changed.");
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, panelRect.width, 20));
        }

        private void ClearSnapshot() { transcript = string.Empty; rows.Clear(); sky = null; helper = null; }
        private void ClosePanel()
        {
            panelOpen = false;
            if (clickShield != null) clickShield.SetActive(false);
            try { SetPanelInput(false); }
            catch (Exception) { restoreInput = null; }
            finally { ClearSnapshot(); }
        }
        private void OnDisable() { ClosePanel(); }
        private void OnDestroy() { ClosePanel(); }
        private void Fail(Exception ex)
        {
            faulted = true; ClosePanel();
            // Do not log exception messages: third-party exceptions may contain terminal values.
            Logger.LogError("Copy plugin disabled safely after " + ex.GetType().Name + ". Original game handlers were not patched. Restart after checking compatibility.");
            Logger.LogDebug(ex.StackTrace);
        }
    }
}
