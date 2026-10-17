using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit before/after capture and read-only preservation review. Never authors or saves scenes.</summary>
[InitializeOnLoad]
public static class TownDressingReviewChecks
{
    const string Folder = "Logs/TownDressingReview";
    const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    const string DressingName = "Town Dressing";
    const string ActiveKey = "TownDressingReview.Active";
    const string ModeKey = "TownDressingReview.Mode";
    const string PhaseKey = "TownDressingReview.Phase";
    const string RestoreKey = "TownDressingReview.Restore";
    const string BackgroundKey = "TownDressingReview.Background";
    const string SelectionKey = "TownDressingReview.Selection";
    [Serializable] public sealed class View
    {
        public string name, anchor;
        public Vector3 position, target;
        public float fieldOfView = 60f;
    }
    [Serializable] public sealed class Preserved
    {
        public string id, path, signature;
    }
    [Serializable] public sealed class Baseline
    {
        public string scene, createdUtc;
        public int targetHouses;
        public List<View> views = new List<View>();
        public List<Preserved> objects = new List<Preserved>();
    }
    [Serializable] public sealed class Check { public string name, evidence; public bool passed; }
    [Serializable] public sealed class Report
    {
        public string mode, status = "running", startedUtc, finishedUtc, error;
        public string measurement = "Same saved world-camera poses before/after. Gameplay, animation and time frozen; fixed 1280x720 render target. Five-second warm-up, thirty-second frame sample. Editor frame timing includes editor overhead; <=10% mean increase is the acceptance criterion.";
        public bool allPassed;
        public float meanMs, medianMs, p95Ms, beforeMeanMs, meanIncreasePercent;
        public int frames, dressingObjects, dressingRenderers, dressingColliders, targetHouses;
        public List<string> images = new List<string>(), errors = new List<string>();
        public List<Check> checks = new List<Check>();
    }
    [Serializable] sealed class SavedScene { public string path; public bool isLoaded, isActive; }
    [Serializable] sealed class RestoreState { public SavedScene[] scenes; }
    [Serializable] sealed class SignatureRow { public string path, content; }
    [Serializable] sealed class Stamp
    {
        public string signature, recordedUtc;
        public int objects;
        public List<SignatureRow> rows;
    }
    [Serializable] sealed class RowChange { public string path, before, after; }
    [Serializable] sealed class SignatureDiff { public List<RowChange> changed = new List<RowChange>(); }
    static Report report;
    static Baseline baseline;
    static Camera reviewCamera;
    static RenderTexture benchmarkTexture;
    static readonly List<float> samples = new List<float>();
    static readonly Dictionary<string, string> dependencyHashes = new Dictionary<string, string>();
    static double deadline;
    static int lastFrame = -1;

    static TownDressingReviewChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += PlayChanged;
        Application.logMessageReceived += OnLog;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
    }

    [MenuItem("GameJam/Town Dressing/Review 1 - Capture BEFORE and baseline")]
    public static void CaptureBefore() => Begin("before");
    [MenuItem("GameJam/Town Dressing/Review 2 - Capture AFTER and compare")]
    public static void CaptureAfter() => Begin("after");
    [MenuItem("GameJam/Town Dressing/Review - Audit preservation and dressing (read only)")]
    public static void Audit()
    {
        RequireEditableScene();
        if (SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open Volcano_Demo for the read-only audit.");
        baseline = Read<Baseline>(Folder + "/baseline.json");
        report = NewReport("audit");
        AuditScene(report, baseline);
        CompleteReport();
        Write(Folder + "/audit.json", report);
        Debug.Log("TOWN_DRESSING_AUDIT " + report.status + ": " + Folder + "/audit.json");
    }
    [MenuItem("GameJam/Town Dressing/Review - Record current dressing for idempotency")]
    public static void RecordIdempotency()
    {
        RequireEditableScene();
        Transform root = SingleDressingRoot();
        List<SignatureRow> rows = DressingRows(root);
        Write(Folder + "/idempotency-baseline.json", new Stamp
        {
            signature = RowsHash(rows), objects = root.GetComponentsInChildren<Transform>(true).Length,
            recordedUtc = DateTime.UtcNow.ToString("O"), rows = rows
        });
        Debug.Log("TOWN_DRESSING_IDEMPOTENCY_RECORDED. Run the authoring menu again, then Verify idempotency.");
    }
    [MenuItem("GameJam/Town Dressing/Review - Verify idempotency (read only)")]
    public static void VerifyIdempotency()
    {
        RequireEditableScene();
        var stamp = Read<Stamp>(Folder + "/idempotency-baseline.json");
        Transform root = SingleDressingRoot();
        report = NewReport("idempotency");
        Add(report, "one_dressing_root", SceneTransforms().Count(t => t.name == DressingName && t.parent == null) == 1, DressingName);
        List<SignatureRow> currentRows = DressingRows(root);
        string signature = RowsHash(currentRows);
        Add(report, "repeat_authoring_preserves_semantic_content", signature == stamp.signature,
            "before=" + stamp.signature + "; after=" + signature);
        var diff = new SignatureDiff();
        if (stamp.rows != null)
        {
            var oldRows = stamp.rows.ToDictionary(r => r.path, r => r.content, StringComparer.Ordinal);
            var newRows = currentRows.ToDictionary(r => r.path, r => r.content, StringComparer.Ordinal);
            foreach (string path in oldRows.Keys.Union(newRows.Keys).OrderBy(p => p, StringComparer.Ordinal))
            {
                oldRows.TryGetValue(path, out string before); newRows.TryGetValue(path, out string after);
                if (before != after) diff.changed.Add(new RowChange { path = path, before = before, after = after });
            }
            Add(report, "per_object_signature_rows_identical", diff.changed.Count == 0,
                "changed=" + diff.changed.Count + ": " + string.Join("; ", diff.changed.Select(c => c.path)));
        }
        else Add(report, "diagnostic_baseline_has_rows", false, "Record a fresh baseline with this review version, then rerun authoring.");
        Write(Folder + "/idempotency-diff.json", diff);
        Add(report, "repeat_authoring_preserves_object_count", root.GetComponentsInChildren<Transform>(true).Length == stamp.objects,
            "before=" + stamp.objects + "; after=" + root.GetComponentsInChildren<Transform>(true).Length);
        CompleteReport(); Write(Folder + "/idempotency.json", report);
        Debug.Log("TOWN_DRESSING_IDEMPOTENCY " + report.status + ": " + Folder + "/idempotency.json");
    }

    static void Begin(string mode)
    {
        RequireEditableScene();
        Directory.CreateDirectory(Folder);
        SessionState.SetString(RestoreKey, JsonUtility.ToJson(new RestoreState
        {
            scenes = EditorSceneManager.GetSceneManagerSetup().Select(s => new SavedScene { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive }).ToArray()
        }));
        SessionState.SetString(SelectionKey, Selection.activeObject ? GlobalObjectId.GetGlobalObjectIdSlow(Selection.activeObject).ToString() : "");
        SessionState.SetBool(BackgroundKey, Application.runInBackground);
        if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        var roots = SceneTransforms().Where(t => t.parent == null && t.name == DressingName).ToArray();
        try
        {
            report = NewReport(mode);
            if (mode == "before")
            {
                if (roots.Length != 0) throw new InvalidOperationException("Capture BEFORE before adding Town Dressing. This tool will not delete or disable authored content to fabricate a baseline.");
                baseline = MakeBaseline();
                Write(Folder + "/baseline.json", baseline);
                Add(report, "before_has_no_dressing_root", true, "Original scene captured before authoring.");
                Add(report, "five_original_target_houses", baseline.targetHouses == 5, "count=" + baseline.targetHouses);
            }
            else
            {
                baseline = Read<Baseline>(Folder + "/baseline.json");
                AuditScene(report, baseline);
            }
            Write(Folder + "/" + mode + ".json", report);
            SessionState.SetString(ModeKey, mode);
            SessionState.SetString(PhaseKey, "start");
            SessionState.SetBool(ActiveKey, true);
            EditorApplication.EnterPlaymode();
        }
        catch { RestoreEditor(); throw; }
    }

    static void RequireEditableScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SessionState.GetBool(ActiveKey, false))
            throw new InvalidOperationException("Run this explicit review in Edit Mode after compilation.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save your open scenes first. The review will never save or discard scene edits.");
    }
    static Report NewReport(string mode) => new Report { mode = mode, startedUtc = DateTime.UtcNow.ToString("O") };
    static IEnumerable<Transform> SceneTransforms() => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true));
    static Transform SingleDressingRoot()
    {
        var roots = SceneTransforms().Where(t => t.parent == null && t.name == DressingName).ToArray();
        if (roots.Length != 1) throw new InvalidOperationException("Expected exactly one Town Dressing root; found " + roots.Length + ".");
        return roots[0];
    }
    static bool IsDressing(Transform item)
    {
        for (Transform t = item; t; t = t.parent) if (!t.parent && t.name == DressingName) return true;
        return false;
    }

    static Baseline MakeBaseline()
    {
        var result = new Baseline { scene = ScenePath, createdUtc = DateTime.UtcNow.ToString("O") };
        var all = SceneTransforms().ToArray();
        var houses = all.Select(t => t.GetComponent<BurnableBuilding>()).Where(h => h).ToArray();
        result.targetHouses = houses.Length;
        result.objects = Preserve(all.Where(t => !IsDressing(t)));
        Transform door = all.FirstOrDefault(t => t.name.Equals("opendoor", StringComparison.OrdinalIgnoreCase));
        Transform firstHouse = door ? AncestorHouse(door) : null;
        if (!firstHouse) firstHouse = houses.Select(h => h.transform).FirstOrDefault(t => t.name == "House_Green_Prefab");
        if (!firstHouse) throw new InvalidOperationException("Could not locate the original first house/opendoor. Baseline camera anchors require the authored house.");
        Bounds firstBounds = RendererBounds(firstHouse);
        Bounds town = firstBounds;
        foreach (var house in houses) town.Encapsulate(RendererBounds(house.transform));
        Vector3 townGround = town.center; townGround.y = GroundHeight(townGround, firstBounds.min.y);
        Vector3 doorCenter = door ? RendererBounds(door).center : firstBounds.center + firstHouse.forward * firstBounds.extents.z;
        Vector3 outward = Vector3.ProjectOnPlane(doorCenter - firstBounds.center, Vector3.up).normalized;
        if (outward.sqrMagnitude < .1f) outward = firstHouse.forward;
        Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
        Vector3 firstGround = doorCenter; firstGround.y = GroundHeight(doorCenter, firstBounds.min.y);
        Vector3 overviewOffset = (outward * .85f + side * .35f).normalized * Mathf.Max(32f, town.size.magnitude * .65f);
        result.views.Add(MakeView("town-overview", townGround + overviewOffset + Vector3.up * Mathf.Max(24f, town.size.magnitude * .36f), town.center, 58f, "Five existing house renderer bounds"));
        float exteriorDistance = Mathf.Max(16f, firstBounds.extents.magnitude * 1.05f);
        Vector3 exteriorFocus = Vector3.Lerp(firstBounds.center, doorCenter, .45f);
        result.views.Add(MakeView("first-exterior", firstGround + outward * exteriorDistance + side * 6f +
            Vector3.up * Mathf.Max(5f, firstBounds.size.y * .42f), exteriorFocus, 58f, Path(firstHouse)));
        Transform table = all.Where(t => t.name.IndexOf("table", StringComparison.OrdinalIgnoreCase) >= 0 && t.GetComponentInChildren<Renderer>())
            .OrderBy(t => (RendererBounds(t).center - firstBounds.center).sqrMagnitude).FirstOrDefault();
        Vector3 inside = firstGround - outward * 1.2f + side * .22f + Vector3.up * 3.4f;
        inside.y = HouseFloor(firstHouse, inside, firstBounds.min.y) + 3.4f;
        Vector3 insideTarget = firstBounds.center - outward * 2f;
        if (table && (RendererBounds(table).center - firstBounds.center).sqrMagnitude < firstBounds.extents.sqrMagnitude)
        {
            Bounds tableBounds = RendererBounds(table);
            var furniture = all.Where(t => t.GetComponent<Renderer>() &&
                (t.name.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("cabinet", StringComparison.OrdinalIgnoreCase) >= 0) &&
                Vector3.Distance(RendererBounds(t).center, firstBounds.center) < 30f).ToArray();
            Bounds room = tableBounds;
            foreach (Transform item in furniture) room.Encapsulate(RendererBounds(item));
            insideTarget = room.center;
            insideTarget.y = Mathf.Lerp(tableBounds.min.y, tableBounds.max.y, .65f);
            Vector3 tableTarget = new Vector3(tableBounds.center.x, tableBounds.max.y + .08f, tableBounds.center.z);
            Vector3[] directions =
            {
                Vector3.left, new Vector3(-1f, 0f, -.4f).normalized, -firstHouse.right, -firstHouse.forward,
                firstHouse.right, firstHouse.forward, (-firstHouse.right - firstHouse.forward).normalized,
                (firstHouse.right - firstHouse.forward).normalized, (firstHouse.right + firstHouse.forward).normalized
            };
            Physics.SyncTransforms();
            bool found = false;
            foreach (float radius in new[] { 9f, 7.5f, 11f, 6f })
            {
                foreach (Vector3 direction in directions)
                {
                    Vector3 candidate = room.center + direction * radius;
                    candidate.y = HouseFloor(firstHouse, candidate, tableBounds.min.y) + 3.4f;
                    if (!firstBounds.Contains(candidate) || !CameraClear(candidate, tableTarget, table)) continue;
                    // A table-only sightline produced an underside close-up. Require a second
                    // room furnishing in view so the fixed frame actually reviews the interior.
                    if (furniture.Length > 0 && !furniture.Any(item => CameraClear(candidate, RendererBounds(item).center, item))) continue;
                    Vector3 forward = (insideTarget - candidate).normalized;
                    if (Vector3.Angle(forward, tableTarget - candidate) > 49f) continue;
                    inside = candidate; found = true; break;
                }
                if (found) break;
            }
            if (!found) throw new InvalidOperationException("No clear interior overview camera could see both the existing table and another room furnishing below the ceiling. Inspect the room before capturing; this tool will not hide walls.");
        }
        if (Vector3.Distance(inside, insideTarget) < .6f) insideTarget = inside - outward * 3f;
        result.views.Add(MakeView("first-interior", inside, insideTarget, 78f, "Room overview at original floor + 3.4m, clear rays to existing table and another furnishing: " + (table ? Path(table) : "none")));
        result.views.Add(MakeView("square", townGround + outward * Mathf.Max(13f, town.extents.magnitude * .32f) + side * 5f + Vector3.up * 3f, townGround + Vector3.up * 1.1f, 64f, "Original town centroid"));
        WaterSourceVolume source = all.Select(t => t.GetComponent<WaterSourceVolume>()).FirstOrDefault(w => w && w.waterSurface);
        Transform lake = source ? source.waterSurface : all.FirstOrDefault(t => t.name == "Lava (8)") ?? all.FirstOrDefault(t => t.name.IndexOf("Lake Water", StringComparison.OrdinalIgnoreCase) >= 0);
        if (!lake) lake = all.FirstOrDefault(t => t.GetComponent<WaterSourceVolume>());
        if (!lake) throw new InvalidOperationException("Could not locate original lake/water source camera anchor.");
        Bounds lakeBounds = RendererBounds(lake);
        if (lakeBounds.size.sqrMagnitude < 1f && lake.GetComponent<Collider>()) lakeBounds = lake.GetComponent<Collider>().bounds;
        Vector3 lakeCenter = lakeBounds.center;
        // Keep the original water and the intended station bank in the same BEFORE/AFTER
        // frame. A town-side camera places the future deck behind the far shoreline.
        Vector3 station = new Vector3(345f, lakeCenter.y, -590f);
        station.y = GroundHeight(station, lakeCenter.y);
        Bounds lakeFrame = new Bounds(station + Vector3.up * 2f, new Vector3(26f, 8f, 30f));
        lakeFrame.Encapsulate(lakeCenter);
        lakeFrame.Encapsulate(new Vector3(334.05f, lakeCenter.y, -605.8f));
        Vector3 lakeFocus = lakeFrame.center;
        Vector3 lakeCameraDirection = new Vector3(.85f, .62f, -.75f).normalized;
        float lakeDistance = Mathf.Max(48f, lakeFrame.extents.magnitude / Mathf.Sin(31f * Mathf.Deg2Rad) * 1.12f);
        Vector3 lakeCamera = lakeFocus + lakeCameraDirection * lakeDistance;
        lakeCamera.y = Mathf.Max(lakeCamera.y, GroundHeight(lakeCamera, lakeFocus.y) + 8f);
        result.views.Add(MakeView("lake", lakeCamera, lakeFocus, 62f,
            Path(lake) + "; original water centre + tested intake + future station bank (345,-590)"));
        Transform shelter = all.FirstOrDefault(t => t.name == "Shelter Entrance Sign & Lamp") ??
            all.FirstOrDefault(t => t.name.IndexOf("Shelter", StringComparison.OrdinalIgnoreCase) >= 0 && t.GetComponentInChildren<Renderer>());
        if (!shelter) throw new InvalidOperationException("Could not locate original shelter sign/lamp camera anchor.");
        Transform backboard = shelter.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Wooden backboard");
        Bounds shelterBounds = RendererBounds(backboard ? backboard : shelter);
        // The authored lettering sits on negative local Z. Its aggregate mesh bounds are
        // unsuitable for framing the entrance; use the wooden board's actual renderer.
        Vector3 shelterFront = -shelter.forward;
        Vector3 shelterCamera = shelterBounds.center + shelterFront * 40f + shelter.right * 5f;
        float shelterFloor = GroundHeight(shelterBounds.center + shelterFront * 10f, 0f);
        shelterCamera.y = Mathf.Max(shelterFloor + 10f, GroundHeight(shelterCamera, shelterFloor) + 5f);
        Vector3 shelterFocus = shelterBounds.center;
        shelterFocus.y = Mathf.Lerp(shelterFloor, shelterBounds.center.y, .48f);
        result.views.Add(MakeView("shelter", shelterCamera, shelterFocus,
            58f, Path(backboard ? backboard : shelter)));
        return result;
    }
    static View MakeView(string name, Vector3 position, Vector3 target, float fov, string anchor) => new View
        { name = name, position = position, target = target, fieldOfView = fov, anchor = anchor };
    static Transform AncestorHouse(Transform door)
    {
        for (var t = door; t; t = t.parent) if (t.GetComponent<BurnableBuilding>() || t.name == "House_Green_Prefab") return t;
        return null;
    }
    static Bounds RendererBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
    static float GroundHeight(Vector3 position, float fallback)
    {
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            Vector3 p = position - terrain.transform.position, size = terrain.terrainData.size;
            if (p.x >= 0 && p.z >= 0 && p.x <= size.x && p.z <= size.z) return terrain.SampleHeight(position) + terrain.transform.position.y;
        }
        return fallback;
    }
    static float HouseFloor(Transform house, Vector3 point, float fallback)
    {
        float best = float.NegativeInfinity;
        foreach (Renderer renderer in house.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) < 0) continue;
            Bounds bounds = renderer.bounds;
            if (point.x >= bounds.min.x && point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z &&
                bounds.size.y < 1.5f) best = Mathf.Max(best, bounds.max.y);
        }
        return float.IsNegativeInfinity(best) ? fallback : best;
    }
    static bool CameraClear(Vector3 position, Vector3 target, Transform intendedFurniture)
    {
        if (Physics.CheckSphere(position, .14f, ~0, QueryTriggerInteraction.Ignore)) return false;
        Vector3 ray = target - position;
        foreach (RaycastHit hit in Physics.RaycastAll(position, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (hit.collider is CharacterController) continue;
            return hit.transform.IsChildOf(intendedFurniture);
        }
        return true;
    }

    static List<Preserved> Preserve(IEnumerable<Transform> transforms)
    {
        dependencyHashes.Clear();
        return transforms.Select(t => new Preserved { id = GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject).ToString(), path = Path(t), signature = ObjectSignature(t) })
            .OrderBy(p => p.id, StringComparer.Ordinal).ToList();
    }
    static string ObjectSignature(Transform t)
    {
        var s = new StringBuilder();
        s.Append(t.name).Append('|').Append(t.parent ? GlobalObjectId.GetGlobalObjectIdSlow(t.parent.gameObject).ToString() : "root").Append('|');
        AppendVector(s, t.localPosition); AppendQuaternion(s, t.localRotation); AppendVector(s, t.localScale);
        s.Append(t.gameObject.activeSelf).Append('|').Append(t.gameObject.layer).Append('|').Append(t.tag).Append('|');
        foreach (Renderer renderer in t.GetComponents<Renderer>())
        {
            s.Append(renderer.GetType().FullName).Append('|').Append(renderer.enabled).Append('|');
            foreach (Material material in renderer.sharedMaterials) s.Append(AssetIdentity(material)).Append(';');
        }
        foreach (MeshFilter mesh in t.GetComponents<MeshFilter>()) s.Append("mesh:").Append(AssetIdentity(mesh.sharedMesh));
        foreach (SkinnedMeshRenderer skin in t.GetComponents<SkinnedMeshRenderer>()) s.Append("skin:").Append(AssetIdentity(skin.sharedMesh));
        foreach (Collider collider in t.GetComponents<Collider>())
        {
            s.Append(collider.GetType().FullName).Append(collider.enabled).Append(collider.isTrigger).Append(AssetIdentity(collider.sharedMaterial));
            if (collider is BoxCollider box) { AppendVector(s, box.center); AppendVector(s, box.size); }
            else if (collider is SphereCollider sphere) { AppendVector(s, sphere.center); s.Append(F(sphere.radius)); }
            else if (collider is CapsuleCollider capsule) { AppendVector(s, capsule.center); s.Append(F(capsule.radius)).Append(F(capsule.height)).Append(capsule.direction); }
            else if (collider is MeshCollider meshCollider) s.Append(AssetIdentity(meshCollider.sharedMesh)).Append(meshCollider.convex);
        }
        return Hash(s.ToString());
    }
    static string AssetIdentity(Object asset)
    {
        if (!asset) return "null";
        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path)) return asset.GetType().FullName + ":" + asset.name;
        if (!dependencyHashes.TryGetValue(path, out string hash)) dependencyHashes[path] = hash = AssetDatabase.GetAssetDependencyHash(path).ToString();
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long local);
        return guid + ":" + local + ":" + hash;
    }
    static List<SignatureRow> DressingRows(Transform root)
    {
        dependencyHashes.Clear();
        var rows = new List<SignatureRow>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            string path = SemanticPath(t, root);
            var s = new StringBuilder(path); AppendVector(s, t.localPosition); AppendQuaternion(s, t.localRotation); AppendVector(s, t.localScale);
            s.Append(t.gameObject.activeSelf).Append(t.gameObject.layer);
            foreach (Renderer r in t.GetComponents<Renderer>())
            {
                s.Append("|renderer:").Append(r.enabled).Append(':').Append(r.shadowCastingMode).Append(':').Append(r.receiveShadows);
                foreach (Material m in r.sharedMaterials) s.Append("|material:").Append(AssetIdentity(m));
            }
            foreach (MeshFilter m in t.GetComponents<MeshFilter>()) s.Append("|mesh:").Append(AssetIdentity(m.sharedMesh));
            foreach (TextMesh text in t.GetComponents<TextMesh>())
            {
                s.Append("|text:").Append(text.text).Append('|').Append(AssetIdentity(text.font)).Append('|').Append(text.fontSize)
                    .Append('|').Append(F(text.characterSize)).Append('|').Append(text.fontStyle).Append('|').Append(text.anchor).Append('|').Append(text.alignment)
                    .Append('|').Append(F(text.lineSpacing)).Append('|').Append(F(text.offsetZ)).Append('|').Append(text.richText);
                AppendColor(s, text.color);
            }
            foreach (Light light in t.GetComponents<Light>())
            {
                s.Append("|light:").Append(light.enabled).Append('|').Append(light.type).Append('|').Append(F(light.range)).Append('|').Append(F(light.intensity))
                    .Append('|').Append(light.shadows).Append('|').Append(light.cullingMask); AppendColor(s, light.color);
            }
            foreach (WorldLetteringFontAtlas atlas in t.GetComponents<WorldLetteringFontAtlas>()) s.Append("|font-atlas-binder:").Append(atlas.enabled);
            foreach (Collider c in t.GetComponents<Collider>())
            {
                s.Append(c.GetType().Name).Append(c.enabled).Append(c.isTrigger);
                if (c is BoxCollider box) { AppendVector(s, box.center); AppendVector(s, box.size); }
                if (c is MeshCollider mesh) s.Append(AssetIdentity(mesh.sharedMesh)).Append(mesh.convex);
            }
            rows.Add(new SignatureRow { path = path, content = s.ToString() });
        }
        return rows.OrderBy(r => r.path, StringComparer.Ordinal).ToList();
    }
    static string RowsHash(List<SignatureRow> rows) => Hash(string.Join("\n", rows.Select(r => r.content)));
    static string SemanticPath(Transform item, Transform root)
    {
        if (item == root) return root.name;
        var parts = new List<string>();
        for (Transform t = item; t && t != root; t = t.parent) parts.Add(t.name + "[" + t.GetSiblingIndex() + "]");
        parts.Add(root.name); parts.Reverse(); return string.Join("/", parts);
    }

    static void AuditScene(Report output, Baseline before)
    {
        var all = SceneTransforms().ToArray();
        var roots = all.Where(t => t.parent == null && t.name == DressingName).ToArray();
        Add(output, "one_dressing_root", roots.Length == 1, "count=" + roots.Length);
        output.targetHouses = all.Count(t => t.GetComponent<BurnableBuilding>());
        Add(output, "exactly_five_target_houses", output.targetHouses == 5 && before.targetHouses == 5, "count=" + output.targetHouses);
        var current = Preserve(all.Where(t => !IsDressing(t))).ToDictionary(p => p.id, p => p);
        var missing = before.objects.Where(p => !current.ContainsKey(p.id)).Select(p => p.path).ToArray();
        var changed = before.objects.Where(p => current.TryGetValue(p.id, out var now) && p.signature != now.signature).Select(p => p.path).ToArray();
        Add(output, "all_original_scene_objects_preserved", missing.Length == 0, "originals=" + before.objects.Count + "; missing=" + string.Join("; ", missing.Take(40)));
        Add(output, "original_transforms_materials_meshes_and_colliders_unchanged", changed.Length == 0, "changed=" + changed.Length + ": " + string.Join("; ", changed.Take(40)));
        if (roots.Length != 1) return;
        Transform root = roots[0];
        output.dressingObjects = root.GetComponentsInChildren<Transform>(true).Length;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var colliders = root.GetComponentsInChildren<Collider>(true);
        output.dressingRenderers = renderers.Length; output.dressingColliders = colliders.Length;
        var missingMaterials = renderers.Where(r => r.sharedMaterials.Length == 0 || r.sharedMaterials.Any(m => !m || !m.shader || !m.shader.isSupported)).Select(r => Path(r.transform)).ToArray();
        Add(output, "dressing_materials_and_shaders_valid", missingMaterials.Length == 0, string.Join("; ", missingMaterials));
        var badMeshes = root.GetComponentsInChildren<MeshFilter>(true).Where(m => !m.sharedMesh || m.sharedMesh.vertexCount == 0).Select(m => Path(m.transform)).ToArray();
        Add(output, "dressing_meshes_valid", badMeshes.Length == 0, string.Join("; ", badMeshes));
        var invalidColliders = colliders.Where(c => c is MeshCollider mesh && !mesh.sharedMesh || c is BoxCollider box && (box.size.x <= 0 || box.size.y <= 0 || box.size.z <= 0)).Select(c => Path(c.transform)).ToArray();
        Add(output, "authored_colliders_valid", invalidColliders.Length == 0 && colliders.Length > 0, "count=" + colliders.Length + "; invalid=" + string.Join("; ", invalidColliders));
        Add(output, "no_extra_burnable_targets_in_dressing", root.GetComponentsInChildren<BurnableBuilding>(true).Length == 0, "No decorative objects added to rescue objective.");
        var duplicates = root.Cast<Transform>().GroupBy(t => t.name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Add(output, "unique_dressing_top_level_groups", duplicates.Length == 0, string.Join("; ", duplicates.Take(40)));
    }

    static void Update()
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.isCompiling) return;
        try
        {
            string mode = SessionState.GetString(ModeKey, "before"), phase = SessionState.GetString(PhaseKey, "start");
            if (phase == "start")
            {
                report = Read<Report>(Folder + "/" + mode + ".json"); baseline = Read<Baseline>(Folder + "/baseline.json");
                Application.runInBackground = true; samples.Clear(); lastFrame = -1;
                Next("settle", 1); return;
            }
            if (phase == "settle")
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                PrepareRuntime();
                foreach (View view in baseline.views) { Capture(view, 1280, 720); Capture(view, 1920, 1080); }
                ApplyView(baseline.views.First(v => v.name == "town-overview"));
                benchmarkTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { name = "Town review matched performance target", hideFlags = HideFlags.HideAndDontSave };
                reviewCamera.targetTexture = benchmarkTexture; reviewCamera.aspect = 16f / 9f; reviewCamera.enabled = true;
                Next("warm", 5); return;
            }
            if (phase == "warm")
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                samples.Clear(); lastFrame = Time.frameCount; Next("measure", 30); return;
            }
            if (phase == "measure")
            {
                if (Time.frameCount != lastFrame)
                {
                    lastFrame = Time.frameCount;
                    float ms = Time.unscaledDeltaTime * 1000f;
                    if (ms > 0 && !float.IsNaN(ms) && !float.IsInfinity(ms)) samples.Add(ms);
                }
                if (EditorApplication.timeSinceStartup < deadline) return;
                report.frames = samples.Count;
                if (samples.Count > 0)
                {
                    var sorted = samples.OrderBy(v => v).ToArray();
                    report.meanMs = samples.Average(); report.medianMs = sorted[sorted.Length / 2]; report.p95Ms = sorted[Mathf.Clamp((int)(sorted.Length * .95f), 0, sorted.Length - 1)];
                }
                Add(report, "thirty_second_matched_frame_sample", samples.Count >= 30, "frames=" + samples.Count + "; meanMs=" + report.meanMs + "; medianMs=" + report.medianMs);
                Add(report, "six_views_at_two_resolutions", report.images.Count == 12, "images=" + report.images.Count);
                if (mode == "after")
                {
                    var before = Read<Report>(Folder + "/before.json"); report.beforeMeanMs = before.meanMs;
                    report.meanIncreasePercent = before.meanMs > 0 ? (report.meanMs / before.meanMs - 1f) * 100f : float.PositiveInfinity;
                    Add(report, "average_frame_time_increase_at_most_ten_percent", before.allPassed && report.meanIncreasePercent <= 10f,
                        "before=" + before.meanMs + "ms; after=" + report.meanMs + "ms; increase=" + report.meanIncreasePercent + "%");
                }
                Finish(null);
            }
        }
        catch (Exception exception) { Finish(exception.ToString()); }
    }
    static void PrepareRuntime()
    {
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        Camera source = player && player.viewCamera ? player.viewCamera : Camera.main;
        if (!source) throw new InvalidOperationException("A source world camera is required for matching rendering settings.");
        var sourceData = source.GetUniversalAdditionalCameraData();
        var reviewObject = new GameObject("Town Dressing Review Camera") { hideFlags = HideFlags.HideAndDontSave };
        reviewCamera = reviewObject.AddComponent<Camera>(); reviewCamera.CopyFrom(source); reviewCamera.enabled = false;
        reviewCamera.targetTexture = null; reviewCamera.useOcclusionCulling = false; reviewCamera.nearClipPlane = .04f;
        var data = reviewCamera.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base; data.renderPostProcessing = sourceData.renderPostProcessing;
        data.volumeLayerMask = sourceData.volumeLayerMask; data.volumeTrigger = reviewCamera.transform;
        data.antialiasing = sourceData.antialiasing; data.antialiasingQuality = sourceData.antialiasingQuality;
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (camera != reviewCamera) camera.enabled = false;
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)) canvas.enabled = false;
        // Freeze only runtime objects in Play Mode; entering Edit Mode restores the original scene.
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (behaviour.gameObject.scene.path == ScenePath && !(behaviour is UniversalAdditionalCameraData) &&
                !(behaviour is UnityEngine.Rendering.Volume) && !(behaviour is WorldLetteringFontAtlas)) behaviour.enabled = false;
        foreach (Animator animator in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None)) animator.enabled = false;
        foreach (ParticleSystem particles in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) particles.Pause(true);
        Time.timeScale = 0f; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }
    static void ApplyView(View view)
    {
        reviewCamera.transform.SetPositionAndRotation(view.position, Quaternion.LookRotation(view.target - view.position, Vector3.up));
        reviewCamera.fieldOfView = view.fieldOfView;
    }
    static void Capture(View view, int width, int height)
    {
        ApplyView(view);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        var previousActive = RenderTexture.active;
        try
        {
            reviewCamera.targetTexture = target; reviewCamera.aspect = (float)width / height; reviewCamera.Render();
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            string path = Folder + "/" + report.mode + "/" + view.name + "-" + height + "p.png";
            Directory.CreateDirectory(PathDirectory(path)); File.WriteAllBytes(path, pixels.EncodeToPNG()); report.images.Add(path);
        }
        finally
        {
            RenderTexture.active = previousActive; reviewCamera.targetTexture = null;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
        }
    }
    static void Next(string phase, double seconds) { SessionState.SetString(PhaseKey, phase); deadline = EditorApplication.timeSinceStartup + seconds; }
    static void CompleteReport()
    {
        report.allPassed = string.IsNullOrEmpty(report.error) && report.errors.Count == 0 && report.checks.Count > 0 && report.checks.All(c => c.passed);
        report.status = report.allPassed ? "passed" : "failed"; report.finishedUtc = DateTime.UtcNow.ToString("O");
    }
    static void Finish(string error)
    {
        if (report != null)
        {
            report.error = error; CompleteReport(); Write(Folder + "/" + report.mode + ".json", report);
            Debug.Log("TOWN_DRESSING_REVIEW " + report.mode + " " + report.status + ": " + Folder + "/" + report.mode + ".json");
        }
        SessionState.SetString(PhaseKey, "finished");
        if (reviewCamera) reviewCamera.targetTexture = null;
        if (benchmarkTexture) { benchmarkTexture.Release(); Object.DestroyImmediate(benchmarkTexture); }
        if (reviewCamera) Object.DestroyImmediate(reviewCamera.gameObject);
        Time.timeScale = 1f;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else RestoreEditor();
    }
    static void PlayChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(ActiveKey, false)) return;
        if (SessionState.GetString(PhaseKey, "") != "finished")
        {
            string mode = SessionState.GetString(ModeKey, "before");
            report = report ?? Read<Report>(Folder + "/" + mode + ".json");
            report.error = "Play Mode ended before review completed."; CompleteReport(); Write(Folder + "/" + mode + ".json", report);
        }
        RestoreEditor();
    }
    static void RestoreEditor()
    {
        SessionState.SetBool(ActiveKey, false);
        Application.runInBackground = SessionState.GetBool(BackgroundKey, false); Time.timeScale = 1f;
        string saved = SessionState.GetString(RestoreKey, "");
        if (!string.IsNullOrEmpty(saved))
        {
            var state = JsonUtility.FromJson<RestoreState>(saved);
            if (state?.scenes != null && state.scenes.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(state.scenes.Select(s => new SceneSetup { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive }).ToArray());
        }
        if (GlobalObjectId.TryParse(SessionState.GetString(SelectionKey, ""), out GlobalObjectId id)) Selection.activeObject = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
    }
    static void OnLog(string text, string stack, LogType type)
    {
        if (SessionState.GetBool(ActiveKey, false) && report != null && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) report.errors.Add(text);
    }
    static void BeforeReload()
    {
        string phase = SessionState.GetString(PhaseKey, "");
        if (SessionState.GetBool(ActiveKey, false) && EditorApplication.isPlaying && phase != "start" && phase != "finished")
            Finish("Compilation interrupted the review. Re-run after compilation.");
    }
    static string Path(Transform t, Transform stop = null)
    {
        string value = t.name;
        while (t.parent && t != stop) { t = t.parent; value = t.name + "/" + value; }
        return value;
    }
    static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    static void AppendVector(StringBuilder s, Vector3 v) => s.Append('|').Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z));
    static void AppendQuaternion(StringBuilder s, Quaternion q) => s.Append('|').Append(F(q.x)).Append(',').Append(F(q.y)).Append(',').Append(F(q.z)).Append(',').Append(F(q.w));
    static void AppendColor(StringBuilder s, Color c) => s.Append('|').Append(F(c.r)).Append(',').Append(F(c.g)).Append(',').Append(F(c.b)).Append(',').Append(F(c.a));
    static string Hash(string text)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }
    static string PathDirectory(string path) => System.IO.Path.GetDirectoryName(path);
    static T Read<T>(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Missing review evidence: " + path + ". Capture BEFORE first.");
        return JsonUtility.FromJson<T>(File.ReadAllText(path));
    }
    static void Write(string path, object value) { Directory.CreateDirectory(PathDirectory(path)); File.WriteAllText(path, JsonUtility.ToJson(value, true)); }
    static void Add(Report output, string name, bool passed, string evidence) => output.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
}
