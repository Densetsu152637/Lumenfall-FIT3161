using Lumenfall.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    /// <summary>
    /// Primitives built at runtime (secret rooms, puzzle rooms) must not rely on the editor-only default material, or they
    /// are invisible in player builds.
    /// </summary>
    public sealed class RuntimePrimitiveTests
    {
        [Test] public void LitMaterialIsAUrpLitResourceWithEmission()
        {
            Material material = RuntimePrimitive.LitMaterial;
            Assert.That(material, Is.Not.Null, "Resources/RuntimeLit.mat is missing.");
            Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            // Parts switch emission on at runtime; the build only keeps that variant if a shipped material uses it.
            Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True);
        }

        [Test] public void CreatedPrimitivesUseTheBuildSafeMaterial()
        {
            GameObject cube = RuntimePrimitive.Create(PrimitiveType.Cube);
            try
            {
                Assert.That(cube.GetComponent<Renderer>().sharedMaterial, Is.SameAs(RuntimePrimitive.LitMaterial));
            }
            finally { Object.DestroyImmediate(cube); }
        }
    }
}
