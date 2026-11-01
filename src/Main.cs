using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace FriendsWine
{
    public static class Main
    {
        internal static UnityModManager.ModEntry Entry;
        internal static RuntimeController Runtime;
        internal static Settings Settings;
        private static string configPath;
        private static readonly string[] fields = new string[6];
        private static string message = "";
        private static bool editing;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            Entry = entry;
            configPath = Path.Combine(entry.Path, "settings.json");
            try { Settings = Settings.Read(configPath); }
            catch (Exception ex)
            {
                Settings = new Settings();
                message = "配置读取失败，暂用默认值：" + ex.Message;
                entry.Logger.Error(message);
            }
            if (!File.Exists(configPath)) Settings.Write(configPath);
            RefreshFields();
            var root = new GameObject("FriendsWine.Runtime");
            UnityEngine.Object.DontDestroyOnLoad(root);
            Runtime = root.AddComponent<RuntimeController>();
            Runtime.Initialize(entry.Path);
            entry.OnToggle = OnToggle;
            entry.OnGUI = DrawSettings;
            entry.OnSaveGUI = _ => { }; // Only the explicit save button commits an edited draft.
            entry.OnUnload = _ => { Runtime.Shutdown(); UnityEngine.Object.Destroy(root); return true; };
            entry.Logger.Log("Loaded. Effect starts OFF; toggle with " + Settings.ToggleKey + ". Fixed BPM=130.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool enabled)
        {
            if (Runtime) Runtime.ManagerEnabled = enabled;
            if (!enabled && Runtime) Runtime.StopEffect();
            return true;
        }

        internal static void LogError(string context, Exception ex)
        {
            Entry.Logger.Error(context + ": " + ex);
        }

        private static void RefreshFields()
        {
            fields[0] = Settings.VerticalX.ToString(CultureInfo.InvariantCulture);
            fields[1] = Settings.VerticalY.ToString(CultureInfo.InvariantCulture);
            fields[2] = Settings.HorizontalX.ToString(CultureInfo.InvariantCulture);
            fields[3] = Settings.HorizontalY.ToString(CultureInfo.InvariantCulture);
            fields[4] = Settings.Volume.ToString(CultureInfo.InvariantCulture);
            fields[5] = Settings.ToggleKey;
            editing = false;
        }

        private static float Number(int i)
        {
            float result;
            if (!float.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                throw new ArgumentException("请使用数字和小数点填写缩放倍数与音量。");
            return result;
        }

        private static void DrawSettings(UnityModManager.ModEntry entry)
        {
            GUILayout.Label("朋友的酒 · 130 BPM · 每拍一次纵向和横向拉伸");
            GUILayout.Label("状态：" + Runtime.Status);
            if (GUILayout.Button(Runtime.Requested ? "停用效果和 BGM" : "启用效果和 BGM")) Runtime.ToggleEffect();
            GUILayout.Space(8);
            string[] labels = { "纵向拉伸：X 倍数", "纵向拉伸：Y 倍数", "横向拉伸：X 倍数", "横向拉伸：Y 倍数", "BGM 音量（0～1）", "切换快捷键（例如 F8 / Ctrl+F8）" };
            for (int i = 0; i < fields.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(labels[i], GUILayout.Width(290));
                string next = GUILayout.TextField(fields[i], GUILayout.Width(170));
                if (next != fields[i]) { fields[i] = next; editing = true; }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(editing ? "配置尚未保存。保存后立即生效，BGM 不会重播。" : "配置已载入。游戏启动时效果默认停用。");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存并应用"))
            {
                try
                {
                    var next = new Settings { VerticalX = Number(0), VerticalY = Number(1), HorizontalX = Number(2), HorizontalY = Number(3), Volume = Number(4), ToggleKey = fields[5].Trim() };
                    next.Write(configPath);
                    Settings = next;
                    Runtime.ApplySettings();
                    RefreshFields();
                    message = "已保存并应用。";
                }
                catch (Exception ex) { message = ex.Message; }
            }
            if (GUILayout.Button("从配置文件重新载入"))
            {
                try { Settings = Settings.Read(configPath); Runtime.ApplySettings(); RefreshFields(); message = "已重新载入。"; }
                catch (Exception ex) { message = ex.Message; }
            }
            if (GUILayout.Button("填入默认值"))
            {
                var previous = Settings;
                Settings = new Settings(); RefreshFields(); Settings = previous;
                editing = true; message = "已填入默认值，点击保存并应用后生效。";
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message);
            GUILayout.Label("配置文件：" + configPath);
            GUILayout.Label(Runtime.Coverage);
        }
    }
}
