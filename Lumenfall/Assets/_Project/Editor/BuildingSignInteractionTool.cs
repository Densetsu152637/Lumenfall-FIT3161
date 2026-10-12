using UnityEditor;
using UnityEngine;
using Lumenfall.DataDriven;

namespace Lumenfall.EditorTools
{
    public static class BuildingSignInteractionTool
    {
        private const string CampusPrefabPath = "Assets/_Project/World/Areas/Prefabs/campus.prefab";

        public static void Apply()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CampusPrefabPath);
            try
            {
                string[] signNames = { "Sign-B01", "Sign-B02", "Sign-B03", "Sign-B04" };
                foreach (string signName in signNames)
                {
                    Transform sign = FindByName(root.transform, signName);
                    if (sign == null) throw new System.InvalidOperationException("Could not find " + signName + " in campus prefab.");

                    BuildingSignInteraction interaction = sign.GetComponent<BuildingSignInteraction>();
                    if (interaction == null)
                        interaction = Undo.AddComponent<BuildingSignInteraction>(sign.gameObject);

                    SerializedObject serialized = new SerializedObject(interaction);
                    serialized.FindProperty("buildingName").stringValue = "Building " + signName.Substring("Sign-".Length);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, CampusPrefabPath);
                Debug.Log("Added E-key building sign interactions to Sign-B01 through Sign-B04.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform result = FindByName(child, name);
                if (result != null) return result;
            }
            return null;
        }
    }
}
