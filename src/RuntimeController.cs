using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace FriendsWine
{
    [DefaultExecutionOrder(32000)]
    public sealed class RuntimeController : MonoBehaviour
    {
        public static RuntimeController Instance { get; private set; }
        public bool ManagerEnabled = true;
        public bool Requested { get; private set; }
        public bool Active { get; private set; }
        public StretchScale Scale { get; private set; } = StretchScale.Identity;
        public string Status { get; private set; } = "正在载入 BGM";
        public string Coverage { get { return visuals == null ? "" : visuals.Coverage; } }
        private AudioSource source;
        private AudioClip clip;
        private Hotkey hotkey;
        private RenderCoordinator visuals;
        private double startedAt;
        private bool shuttingDown;
        private UnityWebRequest request;
        private string musicPath;
        private bool loadingMusic;
        private bool audioConfigurationChanged;

        internal void Initialize(string modPath)
        {
            Instance = this;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0;
            source.ignoreListenerPause = true;
            source.ignoreListenerVolume = true;
            source.bypassListenerEffects = true;
            ApplySettings();
            visuals = new RenderCoordinator();
            musicPath = Path.Combine(modPath, "Audio", "bgm.mp3");
            AudioSettings.OnAudioConfigurationChanged += AudioConfigurationChanged;
            ReloadMusic();
#if FRIENDSWINE_SELFTEST
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--friendswine-selftest") >= 0)
                gameObject.AddComponent<IntegrationVerification>();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--friendswine-visibility-test") >= 0)
                gameObject.AddComponent<VisibilityVerification>();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--friendswine-culling-test") >= 0)
                gameObject.AddComponent<CullingVerification>();
#endif
        }

        public void ApplySettings()
        {
            hotkey = Hotkey.Parse(Main.Settings.ToggleKey);
            if (source) source.volume = Main.Settings.Volume;
            if (clip) Status = Active ? "已启用（" + Main.Settings.ToggleKey + " 停用）" : "已停用（" + Main.Settings.ToggleKey + " 启用）";
        }

        private IEnumerator LoadMusic(string path)
        {
            loadingMusic = true;
            if (!File.Exists(path))
            { Status = "找不到 BGM：" + path; Main.Entry.Logger.Error(Status); loadingMusic = false; Requested = false; yield break; }
            request = UnityWebRequestMultimedia.GetAudioClip(new Uri(Path.GetFullPath(path)).AbsoluteUri, AudioType.MPEG);
            ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = false;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Status = "BGM 载入失败：" + request.error;
                Main.Entry.Logger.Error(Status);
                Requested = false;
                if (Active) StopEffect();
            }
            else
            {
                var oldClip = clip;
                clip = DownloadHandlerAudioClip.GetContent(request);
                clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                source.clip = clip;
                if (oldClip) Destroy(oldClip);
                Status = "已停用（" + Main.Settings.ToggleKey + " 启用）";
                Main.Entry.Logger.Log("BGM ready: " + clip.length.ToString("F2") + "s, " + clip.frequency + "Hz, " + clip.loadState + ".");
                if (Active)
                {
                    // AudioSettings.Reset invalidates downloaded clips. Re-decode and seek
                    // to the existing unscaled timeline; scene changes must not restart it.
                    double seconds = (Time.realtimeSinceStartupAsDouble - startedAt) % clip.length;
                    source.timeSamples = (int)(seconds * clip.frequency);
                    source.Play();
                    Status = "已启用（" + Main.Settings.ToggleKey + " 停用）";
                }
                else if (Requested && ManagerEnabled) StartEffect();
            }
            request.Dispose(); request = null;
            loadingMusic = false;
        }

        private void AudioConfigurationChanged(bool deviceChanged) { audioConfigurationChanged = true; }
        private void ReloadMusic()
        {
            if (loadingMusic || shuttingDown) return;
            audioConfigurationChanged = false;
            StartCoroutine(LoadMusic(musicPath));
        }

        public void ToggleEffect()
        {
            if (Requested) { StopEffect(); return; }
            if (!ManagerEnabled) return;
            Requested = true;
            if (clip && clip.loadState == AudioDataLoadState.Loaded) StartEffect();
            else { Status = "等待 BGM 载入；音乐和效果将一起开始"; ReloadMusic(); }
        }

        private void StartEffect()
        {
            if (!clip || !ManagerEnabled || shuttingDown) return;
            try
            {
                visuals.Enable();
                source.timeSamples = 0;
                source.Play();
                startedAt = Time.realtimeSinceStartupAsDouble;
                Scale = StretchScale.Identity;
                Active = true;
                Status = "已启用（" + Main.Settings.ToggleKey + " 停用）";
                Main.Entry.Logger.Log("Effect and BGM ON. " + visuals.Coverage);
            }
            catch (Exception ex) { Main.LogError("Enable failed", ex); StopEffect(); Status = "启用失败，详见 UMM 日志"; }
        }

        public void StopEffect()
        {
            Requested = false; Active = false; Scale = StretchScale.Identity;
            if (source) source.Stop();
            if (visuals != null) visuals.Disable();
            Status = "已停用（" + Main.Settings.ToggleKey + " 启用）";
            if (Main.Entry != null) Main.Entry.Logger.Log("Effect and BGM OFF; visuals restored.");
        }

        private void Update()
        {
            if (shuttingDown) return;
            if (!loadingMusic && (audioConfigurationChanged || (clip && clip.loadState == AudioDataLoadState.Unloaded))) ReloadMusic();
            if (ManagerEnabled && hotkey != null && hotkey.Pressed()) ToggleEffect();
            if (!Active) return;
            var s = Main.Settings;
            Scale = BeatMath.Evaluate(Time.realtimeSinceStartupAsDouble - startedAt, s.VerticalX, s.VerticalY, s.HorizontalX, s.HorizontalY);
            // Keep our private source independent of game-wide listener volume and pause.
            source.volume = s.Volume;
            source.ignoreListenerPause = true;
            source.ignoreListenerVolume = true;
            visuals.Tick();
        }

        private void LateUpdate()
        {
            if (Active && visuals != null) visuals.PrepareUi();
        }

        internal void Shutdown()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            StopEffect();
            if (visuals != null) visuals.Dispose();
            AudioSettings.OnAudioConfigurationChanged -= AudioConfigurationChanged;
            StopAllCoroutines();
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            if (clip) Destroy(clip);
            if (Instance == this) Instance = null;
        }

        private void OnDestroy() { Shutdown(); }
    }
}
