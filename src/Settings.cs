using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace FriendsWine
{
    public sealed class Settings
    {
        public float VerticalX = .75f;
        public float VerticalY = 1.35f;
        public float HorizontalX = 1.35f;
        public float HorizontalY = .75f;
        public float Volume = .5f;
        public string ToggleKey = "F8";

        public void Validate()
        {
            if (!BeatMath.ValidFactor(VerticalX) || !BeatMath.ValidFactor(VerticalY) ||
                !BeatMath.ValidFactor(HorizontalX) || !BeatMath.ValidFactor(HorizontalY))
                throw new ArgumentException("四个缩放倍数必须为有限的正数，不能为 NaN 或无穷大。");
            if (float.IsNaN(Volume) || float.IsInfinity(Volume) || Volume < 0 || Volume > 1)
                throw new ArgumentException("音量必须在 0 到 1 之间。");
            Hotkey.Parse(ToggleKey);
        }

        public static Settings Read(string path)
        {
            Settings result = File.Exists(path)
                ? JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path, Encoding.UTF8))
                : new Settings();
            if (result == null) throw new ArgumentException("配置文件不能为 null。");
            result.Validate();
            return result;
        }

        public void Write(string path)
        {
            Validate();
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(this, Formatting.Indented) + "\n", new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
    }

    public sealed class Hotkey
    {
        public KeyCode Key;
        public bool Ctrl, Alt, Shift;

        public static Hotkey Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("快捷键不能为空。");
            var result = new Hotkey();
            string[] parts = value.Split('+');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": result.Ctrl = true; break;
                    case "alt": result.Alt = true; break;
                    case "shift": result.Shift = true; break;
                    default: throw new ArgumentException("快捷键修饰符支持 Ctrl、Alt、Shift。");
                }
            }
            if (!Enum.TryParse(parts[parts.Length - 1].Trim(), true, out result.Key) ||
                !Enum.IsDefined(typeof(KeyCode), result.Key) || result.Key == KeyCode.None)
                throw new ArgumentException("快捷键示例：F8、F9、Ctrl+F8。");
            return result;
        }

        public bool Pressed()
        {
            return Input.GetKeyDown(Key) &&
                Ctrl == (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                Alt == (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                Shift == (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }
    }
}
