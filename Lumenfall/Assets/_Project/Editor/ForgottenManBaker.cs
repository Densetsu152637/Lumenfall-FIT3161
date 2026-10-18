using Lumenfall.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Bakes the forgotten man in the egg room (<see cref="DevChibis.OldMan"/>) into a prefab, so the room places a
    /// finished character instead of recolouring and dressing one at runtime. The base model is recoloured and dressed
    /// exactly as the room would (white hair and beard, spectacles, coat), in a preview scene so the open scene is left
    /// alone; every material is saved beside the prefab, and the prefab is assigned to <see cref="SecretRoomArt"/>. It
    /// runs once by itself when the prefab is missing, and again from <c>Lumenfall/Secrets/Bake Forgotten Man</c>
    /// after the look changes.
    /// </summary>
    public static class ForgottenManBaker
    {
        private const string Folder = "Assets/_Project/Art/Charaters/NPCs/ForgottenMan";
        private const string PrefabPath = Folder + "/ForgottenMan.prefab";

        [InitializeOnLoadMethod]
        private static void BakeOnceIfMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                SecretRoomArt art = SecretRoomArt.Load();
                if (art != null && art.forgottenMan == null) Bake();
            };
        }

        [MenuItem("Lumenfall/Secrets/Bake Forgotten Man")]
        public static void Bake()
        {
            SecretRoomArt art = SecretRoomArt.Load();
            if (art == null || art.devGlasses == null)
            {
                Debug.LogError("SecretRoomArt (with its short-haired base model) is missing, so the forgotten man cannot be baked.");
                return;
            }

            // A fresh folder each bake, so materials from an older look do not linger.
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets/_Project/Art/Charaters/NPCs", "ForgottenMan");

            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject man = null;
            try
            {
                man = DevChibis.Create(art.devGlasses, DevChibis.OldMan, null);
                SceneManager.MoveGameObjectToScene(man, preview);
                man.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                DevChibis.Dress(man, DevChibis.OldMan);
                SaveMaterials(man);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(man, PrefabPath);
                art.forgottenMan = prefab;
                EditorUtility.SetDirty(art);
                AssetDatabase.SaveAssets();
                Debug.Log($"Baked the forgotten man to {PrefabPath} and assigned it to SecretRoomArt.");
            }
            finally
            {
                if (man != null) Object.DestroyImmediate(man);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // The recoloured body and the beard, moustache and other dressing all use materials made in memory; each becomes
        // an asset so the prefab keeps its look in a build.
        private static void SaveMaterials(GameObject man)
        {
            int index = 0;
            foreach (Renderer renderer in man.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || EditorUtility.IsPersistent(materials[i])) continue;
                    string path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/ForgottenMan_{renderer.gameObject.name}_{index++}.mat");
                    AssetDatabase.CreateAsset(materials[i], path);
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }
    }
}
