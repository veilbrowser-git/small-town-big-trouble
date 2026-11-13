using UnityEditor;
using UnityEngine;

namespace SmallTown.EditorTools
{
    /// <summary>
    /// Import rules for everything exported from Blender into Art/Models:
    /// hard-edged low-poly meshes with no embedded materials, and palette
    /// atlases that must never be filtered or compressed (each swatch is a
    /// flat colour sampled at its centre).
    /// </summary>
    public sealed class ArtImportSettings : AssetPostprocessor
    {
        const string Folder = "Assets/SmallTown/Art/Models/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.isReadable = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.importNormals = ModelImporterNormals.Calculate;
            importer.normalSmoothingAngle = 0f;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = false;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
        }
    }
}
