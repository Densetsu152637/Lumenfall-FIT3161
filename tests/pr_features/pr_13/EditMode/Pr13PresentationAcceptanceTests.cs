using System.Linq;
using System.Reflection;
using Lumenfall.DataDriven;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Lumenfall.FeatureAcceptance
{
    [Category("PR13")]
    public sealed class Pr13PresentationAcceptanceTests
    {
        [Test] public void PrincipalIdentitiesRemainDistinguishableWithoutColorAndCinnaAliasesShareOneIdentity()
        {
            string[] ids = { "player", "stella", "cinna_cirra", "orio", "katrina", "receptionist" };
            var identities = ids.Select(id => { Assert.That(WorldPresentationCatalog.TryCharacter(id, out var value), Is.True); return value; }).ToArray();
            Assert.That(identities.Select(i => i.Symbol).Distinct().Count(), Is.EqualTo(ids.Length));
            Assert.That(identities.Select(i => i.Shape).Distinct().Count(), Is.EqualTo(ids.Length));
            WorldPresentationCatalog.TryCharacter("cinna_cirra_trial", out var trial);
            WorldPresentationCatalog.TryCharacter("cinna_cirra_orientation", out var orientation);
            Assert.That(trial, Is.SameAs(orientation));
            Assert.That(WorldPresentationCatalog.TryCharacter("unknown", out var unknown), Is.False);
            Assert.That(unknown, Is.Null);
        }
        [Test] public void ReconfiguringLabelReleasesOldBadgeAndClearingReleasesCurrentBadge()
        {
            var host = new GameObject("PR13 badge fixture"); Texture2D first = null, second = null;
            try
            {
                var label = host.AddComponent<WorldPresentationLabel>();
                var badge = typeof(WorldPresentationLabel).GetField("badge", BindingFlags.NonPublic | BindingFlags.Instance);
                WorldPresentationCatalog.TryCharacter("stella", out var stella);
                WorldPresentationCatalog.TryCharacter("orio", out var orio);
                label.Configure("Stella", stella.AccentRgb, Vector3.up, stella); first = (Texture2D)badge.GetValue(label);
                Assert.That(first.GetPixels().Any(c => c.a == 0f), Is.True);
                Assert.That(first.GetPixels().Any(c => c.a > 0f), Is.True);
                label.Configure("Orio", orio.AccentRgb, Vector3.up, orio); second = (Texture2D)badge.GetValue(label);
                Assert.That(first == null, Is.True, "The replaced badge must release its native texture.");
                Assert.That(second != null, Is.True);
                label.Configure(null, 0, Vector3.zero);
                Assert.That(second == null, Is.True); Assert.That(label.Text, Is.Empty);
            }
            finally { Object.DestroyImmediate(host); }
        }
        [TestCase("campus")][TestCase("westForest")][TestCase("B01.Ground")][TestCase("B02.Ground")][TestCase("B03.Ground")][TestCase("B04.Ground")]
        public void AuthoredAreaPrefabHasUsableMeshMaterialShaderAndTextureReferences(string id)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/World/Areas/Prefabs/" + id + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0));
            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    Assert.That(material, Is.Not.Null, renderer.name);
                    Assert.That(material.shader, Is.Not.Null, material.name);
                    var serialized = new SerializedObject(material); var property = serialized.GetIterator();
                    while (property.NextVisible(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceInstanceIDValue != 0)
                            Assert.That(property.objectReferenceValue, Is.Not.Null, material.name + ":" + property.propertyPath);
                }
                if (renderer is MeshRenderer) Assert.That(renderer.GetComponent<MeshFilter>()?.sharedMesh, Is.Not.Null, renderer.name);
                if (renderer is SkinnedMeshRenderer skinned) Assert.That(skinned.sharedMesh, Is.Not.Null, renderer.name);
            }
        }
    }
}
