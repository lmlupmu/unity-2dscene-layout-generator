using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SceneLayoutTool
{
    public class SceneObjectExporter : EditorWindow
    {
        [MenuItem("Tools/Scene Layout/Export Scene Objects to JSON")]
        public static void ExportSceneObjects()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            GameObject[] rootObjects = scene.GetRootGameObjects();

            List<SceneObjectExportData> exportList = new List<SceneObjectExportData>();

            foreach (GameObject root in rootObjects)
            {
                ProcessGameObject(root, root.name, exportList);
            }

            SceneExportPackage package = new SceneExportPackage
            {
                sceneName = scene.name,
                exportTime = System.DateTime.UtcNow.ToString("o"),
                objects = exportList.ToArray()
            };

            // Auto-detect groundY for physics puzzle games
            float detectedGroundY = DetectGroundY(exportList);
            package.groundY = detectedGroundY;
            package.boundary = DetectBoundary(exportList, detectedGroundY);

            string json = JsonUtility.ToJson(package, true);
            string path = EditorUtility.SaveFilePanel("Export Scene Objects JSON", "", scene.name + "_objects.json", "json");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, json, Encoding.UTF8);
                Debug.Log("[SceneLayoutTool] 已导出 " + exportList.Count + " 个物体到: " + path);
                string msg = "已导出 " + exportList.Count + " 个物体的清单到 JSON 文件。\n";
                msg += "自动检测: groundY=" + detectedGroundY.ToString("F2") + 
                       ", boundary=[" + package.boundary.minX + "," + package.boundary.maxX + "] x [" + 
                       package.boundary.minY + "," + package.boundary.maxY + "]\n";
                msg += "请把这个 JSON 内容粘贴给 AI，用于生成布局方案。";
                EditorUtility.DisplayDialog("导出成功", msg, "OK");
            }
        }

        private static void ProcessGameObject(GameObject go, string hierarchyPath, List<SceneObjectExportData> list)
        {
            // 跳过隐藏物体和临时物体
            if (go.hideFlags != HideFlags.None) return;

            SceneObjectExportData data = new SceneObjectExportData
            {
                name = go.name,
                hierarchyPath = hierarchyPath,
                category = GuessCategory(go),
                isFixed = IsFixedObject(go),
                bounds = GetRendererBounds(go),
                currentTransform = new TransformData
                {
                    position = new Vec3 { x = go.transform.position.x, y = go.transform.position.y, z = go.transform.position.z },
                    rotation = new Vec3 { x = go.transform.eulerAngles.x, y = go.transform.eulerAngles.y, z = go.transform.eulerAngles.z },
                    scale = new Vec3 { x = go.transform.localScale.x, y = go.transform.localScale.y, z = go.transform.localScale.z }
                },
                colliderType = GetColliderType(go),
                unityInstanceID = go.GetInstanceID()
            };

            list.Add(data);

            // 递归处理子物体
            for (int i = 0; i < go.transform.childCount; i++)
            {
                Transform child = go.transform.GetChild(i);
                ProcessGameObject(child.gameObject, hierarchyPath + "/" + child.name, list);
            }
        }

        // ================ 辅助判定函数 ================

        private static string GuessCategory(GameObject go)
        {
            string lowerName = go.name.ToLower();

            // 固定类优先级最高
            if (IsFixedObject(go)) return "fixed";

            // 收集物
            if (lowerName.Contains("star") || lowerName.Contains("coin") || lowerName.Contains("gem") ||
                lowerName.Contains("collect") || lowerName.Contains("pickup") || lowerName.Contains("key") ||
                lowerName.Contains("星星") || lowerName.Contains("金币") || lowerName.Contains("宝石"))
                return "collectible";

            // 陷阱
            if (lowerName.Contains("spike") || lowerName.Contains("trap") || lowerName.Contains("lava") ||
                lowerName.Contains("saw") || lowerName.Contains("poison") || lowerName.Contains("pit") ||
                lowerName.Contains("尖刺") || lowerName.Contains("陷阱") || lowerName.Contains("熔岩"))
                return "trap";

            // 敌人
            if (lowerName.Contains("enemy") || lowerName.Contains("monster") || lowerName.Contains("bot") ||
                lowerName.Contains("drone") || lowerName.Contains("turret") || lowerName.Contains("slime") ||
                lowerName.Contains("敌人") || lowerName.Contains("怪物"))
                return "enemy";

            // 装饰物 (无碰撞的视觉物件)
            Collider2D col2d = go.GetComponent<Collider2D>();
            Collider col3d = go.GetComponent<Collider>();
            if (col2d == null && col3d == null)
            {
                if (lowerName.Contains("grass") || lowerName.Contains("cloud") || lowerName.Contains("mushroom") ||
                    lowerName.Contains("tree") || lowerName.Contains("bush") || lowerName.Contains("flower") ||
                    lowerName.Contains("deco") || lowerName.Contains("背景") || lowerName.Contains("装饰") ||
                    lowerName.Contains("草") || lowerName.Contains("云") || lowerName.Contains("蘑菇"))
                    return "decoration";
            }

            // 默认：有碰撞体的非上述物体 = 平台
            return "platform";
        }

        private static bool IsFixedObject(GameObject go)
        {
            string lowerName = go.name.ToLower();
            if (lowerName == "player" || lowerName.Contains("spawn") || lowerName.Contains("startpoint") ||
                lowerName.Contains("出生点") || lowerName.Contains("玩家"))
                return true;
            if (lowerName.Contains("boundary") || lowerName.Contains("wall_left") || lowerName.Contains("wall_right") ||
                lowerName.Contains("wall_top") || lowerName.Contains("wall_bottom") || lowerName == "ground" ||
                lowerName.Contains("边界") || lowerName.Contains("墙体"))
                return true;
            if (lowerName.Contains("goal") || lowerName.Contains("endpoint") || lowerName.Contains("finish") ||
                lowerName.Contains("exit") || lowerName.Contains("flag") || lowerName.Contains("终点") || lowerName.Contains("旗"))
                return true;
            if (lowerName.Contains("faildetector") || lowerName.Contains("death") || lowerName.Contains("死亡"))
                return true;
            if (lowerName == "maincamera" || lowerName == "main camera" || lowerName.Contains("主相机") || lowerName.Contains("摄像机"))
                return true;
            if (lowerName.Contains("slingshot")) // 用户截图中的弹弓
                return true;

            // 标签判定
            if (go.CompareTag("Player") || go.CompareTag("Finish") || go.CompareTag("Respawn") || go.CompareTag("MainCamera"))
                return true;

            return false;
        }

        private static BoundsData GetRendererBounds(GameObject go)
        {
            Renderer r = go.GetComponent<Renderer>();
            Collider2D c2d = go.GetComponent<Collider2D>();
            Collider c3d = go.GetComponent<Collider>();

            Bounds b;
            if (r != null) b = r.bounds;
            else if (c2d != null) b = c2d.bounds;
            else if (c3d != null) b = c3d.bounds;
            else
            {
                // 没有任何可视化/碰撞体，估算为 transform scale
                return new BoundsData
                {
                    sizeX = Mathf.Max(go.transform.localScale.x, 0.1f),
                    sizeY = Mathf.Max(go.transform.localScale.y, 0.1f),
                    sizeZ = Mathf.Max(go.transform.localScale.z, 0.1f)
                };
            }
            return new BoundsData
            {
                sizeX = b.size.x,
                sizeY = b.size.y,
                sizeZ = b.size.z
            };
        }

        private static string GetColliderType(GameObject go)
        {
            if (go.GetComponent<BoxCollider2D>()) return "BoxCollider2D";
            if (go.GetComponent<CircleCollider2D>()) return "CircleCollider2D";
            if (go.GetComponent<PolygonCollider2D>()) return "PolygonCollider2D";
            if (go.GetComponent<CompositeCollider2D>()) return "CompositeCollider2D";
            if (go.GetComponent<CapsuleCollider2D>()) return "CapsuleCollider2D";
            if (go.GetComponent<BoxCollider>()) return "BoxCollider";
            if (go.GetComponent<SphereCollider>()) return "SphereCollider";
            if (go.GetComponent<MeshCollider>()) return "MeshCollider";
            return "None";
        }

        private static float DetectGroundY(List<SceneObjectExportData> objects)
        {
            foreach (var obj in objects)
            {
                if (obj.category == "fixed" && obj.name.ToLower().Contains("ground"))
                {
                    return obj.currentTransform.position.y;
                }
            }
            float minY = float.MaxValue;
            foreach (var obj in objects)
            {
                if (obj.currentTransform.position.y < minY)
                    minY = obj.currentTransform.position.y;
            }
            return minY;
        }

        private static BoundaryData DetectBoundary(List<SceneObjectExportData> objects, float groundY)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var obj in objects)
            {
                float x = obj.currentTransform.position.x;
                float y = obj.currentTransform.position.y;
                float halfX = obj.bounds.sizeX * obj.currentTransform.scale.x / 2;
                float halfY = obj.bounds.sizeY * obj.currentTransform.scale.y / 2;
                if (x - halfX < minX) minX = x - halfX;
                if (x + halfX > maxX) maxX = x + halfX;
                if (y - halfY < minY) minY = y - halfY;
                if (y + halfY > maxY) maxY = y + halfY;
            }
            float padX = (maxX - minX) * 0.15f;
            float padY = (maxY - minY) * 0.15f;
            return new BoundaryData
            {
                minX = Mathf.FloorToInt(minX - padX),
                maxX = Mathf.CeilToInt(maxX + padX),
                minY = groundY,
                maxY = Mathf.CeilToInt(maxY + padY)
            };
        }
    }

    // ================ 数据结构（与 SKILL.md 输入规范对应） =================

    [System.Serializable]
        public class SceneExportPackage
        {
            public string sceneName;
            public string exportTime;
            public SceneObjectExportData[] objects;
            public float groundY;
            public BoundaryData boundary;
        }

        [System.Serializable]
        public class BoundaryData
        {
            public float minX;
            public float maxX;
            public float minY;
            public float maxY;
        }

    [System.Serializable]
    public class SceneObjectExportData
    {
        public string name;
        public string hierarchyPath;
        public string category; // platform | enemy | collectible | trap | decoration | fixed
        public bool isFixed;
        public BoundsData bounds;
        public TransformData currentTransform;
        public string colliderType;
        public int unityInstanceID; // 便于应用时快速查找
    }

    [System.Serializable]
    public class BoundsData
    {
        public float sizeX;
        public float sizeY;
        public float sizeZ;
    }

    [System.Serializable]
    public class TransformData
    {
        public Vec3 position;
        public Vec3 rotation; // euler
        public Vec3 scale;    // localScale
    }

    [System.Serializable]
    public class Vec3
    {
        public float x;
        public float y;
        public float z;
    }
}
