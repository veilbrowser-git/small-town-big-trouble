using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SmallTown.EditorTools
{
    /// <summary>Renders an overview picture of the movement gym to Docs/, for the handoff notes.</summary>
    public static class ScenePreview
    {
        [MenuItem("Small Town/Setup/Capture Town Previews")]
        public static void CaptureTown()
        {
            EditorSceneManager.OpenScene(TownSceneBuilder.ScenePath, OpenSceneMode.Single);
            Shoot("Docs/town-unity-overview.png", new Vector3(6f, 40f, -56f), new Vector3(0f, 0f, -2f), 45f);
            Shoot("Docs/town-unity-street.png", new Vector3(-12f, 1.1f, -1.0f), new Vector3(0f, 1.0f, 2.2f), 70f);
            Shoot("Docs/town-unity-store.png", new Vector3(-8.2f, 1.2f, 5.6f), new Vector3(-3.0f, 0.7f, 7.6f), 75f);
            Shoot("Docs/town-unity-pizza.png", new Vector3(-14.75f, 1.25f, 6.6f), new Vector3(-14.9f, 0.7f, 11f), 80f);
            Debug.Log("[SmallTown] Wrote town previews to Docs/");
        }

        static void Shoot(string path, Vector3 eye, Vector3 target, float fieldOfView)
        {
            var cameraObject = new GameObject("PreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = eye;
            cameraObject.transform.LookAt(target);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.56f, 0.83f, 0.96f);
            var target2 = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            camera.targetTexture = target2;
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = false;
            camera.Render();
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = true;
            camera.Render();
            camera.Render();
            RenderTexture.active = target2;
            var image = new Texture2D(target2.width, target2.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target2.width, target2.height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory("Docs");
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
            Object.DestroyImmediate(target2);
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(cameraObject);
        }

        [MenuItem("Small Town/Setup/Capture Movement Test Overview")]
        public static void CaptureMovementTest()
        {
            EditorSceneManager.OpenScene(MovementTestSceneBuilder.ScenePath, OpenSceneMode.Single);
            var cameraObject = new GameObject("PreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(2f, 15f, -19f);
            cameraObject.transform.LookAt(new Vector3(0.5f, 0f, 0.5f));
            camera.fieldOfView = 55f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.56f, 0.83f, 0.96f);

            var target = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            camera.targetTexture = target;
            // In a headless editor the SRP batcher has not uploaded material data before the
            // first manual render, which flattens every colour. Refresh it and warm up.
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = false;
            camera.Render();
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = true;
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory("Docs");
            File.WriteAllBytes("Docs/movement-test-overview.png", image.EncodeToPNG());

            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(cameraObject);
            Debug.Log("[SmallTown] Wrote Docs/movement-test-overview.png");
        }
    }
}
