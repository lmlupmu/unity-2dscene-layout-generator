using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SceneLayoutTool
{
    public class SceneLayoutApplier : EditorWindow
    {
        private string jsonText = "";
        private Vector2 scrollPos;
        private int selectedSchemeIndex = 0;
        private List<string> schemeNames = new List<string>();
        private LayoutScheme loadedSchemes;
        private float groundY = -5.15f;
        private Vector2 validationScrollPos;

        [MenuItem("Tools/Scene Layout/Apply Layout Scheme from JSON")]
        public static void OpenWindow()
        {
            GetWindow<SceneLayoutApplier>("场景布局方案应用器");
        }

        private void OnGUI()
        {
            GUILayout.Label("场景布局方案 JSON 应用器", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "使用步骤：\n" +
                "1. 先用 Export 工具导出当前场景，交给 AI 生成布局方案 JSON\n" +
                "2. 把 AI 返回的纯净 JSON 粘贴到下方（不要带 ``` 代码块标记）\n" +
                "3. 点击「解析方案」，选择要应用的方案\n" +
                "4A. 应用到当前打开的场景（修改 Transform）\n" +
                "4B. 另存为新的 Level 场景（推荐用于 Level2/3/4）",
                MessageType.Info);

            GUILayout.Space(8);

            GUILayout.Label("粘贴 AI 生成的 JSON 方案：", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(220));
            jsonText = EditorGUILayout.TextArea(jsonText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("从文件加载 JSON", GUILayout.Height(30)))
            {
                string path = EditorUtility.OpenFilePanel("选择布局方案 JSON", "", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    jsonText = File.ReadAllText(path, Encoding.UTF8);
                    Debug.Log("[SceneLayoutTool] JSON 加载完成: " + path);
                }
            }
            if (GUILayout.Button("▶ 解析方案", GUILayout.Height(30)))
            {
                ParseSchemes();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(12);

            if (schemeNames.Count > 0)
            {
                GUILayout.Label("检测到 " + schemeNames.Count + " 套方案：", EditorStyles.boldLabel);
                selectedSchemeIndex = EditorGUILayout.Popup("选择方案", selectedSchemeIndex, schemeNames.ToArray());

                if (selectedSchemeIndex >= 0 && selectedSchemeIndex < loadedSchemes.schemes.Length)
                {
                    var scheme = loadedSchemes.schemes[selectedSchemeIndex];
                    EditorGUILayout.HelpBox(
                        "难度: " + scheme.difficultyTag + "\n" +
                        "思路: " + scheme.designRationale,
                        MessageType.None);
                }

                GUILayout.Space(8);
                GUILayout.Label("应用操作：", EditorStyles.boldLabel);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("🔄 应用到当前场景", GUILayout.Height(36)))
                {
                    ApplySchemeToCurrentScene(selectedSchemeIndex);
                }
                if (GUILayout.Button("💾 另存为新场景 LevelX", GUILayout.Height(36)))
                {
                    SaveSchemeAsNewScene(selectedSchemeIndex);
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
                GUILayout.Label("物理弹弓校验：", EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label("地面Y坐标:", GUILayout.Width(80));
                groundY = EditorGUILayout.FloatField(groundY, GUILayout.Width(80));
                if (GUILayout.Button("🔍 校验贴地/支撑", GUILayout.Height(28)))
                {
                    ValidateGroundAdherence(selectedSchemeIndex);
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
                if (GUILayout.Button("🧪 一次性导出所有方案为独立场景 (Level2~LevelN)", GUILayout.Height(28)))
                {
                    SaveAllSchemesAsScenes();
                }
            }
        }

        // ========== JSON 解析（手写简易解析：兼容 Unity JsonUtility 的字段） ==========

        private void ParseSchemes()
        {
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                EditorUtility.DisplayDialog("错误", "JSON 为空，无法解析。", "OK");
                return;
            }

            // 去除可能的 markdown 代码块标记
            string cleanJson = jsonText.Trim();
            if (cleanJson.StartsWith("```"))
                cleanJson = cleanJson.Substring(3);
            if (cleanJson.EndsWith("```"))
                cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
            cleanJson = cleanJson.Trim();

            try
            {
                loadedSchemes = JsonUtility.FromJson<LayoutScheme>(cleanJson);
            }
            catch (System.Exception e)
            {
                // JsonUtility 解析失败可能是字段名不完全匹配，尝试更宽容地提取 schemes 数组
                Debug.LogWarning("[SceneLayoutTool] 初次解析失败，尝试兼容模式: " + e.Message);
                loadedSchemes = TolerantParse(cleanJson);
            }

            if (loadedSchemes == null || loadedSchemes.schemes == null || loadedSchemes.schemes.Length == 0)
            {
                EditorUtility.DisplayDialog("解析失败", "无法识别 JSON 格式。请确保格式与第六章一致，包含 schemes 数组。", "OK");
                return;
            }

            schemeNames.Clear();
            for (int i = 0; i < loadedSchemes.schemes.Length; i++)
            {
                var s = loadedSchemes.schemes[i];
                schemeNames.Add(s.schemeName + " [" + s.difficultyTag + "]  (" + (s.objects != null ? s.objects.Length : 0) + " 个物体)");
            }
            selectedSchemeIndex = 0;
            Debug.Log("[SceneLayoutTool] 解析成功！共 " + schemeNames.Count + " 套方案。");
        }

        // 兼容解析：有些 AI 可能漏掉顶层字段，这里用最小字段提取
        private LayoutScheme TolerantParse(string json)
        {
            LayoutScheme result = new LayoutScheme();
            try
            {
                // 提取 schemes 数组的起始和结束
                int arrStart = json.IndexOf("\"schemes\"") != -1 ? json.IndexOf('[', json.IndexOf("\"schemes\"")) : json.IndexOf('[');
                int depth = 0, arrEnd = arrStart;
                for (int i = arrStart; i < json.Length; i++)
                {
                    if (json[i] == '[') depth++;
                    else if (json[i] == ']') { depth--; if (depth == 0) { arrEnd = i; break; } }
                }
                string arrStr = json.Substring(arrStart, arrEnd - arrStart + 1);
                result.schemes = JsonUtility.FromJson<SchemeArrayWrapper>("{\"arr\":" + arrStr + "}").arr;
                return result;
            }
            catch
            {
                return null;
            }
        }

        [System.Serializable] private class SchemeArrayWrapper { public SchemeData[] arr; }

        // ========== 应用方案 ==========

        private void ApplySchemeToCurrentScene(int index)
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            SchemeData scheme = loadedSchemes.schemes[index];

            // 建立快速查找： hierarchyPath -> GameObject
            Dictionary<string, GameObject> pathMap = BuildHierarchyPathMap(scene);
            Dictionary<int, GameObject> idMap = BuildInstanceIDMap(scene);

            int applied = 0, skipped = 0, notFound = 0;
            StringBuilder log = new StringBuilder();

            foreach (var obj in scheme.objects)
            {
                GameObject target = null;
                // 优先按 hierarchyPath 查找
                if (!string.IsNullOrEmpty(obj.hierarchyPath) && pathMap.TryGetValue(obj.hierarchyPath, out target)) { }
                // 退而求其次按 name 匹配（第一个匹配的）
                else if (idMap != null && !string.IsNullOrEmpty(obj.name))
                {
                    foreach (var kv in pathMap)
                        if (kv.Value.name == obj.name) { target = kv.Value; break; }
                }

                if (target == null) { notFound++; continue; }

                Transform t = target.transform;
                Undo.RecordObject(t, "Apply Layout Transform " + target.name);

                // 应用新 Transform
                if (obj.newTransform != null)
                {
                    var p = obj.newTransform.position;
                    var r = obj.newTransform.rotation;
                    var s = obj.newTransform.scale;
                    t.position = new Vector3(p != null ? p.x : t.position.x, p != null ? p.y : t.position.y, p != null ? p.z : t.position.z);
                    t.eulerAngles = new Vector3(r != null ? r.x : t.eulerAngles.x, r != null ? r.y : t.eulerAngles.y, r != null ? r.z : t.eulerAngles.z);
                    t.localScale = new Vector3(s != null ? s.x : t.localScale.x, s != null ? s.y : t.localScale.y, s != null ? s.z : t.localScale.z);
                    applied++;
                }
                else skipped++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            string msg = string.Format("应用完成：成功{0} / 跳过{1} / 未找到{2} (共{3}物体)", applied, skipped, notFound, scheme.objects != null ? scheme.objects.Length : 0);
            Debug.Log("[SceneLayoutTool] " + msg);
            EditorUtility.DisplayDialog("方案应用完成", msg + "\n\n场景已标记为脏，请 Ctrl+S 保存。", "OK");
        }

        // ========== 另存为新场景 ==========

        private void SaveSchemeAsNewScene(int index)
        {
            string savePath = EditorUtility.SaveFilePanelInProject("另存为新场景", "Level" + (index + 2) + ".unity", "unity", "请选择保存路径");
            if (string.IsNullOrEmpty(savePath)) return;

            // 克隆当前场景到新场景，然后在新场景上应用方案
            Scene currentScene = EditorSceneManager.GetActiveScene();
            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            // 复制所有根物体
            foreach (GameObject root in currentScene.GetRootGameObjects())
            {
                GameObject clone = Instantiate(root);
                clone.name = root.name; // 去掉 "(Clone)" 后缀
                SceneManager.MoveGameObjectToScene(clone, newScene);
            }

            EditorSceneManager.SetActiveScene(newScene);

            // 应用方案
            SchemeData scheme = loadedSchemes.schemes[index];
            Dictionary<string, GameObject> pathMap = BuildHierarchyPathMap(newScene);
            int applied = 0, notFound = 0;
            foreach (var obj in scheme.objects)
            {
                GameObject target = null;
                if (!string.IsNullOrEmpty(obj.hierarchyPath) && pathMap.TryGetValue(obj.hierarchyPath, out target)) { }
                else if (!string.IsNullOrEmpty(obj.name))
                {
                    foreach (var kv in pathMap)
                        if (kv.Value.name == obj.name) { target = kv.Value; break; }
                }
                if (target == null) { notFound++; continue; }
                if (obj.newTransform != null)
                {
                    Transform t = target.transform;
                    var p = obj.newTransform.position;
                    var r = obj.newTransform.rotation;
                    var s = obj.newTransform.scale;
                    t.position = new Vector3(p != null ? p.x : t.position.x, p != null ? p.y : t.position.y, p != null ? p.z : t.position.z);
                    t.eulerAngles = new Vector3(r != null ? r.x : t.eulerAngles.x, r != null ? r.y : t.eulerAngles.y, r != null ? r.z : t.eulerAngles.z);
                    t.localScale = new Vector3(s != null ? s.x : t.localScale.x, s != null ? s.y : t.localScale.y, s != null ? s.z : t.localScale.z);
                    applied++;
                }
            }

            EditorSceneManager.SaveScene(newScene, savePath);
            EditorSceneManager.CloseScene(newScene, false);
            EditorSceneManager.SetActiveScene(currentScene);

            string msg = string.Format("新场景已保存至: {0}\n应用成功 {1} 个物体 / 未找到 {2} 个", savePath, applied, notFound);
            Debug.Log("[SceneLayoutTool] " + msg);
            EditorUtility.DisplayDialog("新场景创建成功", msg, "OK");
        }

        private void SaveAllSchemesAsScenes()
        {
            string baseFolder = EditorUtility.OpenFolderPanel("选择批量保存目录 (Assets 下的某个文件夹)", "Assets", "Scenes");
            if (string.IsNullOrEmpty(baseFolder)) return;
            // 转成相对路径
            if (baseFolder.StartsWith(Application.dataPath))
                baseFolder = "Assets" + baseFolder.Substring(Application.dataPath.Length);
            else
            {
                EditorUtility.DisplayDialog("路径错误", "请选择 Assets 目录内的文件夹。", "OK");
                return;
            }

            Scene currentScene = EditorSceneManager.GetActiveScene();
            StringBuilder report = new StringBuilder();

            for (int i = 0; i < loadedSchemes.schemes.Length; i++)
            {
                string sceneName = "Level" + (i + 2) + ".unity";
                string fullPath = Path.Combine(baseFolder, sceneName).Replace("\\", "/");

                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                foreach (GameObject root in currentScene.GetRootGameObjects())
                {
                    GameObject clone = Instantiate(root);
                    clone.name = root.name;
                    SceneManager.MoveGameObjectToScene(clone, newScene);
                }
                EditorSceneManager.SetActiveScene(newScene);

                var scheme = loadedSchemes.schemes[i];
                Dictionary<string, GameObject> pathMap = BuildHierarchyPathMap(newScene);
                int applied = 0, notFound = 0;
                foreach (var obj in scheme.objects)
                {
                    GameObject target = null;
                    if (!string.IsNullOrEmpty(obj.hierarchyPath) && pathMap.TryGetValue(obj.hierarchyPath, out target)) { }
                    else if (!string.IsNullOrEmpty(obj.name))
                    {
                        foreach (var kv in pathMap)
                            if (kv.Value.name == obj.name) { target = kv.Value; break; }
                    }
                    if (target == null) { notFound++; continue; }
                    if (obj.newTransform != null)
                    {
                        Transform t = target.transform;
                        var p = obj.newTransform.position;
                        var r = obj.newTransform.rotation;
                        var s = obj.newTransform.scale;
                        t.position = new Vector3(p.x, p.y, p.z);
                        t.eulerAngles = new Vector3(r.x, r.y, r.z);
                        t.localScale = new Vector3(s.x, s.y, s.z);
                        applied++;
                    }
                }

                EditorSceneManager.SaveScene(newScene, fullPath);
                EditorSceneManager.CloseScene(newScene, false);
                EditorSceneManager.SetActiveScene(currentScene);

                report.AppendLine(sceneName + " -> 应用" + applied + " / 未找到" + notFound + "    方案: " + scheme.schemeName);
                Debug.Log("[SceneLayoutTool] 创建: " + fullPath);
            }

            EditorUtility.DisplayDialog("批量导出完成", report.ToString(), "OK");
        }

        // ========== 物理弹弓贴地/支撑校验 ==========

        private void ValidateGroundAdherence(int schemeIndex)
        {
            if (loadedSchemes == null || loadedSchemes.schemes == null || schemeIndex >= loadedSchemes.schemes.Length)
            {
                EditorUtility.DisplayDialog("错误", "请先解析方案", "OK");
                return;
            }

            SchemeData scheme = loadedSchemes.schemes[schemeIndex];
            StringBuilder report = new StringBuilder();
            report.AppendLine("=== 贴地/支撑校验报告: " + scheme.schemeName + " ===\n");

            List<ObjectTransform> objects = new List<ObjectTransform>(scheme.objects);
            int floatingCount = 0, noSupportCount = 0;

            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (obj.isFixed) continue;
                if (obj.newTransform == null || obj.newTransform.position == null) continue;

                float posY = obj.newTransform.position.y;
                float halfSizeY = 0.7f;
                float bottomY = posY - halfSizeY;

                // Check ground adherence
                bool onGround = Mathf.Abs(bottomY - groundY) < 0.3f;

                // Check support from other objects
                bool hasSupport = false;
                string supportSource = "";
                for (int j = 0; j < objects.Count; j++)
                {
                    if (i == j) continue;
                    var other = objects[j];
                    if (other.isFixed) continue;
                    if (other.newTransform == null || other.newTransform.position == null) continue;

                    float otherTopY = other.newTransform.position.y + 0.7f;
                    float otherPosX = other.newTransform.position.x;
                    float objPosX = obj.newTransform.position.x;

                    bool xOverlap = Mathf.Abs(otherPosX - objPosX) < 1.5f;
                    bool yClose = Mathf.Abs(bottomY - otherTopY) < 0.3f;

                    if (xOverlap && yClose)
                    {
                        hasSupport = true;
                        supportSource = other.name;
                        break;
                    }
                }

                if (!onGround && !hasSupport)
                {
                    floatingCount++;
                    report.AppendLine("[悬空] " + obj.name + " (x=" + posY.ToString("F2") + ", bottomY=" + bottomY.ToString("F2") + ")");
                }
                else if (!onGround && hasSupport)
                {
                    report.AppendLine("[有支撑] " + obj.name + " → " + supportSource);
                }
            }

            report.AppendLine("\n统计: 悬空物体=" + floatingCount + ", 有支撑=" + (objects.Count - floatingCount));
            report.AppendLine("地面Y坐标: " + groundY.ToString("F2"));

            string reportStr = report.ToString();
            Debug.Log("[SceneLayoutTool] " + reportStr);

            if (floatingCount > 0)
            {
                EditorUtility.DisplayDialog("校验警告", reportStr + "\n\n存在悬空物体，请在Unity中调整或重新生成方案。", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("校验通过", reportStr + "\n\n所有物体均贴地或有支撑。", "OK");
            }
        }

        // ========== 辅助工具 ==========

        private Dictionary<string, GameObject> BuildHierarchyPathMap(Scene scene)
        {
            Dictionary<string, GameObject> map = new Dictionary<string, GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
                RecursiveAddPath(root, root.name, map);
            return map;
        }

        private void RecursiveAddPath(GameObject go, string path, Dictionary<string, GameObject> map)
        {
            // 避免同名覆盖，先到先得（通常 hierarchyPath 是唯一的）
            if (!map.ContainsKey(path)) map[path] = go;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                Transform child = go.transform.GetChild(i);
                RecursiveAddPath(child.gameObject, path + "/" + child.name, map);
            }
        }

        private Dictionary<int, GameObject> BuildInstanceIDMap(Scene scene) { return null; } // 预留，InstanceID在克隆后变化，不实用
    }

    // ========== JSON 结构（与 SKILL.md 第六章输出对应，可放宽字段） ==========

    [System.Serializable]
    public class LayoutScheme
    {
        public string generatedAt;
        public InputSummary inputSummary;
        public SchemeData[] schemes;
        public ValidationReport validationReport;
    }

    [System.Serializable] public class InputSummary
    {
        public string gameType;
        public string difficulty;
        public int objectCount;
        public int fixedCount;
    }

    [System.Serializable] public class SchemeData
    {
        public string schemeName;
        public string difficultyTag;
        public string designRationale;
        public ObjectTransform[] objects;
    }

    [System.Serializable] public class ObjectTransform
    {
        public string name;
        public string hierarchyPath;
        public bool isFixed;
        public TransformSerialize newTransform;
    }

    [System.Serializable] public class TransformSerialize
    {
        public Vec3Serialize position;
        public Vec3Serialize rotation;
        public Vec3Serialize scale;
    }

    [System.Serializable] public class Vec3Serialize
    {
        public float x;
        public float y;
        public float z;
    }

    [System.Serializable] public class ValidationReport
    {
        public bool collisionCheckPassed;
        public bool groundAdherenceCheckPassed;
        public bool supportCheckPassed;
        public bool solvabilityCheckPassed;
        public string notes;
    }
}
