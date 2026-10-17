using System;
using System.Collections.Generic;
using System.IO;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;

public static class EruptionEffectsChecks
{
    [Serializable]
    private sealed class Check
    {
        public string name;
        public bool passed;
        public string evidence;
    }

    [Serializable]
    private sealed class Report
    {
        public bool allPassed;
        public string finishedUtc;
        public string error;
        public List<Check> checks = new List<Check>();
    }

    [MenuItem("GameJam/Fire Rescue/Check eruption effects and camera shake (Play Mode)", true)]
    private static bool CanRun() => EditorApplication.isPlaying;

    [MenuItem("GameJam/Fire Rescue/Check eruption effects and camera shake (Play Mode)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("Eruption effects checks require Play Mode.");
        var report = new Report();
        var objects = new List<GameObject>();
        try
        {
            GameObject rig = NewObject(objects, "Eruption effects test");
            VolcanoEruptionEffects effects = rig.AddComponent<VolcanoEruptionEffects>();
            effects.eruptionBurst = Particles(objects, "Eruption burst test", 20f, false);
            effects.lavaBombs = Particles(objects, "Lava bombs test", 40f, true);
            effects.smokePlume = Particles(objects, "Smoke plume test", 12f, true);
            effects.eruptionLight = NewObject(objects, "Eruption flash test").AddComponent<Light>();
            effects.eruptionLight.intensity = 10f;
            effects.ResetEffects();
            Add(report, "effects_start_idle", !effects.IsErupting && !effects.lavaBombs.isPlaying &&
                !effects.smokePlume.isPlaying && !effects.eruptionLight.enabled, "Erupting=" + effects.IsErupting);
            effects.BeginEruption();
            Add(report, "eruption_starts_authored_effects", effects.IsErupting && effects.eruptionBurst.isPlaying &&
                effects.lavaBombs.isPlaying && effects.smokePlume.isPlaying && effects.eruptionLight.enabled,
                "Flash intensity=" + effects.eruptionLight.intensity);
            effects.TickEffect(0.7f);
            Add(report, "flash_decays", Near(effects.eruptionLight.intensity, 2.5f),
                "Intensity=" + effects.eruptionLight.intensity);
            effects.TickEffect(7.3f);
            Add(report, "flash_stops_without_stopping_plume", !effects.eruptionLight.enabled && effects.smokePlume.isPlaying &&
                Near(effects.lavaBombs.emission.rateOverTimeMultiplier, 40f), "Elapsed=" + effects.Elapsed);
            effects.TickEffect(5f);
            Add(report, "bomb_emission_tapers_after_initial_burst", Near(effects.lavaBombs.emission.rateOverTimeMultiplier, 3.2f),
                "Bomb rate=" + effects.lavaBombs.emission.rateOverTimeMultiplier);
            effects.TickEffect(60f);
            Add(report, "long_smoke_column_keeps_authored_emission", effects.IsErupting && effects.smokePlume.isPlaying &&
                Near(effects.smokePlume.emission.rateOverTimeMultiplier, 12f), "Smoke rate=" + effects.smokePlume.emission.rateOverTimeMultiplier);
            effects.TickEffect(float.NaN);
            Add(report, "invalid_effect_delta_is_ignored", Near(effects.Elapsed, 73f), "Elapsed=" + effects.Elapsed);
            effects.ResetEffects();
            Add(report, "reset_stops_clears_and_rewinds_effects", !effects.IsErupting && Near(effects.Elapsed, 0f) &&
                !effects.lavaBombs.isPlaying && effects.lavaBombs.particleCount == 0 && !effects.smokePlume.isPlaying &&
                !effects.eruptionLight.enabled, "Elapsed=" + effects.Elapsed);
            effects.BeginEruption();
            Add(report, "second_eruption_restores_original_strength", Near(effects.lavaBombs.emission.rateOverTimeMultiplier, 40f) &&
                Near(effects.eruptionLight.intensity, 10f), "Bomb rate=" + effects.lavaBombs.emission.rateOverTimeMultiplier);
            effects.enabled = false;
            Add(report, "disable_stops_effects", !effects.IsErupting && !effects.eruptionLight.enabled &&
                !effects.smokePlume.isPlaying, "Erupting=" + effects.IsErupting);

            GameObject cameraObject = NewObject(objects, "Camera shake test");
            cameraObject.transform.localRotation = Quaternion.Euler(12f, 34f, 0f);
            Vector3 baselinePosition = cameraObject.transform.localPosition;
            Quaternion baselineRotation = cameraObject.transform.localRotation;
            VolcanoCameraShake shake = cameraObject.AddComponent<VolcanoCameraShake>();
            Add(report, "early_restore_helper_created", cameraObject.GetComponent<VolcanoCameraShakeRestore>() != null,
                "Helper=" + (cameraObject.GetComponent<VolcanoCameraShakeRestore>() != null));
            shake.Shake(2f, 0.35f);
            shake.TickShake(0.2f);
            float firstDisplacement = Vector3.Distance(cameraObject.transform.localPosition, baselinePosition);
            Add(report, "shake_displacement_obeys_strength", firstDisplacement > 0f && firstDisplacement <= 0.3501f &&
                Near(shake.Intensity, 0.2835f), "Displacement=" + firstDisplacement + "; intensity=" + shake.Intensity);
            float largestDisplacement = firstDisplacement;
            for (int i = 0; i < 100; i++)
            {
                shake.TickShake(0.01f);
                largestDisplacement = Mathf.Max(largestDisplacement,
                    Vector3.Distance(cameraObject.transform.localPosition, baselinePosition));
            }
            Add(report, "static_camera_does_not_accumulate_offsets", largestDisplacement <= 0.3501f,
                "Largest displacement=" + largestDisplacement);
            shake.StopShake();
            Add(report, "stop_restores_original_pose", SamePose(cameraObject.transform, baselinePosition, baselineRotation) &&
                Near(shake.Intensity, 0f), "Position=" + cameraObject.transform.localPosition);
            shake.Shake(1f, 0.35f);
            shake.TickShake(0.1f);
            Vector3 newPosition = baselinePosition + new Vector3(3f, 2f, -4f);
            Quaternion newRotation = Quaternion.Euler(22f, 73f, 0f);
            cameraObject.transform.SetLocalPositionAndRotation(newPosition, newRotation);
            shake.TickShake(0.1f);
            shake.StopShake();
            Add(report, "external_camera_pose_write_is_preserved", SamePose(cameraObject.transform, newPosition, newRotation),
                "Position=" + cameraObject.transform.localPosition);
            shake.Shake(1f, 0.35f);
            shake.TickShake(0.1f);
            shake.TickShake(5f);
            Add(report, "shake_completion_restores_pose", SamePose(cameraObject.transform, newPosition, newRotation) &&
                !shake.IsShaking && Near(shake.Intensity, 0f), "Intensity=" + shake.Intensity);
            shake.Shake(1f, 0.35f);
            shake.TickShake(0.1f);
            shake.enabled = false;
            Add(report, "disabling_shake_restores_pose", SamePose(cameraObject.transform, newPosition, newRotation) &&
                !cameraObject.GetComponent<VolcanoCameraShakeRestore>().enabled, "Intensity=" + shake.Intensity);
        }
        catch (Exception exception)
        {
            report.error = exception.ToString();
        }
        finally
        {
            foreach (GameObject item in objects)
                if (item != null)
                    UnityEngine.Object.DestroyImmediate(item);
            report.allPassed = string.IsNullOrEmpty(report.error) && report.checks.Count > 0;
            foreach (Check check in report.checks)
                report.allPassed &= check.passed;
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/EruptionEffectsChecks.json", JsonUtility.ToJson(report, true));
            if (report.allPassed)
                Debug.Log("Eruption effects checks passed: " + report.checks.Count + ". Report: Logs/EruptionEffectsChecks.json");
            else
                Debug.LogError("Eruption effects checks failed. Inspect Logs/EruptionEffectsChecks.json\n" + report.error);
        }
    }

    private static GameObject NewObject(List<GameObject> objects, string name)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        item.transform.position = new Vector3(12000f, 250f, 12000f);
        objects.Add(item);
        return item;
    }

    private static ParticleSystem Particles(List<GameObject> objects, string name, float rate, bool looping)
    {
        ParticleSystem particles = NewObject(objects, name).AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = looping;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = rate;
        return particles;
    }

    private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < 0.001f;

    private static bool SamePose(Transform item, Vector3 position, Quaternion rotation) =>
        Vector3.Distance(item.localPosition, position) < 0.0001f && Quaternion.Angle(item.localRotation, rotation) < 0.01f;

    private static void Add(Report report, string name, bool passed, string evidence) =>
        report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
}
