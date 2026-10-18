using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GameJam.FireRescue;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class EnglishReleaseChecks
{
    public const string Folder = "Logs/EnglishRelease";
    const string Pending = "EnglishReleaseChecks.Menu";
    [Serializable] public class Report { public string status, utc; public List<string> checks = new List<string>(); public List<string> errors = new List<string>(); }
    static EnglishReleaseChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                EditorApplication.delayCall += () => new GameObject("English menu check").AddComponent<EnglishMenuCapture>().Begin();
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                EditorApplication.delayCall += () => EditorSceneManager.OpenScene(FinalLoopSetup.ScenePath);
            }
        };
    }

    [MenuItem("GameJam/Final Loop/Validate English release")]
    public static void Validate()
    {
        var report = new Report { status = "PASS", utc = DateTime.UtcNow.ToString("O") };
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        Check(report, PlayerSettings.productName == "VOLKANOVSKI", "Product name is VOLKANOVSKI");
        Check(report, mission && mission.evacuation && mission.gameFlow, "Authored rescue mission is bound");
        if (mission)
        {
            var dialogue = mission.evacuation.dialogue;
            Check(report, dialogue && dialogue.lines.Length == 4 && dialogue.lines.All(l => !l.villager) &&
                dialogue.lines.Where(l => l.id.StartsWith("recruit_")).Count(l => l.text == "Come here, follow me.") == 3 &&
                dialogue.lines.Single(l => l.id == "depart").text == "Follow me. I'll take you to the shelter.", "Only the four short player rescue cues are authored");
            var ending = mission.gameFlow.victoryCinematic;
            Check(report, ending && ending.thoughts.Length == 6 && ending.thoughts.All(t => English(t.text)) && English(ending.arrivalThought.text), "All seven closing captions are English");
            Check(report, mission.evacuation.groupRescue && mission.evacuation.TotalCount == 7 && Mathf.Approximately(mission.preparationSeconds, 120), "Existing group rescue and 120-second deadline survive");
        }
        foreach (var text in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Audit(report, text.name, text.text);
        foreach (var text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Audit(report, text.name, text.text);
        foreach (var text in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Audit(report, text.name, text.text);
        Write("authoring.json", report);
        if (report.errors.Count > 0) throw new InvalidOperationException(string.Join("; ", report.errors));
    }

    public static bool English(string text) => string.IsNullOrEmpty(text) || !Regex.IsMatch(text.Replace("VOLKANOVSKI", ""), "[çğıöşüÇĞİÖŞÜ]");
    public static void Audit(Report report, string name, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (!English(value) || value.Contains("ASHFALL")) report.errors.Add(name + ": " + value);
    }
    public static void Check(Report report, bool passed, string label)
    { if (passed) report.checks.Add(label); else report.errors.Add(label); }
    public static void Write(string filename, Report report)
    {
        if (report.errors.Count > 0) report.status = "FAIL";
        Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/" + filename, JsonUtility.ToJson(report, true));
    }

    [MenuItem("GameJam/Final Loop/Check English menu")]
    public static void CaptureMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Use saved Edit Mode.");
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        SessionState.SetBool(Pending, true);
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        EditorApplication.EnterPlaymode();
    }
}

public sealed class EnglishMenuCapture : MonoBehaviour
{
    bool oldBackground;
    public void Begin() { oldBackground = Application.runInBackground; Application.runInBackground = true; StartCoroutine(Capture()); }
    IEnumerator Capture()
    {
        yield return new WaitForSecondsRealtime(5f);
        var report = new EnglishReleaseChecks.Report { status = "PASS", utc = DateTime.UtcNow.ToString("O") };
        var texts = Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var text in texts) EnglishReleaseChecks.Audit(report, text.name, text.text);
        var title = texts.FirstOrDefault(t => t.text == "VOLKANOVSKI");
        var logo = Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(image => image.name == "VolkanovskiLogoIntro");
        var logoGroup = logo ? logo.GetComponent<CanvasGroup>() : null;
        bool visibleLogo = logo && logo.enabled && logo.sprite && logo.gameObject.activeInHierarchy && logoGroup && logoGroup.alpha > .95f;
        bool visibleTextTitle = title && title.gameObject.activeInHierarchy && title.font && title.font.HasCharacter('I');
        EnglishReleaseChecks.Check(report, visibleLogo || visibleTextTitle, "Visible VOLKANOVSKI branding uses the authored logo or a readable text title");
        EnglishReleaseChecks.Check(report, visibleLogo
            ? logo.rectTransform.rect.width > 0 && logo.rectTransform.rect.height > 0 && float.IsFinite(logo.rectTransform.position.x) && !visibleTextTitle
            : visibleTextTitle && !title.cachedTextGenerator.GetVerticesArray().Any(v => !float.IsFinite(v.position.x)), "Title geometry is valid and the logo has no duplicate text title");
        var primaryLabels = texts.Where(t => t.gameObject.activeInHierarchy && t.GetComponentInParent<Button>() &&
            new[] { "NEW GAME", "SETTINGS", "HOW TO PLAY", "EXIT" }.Contains(t.text.ToUpperInvariant())).ToArray();
        EnglishReleaseChecks.Check(report, primaryLabels.Length == 4 && primaryLabels.All(t => t.font && t.font.HasCharacter('I')), "All four primary menu labels retain a font with uppercase I");
        EnglishReleaseChecks.Check(report, texts.Any(t => t.text.Equals("New game", StringComparison.OrdinalIgnoreCase)), "English main menu buttons survive");
        CheckMenuMusic(report);
        yield return new WaitForEndOfFrame();
        Directory.CreateDirectory(EnglishReleaseChecks.Folder);
        var pixels = ScreenCapture.CaptureScreenshotAsTexture();
        if (pixels) { File.WriteAllBytes(EnglishReleaseChecks.Folder + "/main-menu.png", pixels.EncodeToPNG()); Destroy(pixels); }
        EnglishReleaseChecks.Write("menu.json", report);
        Application.runInBackground = oldBackground;
        EditorApplication.ExitPlaymode();
    }

    static void CheckMenuMusic(EnglishReleaseChecks.Report report)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/sesdosya/Music/Volcanic_Passage_2026-10-18T035040.mp3");
        var sources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(s => s.clip == clip).ToArray();
        var music = sources.FirstOrDefault();
        EnglishReleaseChecks.Check(report, clip && clip.length > 1 && clip.loadType == AudioClipLoadType.Streaming,
            "Volcanic Passage is a playable streaming recording: " + (clip ? clip.length.ToString("F2") + " seconds" : "missing"));
        EnglishReleaseChecks.Check(report, sources.Length == 1 && music && music.isPlaying && music.loop && music.spatialBlend == 0 &&
            music.gameObject.scene.name == "MainMenu", "One scene-owned 2D source loops Volcanic Passage in the menu");
        if (!music) return;
        bool hadMusic = PlayerPrefs.HasKey("Ashfall.MusicVolume");
        float savedMusic = PlayerPrefs.GetFloat("Ashfall.MusicVolume", .75f);
        var settings = Object.FindFirstObjectByType<Ashfall.MainMenu.AshfallSettingsController>();
        EnglishReleaseChecks.Check(report, settings && Mathf.Abs(music.volume - .22f * Mathf.Clamp01(savedMusic)) < .001f,
            "Menu music begins at the saved music-volume setting");
        if (!settings) return;
        try
        {
            settings.SetMusicVolume(.5f);
            EnglishReleaseChecks.Check(report, Mathf.Abs(music.volume - .11f) < .001f, "Music slider changes the playing menu track immediately");
            settings.SetMusicVolume(0);
            EnglishReleaseChecks.Check(report, music.volume == 0 && music.isPlaying, "Music mute silences the menu track without restarting it");
        }
        finally
        {
            settings.SetMusicVolume(savedMusic);
            if (!hadMusic) { PlayerPrefs.DeleteKey("Ashfall.MusicVolume"); PlayerPrefs.Save(); }
        }
    }
}
