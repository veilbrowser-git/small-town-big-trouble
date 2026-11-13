using SmallTown.Interaction;
using SmallTown.Locomotion;
using SmallTown.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SmallTown.EditorTools
{
    /// <summary>Generates the menu: a small room with a board of slap buttons for solo, host and join.</summary>
    public static class MenuSceneBuilder
    {
        public const string ScenePath = ContentBuilder.SceneFolder + "/Menu.unity";

        [MenuItem("Small Town/Setup/Rebuild Menu Scene")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MovementTestSceneBuilder.BuildLighting();

            Material floor = ContentBuilder.Mat("GymGround", ContentBuilder.Hex("7EC8A0"));
            Material wall = ContentBuilder.Mat("GymWall", ContentBuilder.Hex("F2E3C6"));
            Material sign = ContentBuilder.Mat("GymSign", ContentBuilder.Hex("33475B"));
            Material dark = ContentBuilder.Mat("GymDark", ContentBuilder.Hex("2B2B33"));

            var world = new GameObject("World");
            ContentBuilder.Box("Floor", world.transform, new Vector3(0f, -0.5f, 0f), new Vector3(14f, 1f, 14f), floor);
            ContentBuilder.Box("WallN", world.transform, new Vector3(0f, 1.2f, 7f), new Vector3(14f, 2.4f, 0.4f), wall);
            ContentBuilder.Box("WallS", world.transform, new Vector3(0f, 1.2f, -7f), new Vector3(14f, 2.4f, 0.4f), wall);
            ContentBuilder.Box("WallE", world.transform, new Vector3(7f, 1.2f, 0f), new Vector3(0.4f, 2.4f, 14f), wall);
            ContentBuilder.Box("WallW", world.transform, new Vector3(-7f, 1.2f, 0f), new Vector3(0.4f, 2.4f, 14f), wall);

            var spawn = new GameObject("SpawnPoint");
            spawn.AddComponent<PlayerSpawnPoint>();
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ContentBuilder.PlayerRigPath));
            rig.transform.position = Vector3.zero;

            // --- The board, 2.6 m ahead of the player.
            var board = new GameObject("MenuBoard");
            board.transform.position = new Vector3(0f, 0f, 2.6f);
            ContentBuilder.Box("Panel", board.transform, new Vector3(0f, 1.0f, 0.06f), new Vector3(3.6f, 1.5f, 0.08f), sign);
            ContentBuilder.Box("LegA", board.transform, new Vector3(-1.6f, 0.15f, 0.06f), new Vector3(0.1f, 0.3f, 0.1f), dark);
            ContentBuilder.Box("LegB", board.transform, new Vector3(1.6f, 0.15f, 0.06f), new Vector3(0.1f, 0.3f, 0.1f), dark);
            ContentBuilder.Label("<b>SMALL TOWN, BIG TROUBLE</b>", board.transform, new Vector3(0f, 1.6f, 0f), new Vector2(3.4f, 0.24f), 2.0f, ContentBuilder.Hex("FFD166"));
            TextMeshPro status = ContentBuilder.Label("", board.transform, new Vector3(0f, 1.36f, 0f), new Vector2(3.4f, 0.18f), 0.85f, ContentBuilder.Hex("9FE2BF"));

            var controller = board.AddComponent<MenuController>();

            // Left: the three ways in.
            PunchButton solo = MovementTestSceneBuilder.Button(board.transform, "SOLO", new Vector3(-1.25f, 1.02f, -0.02f), ContentBuilder.Mat("ButtonGreen", ContentBuilder.Hex("3FB56F")));
            PunchButton host = MovementTestSceneBuilder.Button(board.transform, "HOST", new Vector3(-1.25f, 0.66f, -0.02f), ContentBuilder.Mat("ButtonBlue", ContentBuilder.Hex("3F6FB5")));
            PunchButton join = MovementTestSceneBuilder.Button(board.transform, "JOIN", new Vector3(-1.25f, 0.30f, -0.02f), ContentBuilder.Mat("ButtonRed", ContentBuilder.Hex("E0453A")));
            UnityEventTools.AddPersistentListener(solo.onPressed, controller.PlaySolo);
            UnityEventTools.AddPersistentListener(host.onPressed, controller.Host);
            UnityEventTools.AddPersistentListener(join.onPressed, controller.Join);
            ContentBuilder.Label("Play alone", board.transform, new Vector3(-0.72f, 1.02f, 0f), new Vector2(0.6f, 0.2f), 0.6f, Color.white, TextAlignmentOptions.Left);
            ContentBuilder.Label("Friends join you", board.transform, new Vector3(-0.72f, 0.66f, 0f), new Vector2(0.6f, 0.2f), 0.6f, Color.white, TextAlignmentOptions.Left);
            ContentBuilder.Label("Join the address\non the right", board.transform, new Vector3(-0.72f, 0.30f, 0f), new Vector2(0.6f, 0.2f), 0.6f, Color.white, TextAlignmentOptions.Left);

            // Right: the address and its keypad.
            ContentBuilder.Label("Host's address", board.transform, new Vector3(0.95f, 1.18f, 0f), new Vector2(1.5f, 0.12f), 0.6f, Color.white);
            TextMeshPro address = ContentBuilder.Label("...", board.transform, new Vector3(0.95f, 1.04f, 0f), new Vector2(1.5f, 0.16f), 1.1f, ContentBuilder.Hex("FFD166"));
            controller.Configure(address, status);

            Material key = ContentBuilder.Mat("ButtonKey", ContentBuilder.Hex("5A6470"));
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "<" };
            for (int i = 0; i < keys.Length; i++)
            {
                int column = i % 3;
                int row = i / 3;
                PunchButton button = KeyButton(board.transform, keys[i], new Vector3(0.6f + column * 0.35f, 0.84f - row * 0.2f, -0.02f), key);
                if (keys[i] == "<") UnityEventTools.AddPersistentListener(button.onPressed, controller.Backspace);
                else UnityEventTools.AddStringPersistentListener(button.onPressed, controller.Type, keys[i]);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static PunchButton KeyButton(Transform parent, string text, Vector3 localPosition, Material material)
        {
            PunchButton button = MovementTestSceneBuilder.Button(parent, text, localPosition, material);
            // Keys are smaller than the big action buttons.
            button.transform.localScale = new Vector3(0.62f, 0.62f, 1f);
            button.cooldown = 0.25f;
            return button;
        }
    }
}
