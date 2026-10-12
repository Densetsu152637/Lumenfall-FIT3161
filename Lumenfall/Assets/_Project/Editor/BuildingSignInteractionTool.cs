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
                (string objectName, string displayName)[] signs =
                {
                    ("Sign-Training Centre", "Training Centre"),
                    ("Sign-Arcane Workshop", "Arcane Workshop"),
                    ("Sign-Main Academy Building", "Main Academy Building"),
                    ("Sign-Library", "Library")
                };
                foreach ((string objectName, string displayName) signDefinition in signs)
                {
                    Transform sign = FindByName(root.transform, signDefinition.objectName);
                    if (sign == null) throw new System.InvalidOperationException("Could not find " + signDefinition.objectName + " in campus prefab.");

                    BuildingSignInteraction interaction = sign.GetComponent<BuildingSignInteraction>();
                    if (interaction == null)
                        interaction = Undo.AddComponent<BuildingSignInteraction>(sign.gameObject);

                    SerializedObject serialized = new SerializedObject(interaction);
                    serialized.FindProperty("buildingName").stringValue = signDefinition.displayName;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, CampusPrefabPath);
                Debug.Log("Updated the four named campus building signs.");
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
