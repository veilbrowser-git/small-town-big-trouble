using System.Collections.Generic;
using SmallTown.Crime;
using SmallTown.Npc;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace SmallTown.EditorTools
{
    /// <summary>
    /// Turns the Blender character export into Unity assets: bakes each named
    /// part of Panda.fbx into a clean mesh asset (identity transform, so pivots
    /// and axes are exactly as authored), makes palette materials, and builds
    /// NPC prefabs from the parts.
    /// </summary>
    public static class CharacterBuilder
    {
        public const string ModelFolder = ContentBuilder.ArtFolder + "/Models";
        public const string MeshFolder = ContentBuilder.ArtFolder + "/Meshes";
        public const string PandaModel = ModelFolder + "/Panda.fbx";
        public const string NpcFolder = ContentBuilder.PrefabFolder + "/Npc";

        /// <summary>The modelled paws are true to scale; the reference sheet draws them bigger, so they are scaled up.</summary>
        public const float HandScale = 1.45f;

        static readonly Dictionary<string, Mesh> Parts = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material> Palettes = new Dictionary<string, Material>();

        public static void Reset()
        {
            Parts.Clear();
            Palettes.Clear();
        }

        /// <summary>The palette material, or a recoloured variant such as "Officer".</summary>
        public static Material Palette(string variant = "")
        {
            if (Palettes.TryGetValue(variant, out Material cached) && cached != null) return cached;
            string suffix = string.IsNullOrEmpty(variant) ? "" : "_" + variant;
            Material material = ContentBuilder.Mat("Palette" + suffix, Color.white);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelFolder + "/Palette" + suffix + ".png");
            if (texture == null) Debug.LogWarning("[SmallTown] Missing palette texture for '" + variant + "'. Run Tools/blender/build_palettes.py.");
            material.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(material);
            Palettes[variant] = material;
            return material;
        }

        /// <summary>A baked character part by its Blender object name, or null if the model lacks it.</summary>
        public static Mesh Part(string name)
        {
            if (Parts.Count == 0) BakeParts();
            Parts.TryGetValue(name, out Mesh mesh);
            return mesh;
        }

        public static void BakeParts()
        {
            Parts.Clear();
            Parts["_baked"] = null;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PandaModel);
            if (model == null)
            {
                Debug.LogWarning("[SmallTown] " + PandaModel + " not found. Characters fall back to boxes. Run Tools/blender/build_character.py.");
                return;
            }
            ProjectSetup.EnsureFolder(MeshFolder);
            var instance = Object.Instantiate(model);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
            {
                Mesh baked = Bake(filter);
                string path = MeshFolder + "/Panda_" + filter.gameObject.name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null)
                {
                    existing.Clear();
                    EditorUtility.CopySerialized(baked, existing);
                    existing.name = baked.name;
                    EditorUtility.SetDirty(existing);
                    Object.DestroyImmediate(baked);
                    baked = existing;
                }
                else
                {
                    AssetDatabase.CreateAsset(baked, path);
                }
                Parts[filter.gameObject.name] = baked;
                Bounds b = baked.bounds;
                Debug.Log("[SmallTown] Panda part " + filter.gameObject.name + " bounds min " + b.min.ToString("F3") + " max " + b.max.ToString("F3"));
            }
            Object.DestroyImmediate(instance);
        }

        /// <summary>Bakes any model part to a mesh asset with its transform applied, and returns the asset.</summary>
        public static Mesh BakeToAsset(MeshFilter filter, string assetName)
        {
            ProjectSetup.EnsureFolder(MeshFolder);
            Mesh baked = Bake(filter);
            baked.name = assetName;
            string path = MeshFolder + "/" + assetName + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(baked, path);
                return baked;
            }
            existing.Clear();
            EditorUtility.CopySerialized(baked, existing);
            existing.name = assetName;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(baked);
            return existing;
        }

        static Mesh Bake(MeshFilter filter)
        {
            Mesh source = filter.sharedMesh;
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;
            Vector3[] vertices = source.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            int[] triangles = source.triangles;
            if (matrix.determinant < 0f)
            {
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    (triangles[i], triangles[i + 1]) = (triangles[i + 1], triangles[i]);
                }
            }
            var mesh = new Mesh { name = "Panda_" + filter.gameObject.name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, source.uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Creates a child that shows a character part, or a box of the given size if the part is missing.</summary>
        public static Transform PartObject(string partName, Transform parent, Material material, Vector3 fallbackSize, string objectName = null)
        {
            Mesh mesh = Part(partName);
            GameObject go;
            if (mesh != null)
            {
                go = new GameObject(objectName ?? partName);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = objectName ?? partName;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
                go.transform.localScale = fallbackSize;
            }
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>Builds the visual body shared by every NPC and returns its root (not yet saved as a prefab).</summary>
        public static GameObject BuildNpcBody(string name, string paletteVariant, string hatPart)
        {
            Material material = Palette(paletteVariant);
            var root = new GameObject(name) { layer = GameLayers.Npc };

            Transform head = PartObject("Head", root.transform, material, new Vector3(0.32f, 0.3f, 0.3f));
            if (!string.IsNullOrEmpty(hatPart) && Part(hatPart) != null)
            {
                // Hats always use the true palette so a police cap stays navy on a recoloured head.
                PartObject(hatPart, head, Palette(), Vector3.one * 0.1f);
            }
            Transform torso = PartObject("Torso", root.transform, material, new Vector3(0.4f, 0.44f, 0.24f));
            var upper = new Transform[2];
            var lower = new Transform[2];
            var hands = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                upper[i] = PartObject("UpperArm", root.transform, material, Vector3.one, side + "UpperArm");
                lower[i] = PartObject("Forearm", root.transform, material, Vector3.one, side + "Forearm");
                hands[i] = PartObject(i == 0 ? "HandL" : "HandR", root.transform, material, new Vector3(0.11f, 0.05f, 0.14f), side + "Hand");
                if (Part(i == 0 ? "HandL" : "HandR") != null) hands[i].localScale = Vector3.one * HandScale;
            }

            var body = root.AddComponent<NpcBody>();
            body.Configure(head, torso, upper, lower, hands);
            body.Pose(0f);

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.55f, 0f);
            capsule.radius = 0.24f;
            capsule.height = 1.1f;
            return root;
        }

        static NavMeshAgent AddAgent(GameObject root, float speed)
        {
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.22f;
            agent.height = 1f;
            agent.speed = speed;
            agent.angularSpeed = 420f;
            agent.acceleration = 18f;
            agent.stoppingDistance = 0.35f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            return agent;
        }

        static GameObject Save(GameObject root, string name)
        {
            ProjectSetup.EnsureFolder(NpcFolder);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, NpcFolder + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        public static GameObject BuildOfficerPrefab()
        {
            GameObject root = BuildNpcBody("Officer", "Officer", "PoliceCap");
            AddAgent(root, 2.4f);
            root.AddComponent<PoliceOfficer>().Configure(root.GetComponent<NpcBody>());
            return Save(root, "Officer");
        }

        public static GameObject BuildPedestrianPrefab(string variant)
        {
            GameObject root = BuildNpcBody("Pedestrian_" + variant, variant, null);
            AddAgent(root, 1.1f);
            root.AddComponent<Pedestrian>().Configure(root.GetComponent<NpcBody>(), new Transform[0]);
            return Save(root, "Pedestrian_" + variant);
        }

        public static GameObject BuildStaticNpcPrefab(string name, string variant, string hat)
        {
            return Save(BuildNpcBody(name, variant, hat), name);
        }
    }
}
