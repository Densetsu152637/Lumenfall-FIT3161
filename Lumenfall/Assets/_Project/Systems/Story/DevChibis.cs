using System;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The three developers in the devs' room (<see cref="SecretRooms.DevRoomId"/>), styled after the team on the same
    /// chibi rig as every other NPC. Each starts from a background NPC model (<see cref="SecretRoomArt"/>), recoloured
    /// (skin, hair, outfit, glasses), with props it does not need hidden. What the rig has no mesh for (a beard, curls, a
    /// shirt collar, jacket trim) is added from primitives on the head and neck bones, fitted to the live model when the
    /// room is placed (<see cref="Dress"/>), so it moves with the head.
    /// </summary>
    public static class DevChibis
    {
        public sealed class Look
        {
            public string Id;
            public string Name;
            public string DialogueId;
            public Color Skin;
            public Color Hair;
            public Color Outfit;
            public Color Shoes = Hex("#202022");
            public Color Eyes = Hex("#2E2018");
            public Color? Frames;
            public Color FacialHair;
            public bool ShortBeard;
            public bool FullBeard;
            public bool Curls;
            public bool WhiteCollar;
            public bool LimeTrim;
            /// <summary>Parts of the base model that are switched off, such as a satchel or a book.</summary>
            public string[] Hide = Array.Empty<string>();
        }

        /// <summary>Swept brown hair, a short beard, a black zip-up jacket with lime-green trim. Half asleep.</summary>
        public static readonly Look Nick = new Look
        {
            Id = "secret_dev", Name = "Nick", DialogueId = "secret_dev",
            Skin = Hex("#F0CBB2"), Hair = Hex("#5E3E26"), Outfit = Hex("#17171A"), FacialHair = Hex("#4E3320"),
            ShortBeard = true, LimeTrim = true, Hide = new[] { "SatchelBag", "SatchelStrap" }
        };

        /// <summary>Short black hair, thin dark glasses, a light beige top.</summary>
        public static readonly Look Glasses = new Look
        {
            Id = "secret_dev_glasses", Name = "Brian", DialogueId = "secret_dev_glasses",
            Skin = Hex("#F3D2BC"), Hair = Hex("#141214"), Outfit = Hex("#D6C8B0"), Frames = Hex("#26262C"), Hide = new[] { "Book" }
        };

        /// <summary>Big curly black hair, a full beard and moustache, a navy jumper over a white collar.</summary>
        public static readonly Look Curls = new Look
        {
            Id = "secret_dev_curls", Name = "Miki", DialogueId = "secret_dev_curls",
            Skin = Hex("#8E5E43"), Hair = Hex("#121012"), Outfit = Hex("#1F2848"), FacialHair = Hex("#121012"),
            FullBeard = true, Curls = true, WhiteCollar = true, Hide = new[] { "SatchelBag", "SatchelStrap" }
        };

        public static readonly Look[] All = { Nick, Glasses, Curls };

        private const string DressingName = "Dev Dressing";
        private static readonly Color Lime = Hex("#8CDB33");
        private static readonly Color White = Hex("#F2F2EC");

        /// <summary>The model, recoloured, under <paramref name="parent"/>; null if the base model is missing.</summary>
        public static GameObject Create(GameObject prefab, Look look, Transform parent)
        {
            if (prefab == null) return null;
            GameObject model = UnityEngine.Object.Instantiate(prefab, parent);
            model.name = look.Name;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (Array.IndexOf(look.Hide, renderer.gameObject.name) >= 0) { renderer.enabled = false; continue; }
                Material[] materials = renderer.materials;
                foreach (Material material in materials)
                {
                    Color? colour = ColourFor(material.name, look);
                    if (!colour.HasValue) continue;
                    material.SetColor("_BaseColor", colour.Value);
                    material.SetColor("_Color", colour.Value);
                }
                renderer.materials = materials;
            }
            return model;
        }

        // The variant materials are named after their role, such as Student_02_Hair.
        private static Color? ColourFor(string materialName, Look look)
        {
            if (materialName.Contains("_Skin")) return look.Skin;
            if (materialName.Contains("_Hair")) return look.Hair;
            if (materialName.Contains("_Outfit")) return look.Outfit;
            if (materialName.Contains("_Shoes")) return look.Shoes;
            if (materialName.Contains("_Eyes")) return look.Eyes;
            if (materialName.Contains("_Glasses")) return look.Frames;
            return null;
        }

        /// <summary>
        /// Adds what the rig has no mesh for, sized from the live model's hair, eyes and outfit, and parented to its head
        /// and neck bones. Called each time the room is placed; it only dresses a model once.
        /// </summary>
        public static void Dress(GameObject model, Look look)
        {
            if (model == null || model.transform.Find(DressingName) != null) return;
            Transform root = model.transform;
            new GameObject(DressingName).transform.SetParent(root, false);
            Transform head = FindDeep(root, "Head") ?? root;
            Transform neck = FindDeep(root, "Spine2") ?? FindDeep(root, "Neck") ?? root;
            if (!TryLocalBounds(root, "Hair", out Bounds hair) || !TryLocalBounds(root, "SM_Chibi_Eye", out Bounds eyes)) return;
            TryLocalBounds(root, "Outfit", out Bounds outfit);
            float w = hair.size.x;
            Vector3 eye = eyes.center;
            float face = eyes.max.z;

            // Facial hair hugs the jaw: centred inside the head and just breaking its surface at the chin.
            if (look.ShortBeard)
                Add(head, root, PrimitiveType.Sphere, "Beard", new Vector3(eye.x, eye.y - 0.23f * w, face - 0.11f * w), new Vector3(0.5f * w, 0.15f * w, 0.32f * w), look.FacialHair);
            if (look.FullBeard)
            {
                Add(head, root, PrimitiveType.Sphere, "Beard", new Vector3(eye.x, eye.y - 0.24f * w, face - 0.1f * w), new Vector3(0.56f * w, 0.21f * w, 0.34f * w), look.FacialHair);
                Add(head, root, PrimitiveType.Cube, "Moustache", new Vector3(eye.x, eye.y - 0.15f * w, face - 0.01f * w), new Vector3(0.2f * w, 0.04f * w, 0.05f * w), look.FacialHair);
            }
            if (look.Curls)
            {
                // Small puffs spread over the top and sides of the hair (an even spiral over a sphere), leaving the face clear.
                Vector3 extents = hair.extents * 0.95f;
                const int count = 90;
                for (int i = 0; i < count; i++)
                {
                    float y = 1f - 2f * (i + 0.5f) / count;
                    float ring = Mathf.Sqrt(1f - y * y);
                    float angle = i * 2.39996f;
                    Vector3 direction = new Vector3(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring);
                    if (direction.y < -0.15f || (direction.z > 0.35f && direction.y < 0.55f)) continue;
                    Vector3 puff = hair.center + Vector3.Scale(direction, extents);
                    Add(head, root, PrimitiveType.Sphere, "Curl", puff, Vector3.one * (0.17f + 0.03f * (i % 3)) * w, look.Hair);
                }
            }
            if (outfit.size == Vector3.zero) return;
            float collarY = outfit.max.y - 0.03f * w;
            if (look.WhiteCollar)
                foreach (float side in new[] { -1f, 1f })
                    Add(neck, root, PrimitiveType.Cube, "Collar", new Vector3(outfit.center.x + side * 0.08f * w, collarY, outfit.max.z - 0.05f * w),
                        new Vector3(0.15f * w, 0.06f * w, 0.06f * w), White, Quaternion.Euler(0f, 0f, side * -25f));
            if (look.LimeTrim)
            {
                foreach (float side in new[] { -1f, 1f })
                    Add(neck, root, PrimitiveType.Cube, "Trim", new Vector3(outfit.center.x + side * 0.035f * w, outfit.center.y + 0.05f * outfit.size.y, outfit.max.z - 0.01f * w),
                        new Vector3(0.03f * w, 0.6f * outfit.size.y, 0.02f * w), Lime, emission: 0.3f);
                Add(neck, root, PrimitiveType.Cylinder, "Trim Collar", new Vector3(outfit.center.x, collarY, outfit.center.z),
                    new Vector3(0.62f * outfit.size.x, 0.02f * w, 0.62f * outfit.size.z), Lime, emission: 0.3f);
            }
        }

        // Built in the model's space, then handed to the bone so it follows the animation.
        private static void Add(Transform bone, Transform root, PrimitiveType type, string name, Vector3 localPosition, Vector3 localScale, Color colour,
            Quaternion? rotation = null, float emission = 0.04f)
        {
            GameObject part = SecretRooms.Part(type, name, root, localPosition, localScale, colour, emission, rotation);
            part.transform.SetParent(bone, true);
        }

        // A renderer's world bounds, in the model's own space (the model only ever turns about its up axis).
        private static bool TryLocalBounds(Transform root, string rendererName, out Bounds bounds)
        {
            bounds = default;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.name != rendererName || renderer is ParticleSystemRenderer) continue;
                Bounds world = renderer.bounds;
                bool any = false;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = world.center + Vector3.Scale(world.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = root.InverseTransformPoint(corner);
                    if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                    else bounds.Encapsulate(local);
                }
                return bounds.size.sqrMagnitude > 0f;
            }
            return false;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) if (child.name == name) return child;
            return null;
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color colour) ? colour : Color.magenta;
    }
}
