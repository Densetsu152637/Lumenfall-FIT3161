using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Builds background NPC prefabs (students, teachers and receptionists) on the cast's shared SM_Chibi rig. Each
    /// NPC mixes the cast's hair and outfit meshes, pieces of the Chibi Witch (bob, hat, and robe transferred onto the
    /// chibi skeleton), generated props and its own materials. Regenerating overwrites the same asset paths, so
    /// existing GUIDs and scene references are kept.
    /// </summary>
    public static class NpcVariantGenerator
    {
        private const string NpcFolder = "Assets/_Project/Art/Charaters/NPCs/";
        private const string OutputFolder = NpcFolder + "Variants/";
        private const string MaterialFolder = OutputFolder + "Materials/";
        private const string MeshFolder = OutputFolder + "Meshes/";
        private const string BaseModel = NpcFolder + "Cinna Cirra.fbx";
        private const string WitchPrefab = NpcFolder + "ChibiWitch_Customizable_Neutral.prefab";
        private const string WitchMaterial = "Assets/ThirdParty/StylizedChibiWitch/Materials/Mat_Main_Custom.mat";
        private const string ChibiController = "Assets/_Project/Art/Charaters/Animations/NPCs/NPC_Chibi.controller";
        private const string StaffPrefab = "Assets/ThirdParty/Wand - Blink/Art/Weapons/Stylized/Staves/_PrefabsStaves/Staff4_1_1.prefab";
        // The staff's own pack materials are not in the project, so every staff surface uses this URP wood material.
        private const string StaffMaterial = MaterialFolder + "NPC_Staff.mat";
        private const string PreviewFolder = "Logs/NpcPreviews";
        private const float IdleSampleTime = 1.2f;
        private const int PreviewLayer = 31;
        private const float BobFit = 0.96f;
        // Hat fitting: the brim sits just above the eyes, the crown opening matches the hair, and hair above the brim
        // is tucked inside the crown.
        private const float BrimAboveEyes = 0.05f;
        private const float HatOpeningMargin = 0.92f;
        private const float BrimScale = 0.78f;
        private const float CrownHeight = 0.62f;
        private const float TuckMargin = 0.9f;
        private const float StaffLean = 26f;

        private enum Hair { Long, Short, Bob }
        private enum Outfit { TeeRound, TeeCropped, TeeLoose, WitchRobe }

        [Flags]
        private enum Props
        {
            None = 0, Glasses = 1, Satchel = 2, Scarf = 4, Badge = 8, Book = 16,
            Clipboard = 32, Staff = 64, WitchHat = 128, Shoes = 256
        }

        private sealed class Recipe
        {
            public string Name;
            public float Scale = 1f;
            public Hair Hair;
            public Outfit Outfit;
            public Props Props;
            public Color Skin, Eyes, HairColour, Clothes;
            public Color Accent = Hex("#8A5A36");
            public Color Trim = Hex("#D8B45A");
            public Color Shoes = Hex("#3A2E2A");
            public Color Frames = Hex("#2A2A30");
            // Witch robe only: the skirt panel colour (defaults to Clothes); Shoes colours the boots.
            public Color? Skirt;
        }

        // Palettes are chosen so roles read at a glance: students wear bright tees and satchels, teachers wear deep
        // robe colours with witch hats, and receptionists share a navy uniform with a gold scarf and name badge.
        private static readonly Recipe[] Recipes =
        {
            new Recipe { Name = "Student_01", Scale = 0.96f, Hair = Hair.Long, Outfit = Outfit.TeeCropped,
                Props = Props.Satchel, Skin = Hex("#F2C9AE"), Eyes = Hex("#4A2E1E"), HairColour = Hex("#7A4A2A"),
                Clothes = Hex("#7FC8A9"), Accent = Hex("#B07A48"), Trim = Hex("#6B4527") },
            new Recipe { Name = "Student_02", Scale = 0.98f, Hair = Hair.Short, Outfit = Outfit.TeeLoose,
                Props = Props.Satchel | Props.Shoes, Skin = Hex("#C98E6A"), Eyes = Hex("#2D3E2A"), HairColour = Hex("#C4642D"),
                Clothes = Hex("#E2B33C"), Accent = Hex("#3E4C6E"), Trim = Hex("#2A3248"), Shoes = Hex("#F0EDE6") },
            new Recipe { Name = "Student_03", Scale = 0.95f, Hair = Hair.Bob, Outfit = Outfit.TeeCropped,
                Props = Props.Book | Props.Shoes, Skin = Hex("#7A4B32"), Eyes = Hex("#24160F"), HairColour = Hex("#1E1A22"),
                Clothes = Hex("#B9A3E3"), Accent = Hex("#B5373D"), Shoes = Hex("#F4F1EA") },
            new Recipe { Name = "Student_04", Scale = 0.97f, Hair = Hair.Short, Outfit = Outfit.TeeRound,
                Props = Props.Glasses | Props.Book, Skin = Hex("#F6D7C3"), Eyes = Hex("#3B5A7A"), HairColour = Hex("#E8DFC8"),
                Clothes = Hex("#3F6B4A"), Accent = Hex("#2F5D8A"), Frames = Hex("#1E1E24") },
            new Recipe { Name = "Student_05", Scale = 0.96f, Hair = Hair.Long, Outfit = Outfit.TeeRound,
                Props = Props.Glasses | Props.Satchel | Props.Shoes, Skin = Hex("#B7835F"), Eyes = Hex("#1F1A17"),
                HairColour = Hex("#1C1C24"), Clothes = Hex("#8EC5E8"), Accent = Hex("#5E4B8B"), Trim = Hex("#3B2F5C"),
                Frames = Hex("#C9A44A"), Shoes = Hex("#5A3A2A") },
            new Recipe { Name = "Student_06", Scale = 0.95f, Hair = Hair.Bob, Outfit = Outfit.TeeLoose,
                Props = Props.Satchel, Skin = Hex("#F3CDB6"), Eyes = Hex("#4C6B3A"), HairColour = Hex("#E59A72"),
                Clothes = Hex("#F2E8D5"), Accent = Hex("#4F7A55"), Trim = Hex("#34513A") },
            new Recipe { Name = "Teacher_01", Scale = 1.12f, Hair = Hair.Short, Outfit = Outfit.TeeLoose,
                Props = Props.WitchHat | Props.Staff | Props.Glasses | Props.Shoes, Skin = Hex("#D9A88A"), Eyes = Hex("#2E3A4A"),
                HairColour = Hex("#B9B9C0"), Clothes = Hex("#283A6B"), Accent = Hex("#22305A"), Trim = Hex("#D8B45A"),
                Frames = Hex("#C9A44A"), Shoes = Hex("#2A2422") },
            new Recipe { Name = "Teacher_02", Scale = 1.1f, Hair = Hair.Long, Outfit = Outfit.TeeRound,
                Props = Props.WitchHat | Props.Book | Props.Shoes, Skin = Hex("#6E4430"), Eyes = Hex("#1C120D"),
                HairColour = Hex("#E9E6EF"), Clothes = Hex("#5B2E5E"), Accent = Hex("#4A2450"), Trim = Hex("#E0C27A"),
                Shoes = Hex("#2A2422") },
            new Recipe { Name = "Teacher_03", Scale = 1.12f, Hair = Hair.Bob, Outfit = Outfit.WitchRobe,
                Props = Props.WitchHat | Props.Staff, Skin = Hex("#F1CDB4"), Eyes = Hex("#3A2A20"), HairColour = Hex("#9C3D24"),
                Clothes = Hex("#2F5A45"), Skirt = Hex("#1F3B2E"), Accent = Hex("#2F5A45"), Trim = Hex("#D8B45A"), Shoes = Hex("#4A3226") },
            new Recipe { Name = "Receptionist_01", Scale = 1.04f, Hair = Hair.Bob, Outfit = Outfit.TeeRound,
                Props = Props.Scarf | Props.Badge | Props.Glasses | Props.Book | Props.Shoes, Skin = Hex("#F0C7A8"),
                Eyes = Hex("#5A3A22"), HairColour = Hex("#6B3F24"), Clothes = Hex("#2C3E66"), Accent = Hex("#1F3A5F"),
                Trim = Hex("#D8B45A"), Frames = Hex("#3A2A20"), Shoes = Hex("#1E1E22") },
            new Recipe { Name = "Receptionist_02", Scale = 1.04f, Hair = Hair.Short, Outfit = Outfit.TeeRound,
                Props = Props.Scarf | Props.Badge | Props.Clipboard | Props.Shoes, Skin = Hex("#9C6748"), Eyes = Hex("#1D1410"),
                HairColour = Hex("#1A1614"), Clothes = Hex("#2C3E66"), Accent = Hex("#7A5232"), Trim = Hex("#D8B45A"),
                Shoes = Hex("#1E1E22") },
        };

        [MenuItem("Lumenfall/NPCs/Generate Background NPCs")]
        public static void Generate()
        {
            EnsureFolder(OutputFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(MeshFolder);

            InScratchScene(() =>
            {
                using var parts = new PartLibrary();
                SharedProps shared = BuildSharedProps(parts);
                foreach (Recipe recipe in Recipes) BuildChibiNpc(recipe, parts, shared);
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Generated {Recipes.Length} background NPC prefabs in {OutputFolder}.");
        }

        [MenuItem("Lumenfall/NPCs/Render Background NPC Previews")]
        public static void RenderPreviews()
        {
            // The cast are rendered alongside for scale and style comparison.
            string[] prefabs = Recipes.Select(r => OutputFolder + r.Name + ".prefab")
                .Concat(new[] { "Stella", "Cinna Cirra", "Katarina", "Orio" }.Select(n => NpcFolder + n + ".fbx"))
                .ToArray();
            Directory.CreateDirectory(PreviewFolder);

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool batcher = urp != null && urp.useSRPBatcher;
            // Single-frame editor renders can reuse one material's constant buffer across draws with the SRP batcher.
            if (urp != null) urp.useSRPBatcher = false;
            try
            {
                InScratchScene(() => RenderEach(prefabs));
            }
            finally
            {
                if (urp != null) urp.useSRPBatcher = batcher;
            }

            Debug.Log($"Rendered {prefabs.Length} NPC previews to {Path.GetFullPath(PreviewFolder)}.");
        }

        private static void RenderEach(IEnumerable<string> prefabPaths)
        {
            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(40f, 160f, 0f);
            light.cullingMask = 1 << PreviewLayer;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.58f, 0.64f);

            // One fixed framing for every NPC so heights compare directly; the cast models stand slightly below
            // their origin, so the frame starts below the ground plane.
            var camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.86f, 0.88f, 0.92f);
            camera.cullingMask = 1 << PreviewLayer;
            camera.orthographic = true;
            camera.orthographicSize = 1.9f;
            camera.transform.rotation = Quaternion.Euler(10f, 200f, 0f);
            camera.transform.position = new Vector3(0f, 1.42f, 0f) - camera.transform.forward * 12f;
            var target = new RenderTexture(560, 720, 24) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.aspect = 560f / 720f;

            foreach (string path in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PreviewLayer;
                PoseIdle(instance);
                camera.Render();
                SaveRender(target, Path.Combine(PreviewFolder, Path.GetFileNameWithoutExtension(path) + ".png"));
                Object.DestroyImmediate(instance);
            }

            Object.DestroyImmediate(light.gameObject);
            Object.DestroyImmediate(camera.gameObject);
            target.Release();
        }

        /// <summary>
        /// Runs work in a temporary additive scene so the generator never instantiates into, or replaces, the scene
        /// the user has open. Preview objects are kept on a dedicated layer so the open scene is not rendered.
        /// </summary>
        private static void InScratchScene(Action work)
        {
            Scene previous = SceneManager.GetActiveScene();
            bool reuseUntitled = string.IsNullOrEmpty(previous.path) && !previous.isDirty;
            Scene scratch = reuseUntitled ? previous : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scratch);
            try
            {
                work();
            }
            finally
            {
                if (!reuseUntitled)
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scratch, true);
                }
            }
        }

        /// <summary>Batch-mode entry point: generate every NPC, then render the previews.</summary>
        public static void GenerateAndRender()
        {
            Generate();
            RenderPreviews();
        }

        private static void BuildChibiNpc(Recipe recipe, PartLibrary parts, SharedProps shared)
        {
            GameObject npc = parts.InstantiateBase();
            npc.name = recipe.Name;
            Animator animator = npc.GetComponentInChildren<Animator>();
            Dictionary<string, Transform> bones = BonesByName(npc);

            // The base model's own hair and shirt are replaced by the recipe's parts.
            foreach (string castPart in new[] { "hair", "clothes" })
            {
                Transform existing = npc.transform.Find(castPart);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
            }

            var body = npc.transform.Find("SM_Chibi_Body").GetComponent<SkinnedMeshRenderer>();
            var eyes = npc.transform.Find("SM_Chibi_Eye").GetComponent<SkinnedMeshRenderer>();
            body.sharedMaterial = SaveMaterial(recipe.Name, "Skin", body.sharedMaterial, recipe.Skin);
            eyes.sharedMaterial = SaveMaterial(recipe.Name, "Eyes", eyes.sharedMaterial, recipe.Eyes);

            if (recipe.Outfit == Outfit.WitchRobe)
            {
                // The robe carries its own neck, hands, legs and boots, so only the chibi head is kept.
                body.sharedMesh = shared.ChibiHead;
                Material robe = SaveWitchMaterial(recipe.Name, "Robe", mainCloth: recipe.Clothes, details: recipe.Trim, skin: recipe.Skin,
                    additional: recipe.Shoes, skirt: recipe.Skirt ?? recipe.Clothes);
                AddSkinnedPiece(npc, bones, shared.RobeBody, "Robe", robe);
                AddSkinnedPiece(npc, bones, shared.RobeCape, "Capelet", robe);
            }
            else
            {
                PartRef outfit = recipe.Outfit switch
                {
                    Outfit.TeeCropped => PartLibrary.TeeCropped,
                    Outfit.TeeLoose => PartLibrary.TeeLoose,
                    _ => PartLibrary.TeeRound
                };
                parts.AddSkinnedPart(npc, bones, outfit, "Outfit", SaveMaterial(recipe.Name, "Outfit", parts.Material(outfit), recipe.Clothes));
            }

            // Under a hat the hair is swapped for a version tucked inside that hat's crown.
            bool hatted = Has(recipe, Props.WitchHat);
            if (recipe.Hair == Hair.Bob)
            {
                AttachProp(animator, HumanBodyBones.Head, "Hair", hatted ? shared.TuckedHair[Hair.Bob] : shared.WitchBob,
                    SaveWitchMaterial(recipe.Name, "Hair", hair: recipe.HairColour));
            }
            else
            {
                PartRef hair = recipe.Hair == Hair.Long ? PartLibrary.LongHair : PartLibrary.ShortHair;
                parts.AddSkinnedPart(npc, bones, hair, "Hair", SaveMaterial(recipe.Name, "Hair", parts.Material(hair), recipe.HairColour),
                    hatted ? shared.TuckedHair[recipe.Hair] : null);
            }

            if (Has(recipe, Props.Shoes) && recipe.Outfit != Outfit.WitchRobe)
            {
                Material shoes = SaveMaterial(recipe.Name, "Shoes", parts.Material(PartLibrary.LeftShoe), recipe.Shoes);
                parts.AddSkinnedPart(npc, bones, PartLibrary.LeftShoe, "Shoe_L", shoes);
                parts.AddSkinnedPart(npc, bones, PartLibrary.RightShoe, "Shoe_R", shoes);
            }

            if (hatted)
                AttachProp(animator, HumanBodyBones.Head, "WitchHat", shared.Hats[recipe.Hair],
                    SaveWitchMaterial(recipe.Name, "Hat", mainCloth: recipe.Accent, details: recipe.Trim, additional: recipe.Trim));
            if (Has(recipe, Props.Glasses))
                AttachProp(animator, HumanBodyBones.Head, "Glasses", shared.Glasses, SavePlain(recipe.Name, "Glasses", recipe.Frames, 0.6f));
            if (Has(recipe, Props.Scarf))
                AttachProp(animator, shared.ScarfBone, "Scarf", shared.Scarf, SavePlain(recipe.Name, "Scarf", recipe.Trim, 0.25f));
            if (Has(recipe, Props.Badge))
                AttachProp(animator, shared.ChestBone, "Badge", shared.Badge,
                    SavePlain(recipe.Name, "BadgeCard", Hex("#F5F1E6"), 0.3f), SavePlain(recipe.Name, "BadgeStripe", recipe.Trim, 0.5f));
            if (Has(recipe, Props.Satchel))
            {
                Material bag = SavePlain(recipe.Name, "Satchel", recipe.Accent, 0.2f);
                Material strap = SavePlain(recipe.Name, "SatchelStrap", recipe.Trim, 0.2f);
                AttachProp(animator, HumanBodyBones.Hips, "SatchelBag", shared.SatchelBag, bag, strap);
                AttachProp(animator, shared.ChestBone, "SatchelStrap", shared.SatchelStrap, strap);
            }
            if (Has(recipe, Props.Book))
                AttachProp(animator, HumanBodyBones.RightHand, "Book", shared.Book,
                    SavePlain(recipe.Name, "BookCover", recipe.Accent, 0.25f), SavePlain(recipe.Name, "BookPages", Hex("#F3EAD3"), 0.1f));
            if (Has(recipe, Props.Clipboard))
                AttachProp(animator, HumanBodyBones.RightHand, "Clipboard", shared.Clipboard,
                    SavePlain(recipe.Name, "ClipboardBoard", recipe.Accent, 0.3f), SavePlain(recipe.Name, "ClipboardPaper", Hex("#FAF8F2"), 0.1f),
                    SavePlain(recipe.Name, "ClipboardClip", Hex("#B8BCC4"), 0.7f));
            if (Has(recipe, Props.Staff))
                AttachProp(animator, HumanBodyBones.RightHand, "Staff", shared.Staff, shared.StaffMaterials);

            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ChibiController);
            animator.applyRootMotion = false;
            npc.transform.localScale = Vector3.one * recipe.Scale;

            PrefabUtility.SaveAsPrefabAsset(npc, OutputFolder + recipe.Name + ".prefab");
            Object.DestroyImmediate(npc);
        }

        // ---------------------------------------------------------------- shared props

        private sealed class SharedProps
        {
            public Mesh WitchBob, ChibiHead, Glasses, Scarf, Badge, SatchelBag, SatchelStrap, Book, Clipboard, Staff;
            public readonly Dictionary<Hair, Mesh> Hats = new Dictionary<Hair, Mesh>();
            public readonly Dictionary<Hair, Mesh> TuckedHair = new Dictionary<Hair, Mesh>();
            public SkinnedPiece RobeBody, RobeCape;
            public Material[] StaffMaterials;
            public HumanBodyBones ChestBone, ScarfBone;
        }

        /// <summary>A generated skinned mesh plus the chibi bone names its weights refer to, in order.</summary>
        private sealed class SkinnedPiece
        {
            public Mesh Mesh;
            public string[] Bones;
        }

        /// <summary>
        /// Every chibi NPC shares one skeleton, so props are generated once against a reference instance and stored in
        /// their parent bone's local space. Head and torso props use the bind pose; hand props are placed in the idle
        /// pose so they hang naturally while the NPC idles.
        /// </summary>
        private static SharedProps BuildSharedProps(PartLibrary parts)
        {
            var shared = new SharedProps();
            GameObject reference = parts.InstantiateBase();
            var witch = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WitchPrefab));
            try
            {
                Animator animator = reference.GetComponentInChildren<Animator>();
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                shared.ChestBone = animator.GetBoneTransform(HumanBodyBones.UpperChest) != null ? HumanBodyBones.UpperChest : HumanBodyBones.Chest;
                shared.ScarfBone = animator.GetBoneTransform(HumanBodyBones.Neck) != null ? HumanBodyBones.Neck : shared.ChestBone;
                Transform chest = animator.GetBoneTransform(shared.ChestBone);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck) ?? chest;
                Transform upperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var bodyRenderer = reference.transform.Find("SM_Chibi_Body").GetComponent<SkinnedMeshRenderer>();

                Vector3[] skin = BakedWorldVertices(bodyRenderer);
                // The reference still wears its round-neck tee, so torso props are fitted to the clothed surface.
                Vector3[] body = skin.Concat(BakedWorldVertices(reference.transform.Find("clothes").GetComponent<SkinnedMeshRenderer>())).ToArray();
                Vector3[] eyes = BakedWorldVertices(reference.transform.Find("SM_Chibi_Eye").GetComponent<SkinnedMeshRenderer>());
                float shoulderX = Mathf.Abs(upperArm.position.x);
                Bounds headBounds = Encapsulate(skin.Where(v => v.y > neck.position.y + 0.02f));
                Bounds torso = Encapsulate(body.Where(v => v.y < neck.position.y && v.y > hips.position.y && Mathf.Abs(v.x) < shoulderX * 0.9f));

                var witchParts = new WitchParts(witch);
                Vector3[] bob = FitWitchBob(witchParts, headBounds);
                shared.WitchBob = SaveMesh("WitchBob", MeshBuilder.FromWorld(witchParts.Hair, bob), head);

                // One hat per hairstyle that wears one, each fitted to that hair, with a matching tucked hair mesh.
                float brimY = Encapsulate(eyes).max.y + headBounds.size.y * BrimAboveEyes;
                var axis = new Vector2(headBounds.center.x, headBounds.center.z);
                foreach (Hair style in Recipes.Where(r => Has(r, Props.WitchHat)).Select(r => r.Hair).Distinct())
                {
                    Vector3[] hair = style == Hair.Bob ? bob : parts.WorldVertices(style == Hair.Long ? PartLibrary.LongHair : PartLibrary.ShortHair);
                    Vector3[] hat = FitWitchHat(witchParts, hair, axis, brimY);
                    shared.Hats[style] = SaveMesh("WitchHat_" + style, MeshBuilder.FromWorld(witchParts.Hat, hat), head);
                    var profile = new HatProfile(hat, brimY);
                    shared.TuckedHair[style] = style == Hair.Bob
                        ? SaveMesh("HatHair_" + style, MeshBuilder.FromWorld(witchParts.Hair, bob.Select(profile.Tuck).ToArray()), head)
                        : parts.SaveTuckedHair(style == Hair.Long ? PartLibrary.LongHair : PartLibrary.ShortHair, "HatHair_" + style, profile);
                }

                shared.Glasses = SaveMesh("Glasses", BuildGlasses(eyes, headBounds), head);
                shared.Scarf = SaveMesh("Scarf", BuildScarf(body, neck.position.y, shoulderX), animator.GetBoneTransform(shared.ScarfBone));
                shared.Badge = SaveMesh("Badge", BuildBadge(body, torso, Mathf.Lerp(hips.position.y, neck.position.y, 0.6f)), chest);
                shared.SatchelBag = SaveMesh("SatchelBag", BuildSatchelBag(torso, hips.position.y), hips);
                shared.SatchelStrap = SaveMesh("SatchelStrap", BuildSatchelStrap(torso, neck.position.y, shoulderX), chest);

                if (Recipes.Any(r => r.Outfit == Outfit.WitchRobe))
                {
                    shared.ChibiHead = SaveMeshAsset(BuildChibiHead(bodyRenderer, head));
                    TransferWitchRobe(witch, reference, out shared.RobeBody, out shared.RobeCape);
                }

                PoseIdle(reference);
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                shared.Book = SaveMesh("Book", BuildBook(hand, headBounds.size.y), hand);
                shared.Clipboard = SaveMesh("Clipboard", BuildClipboard(hand, headBounds.size.y), hand);
                // The staff leans out from the hand so its carved head rises beside the hat rather than through the crown.
                shared.Staff = BuildStaffMesh(animator, brimY + headBounds.size.y * 0.6f, out shared.StaffMaterials);
            }
            finally
            {
                Object.DestroyImmediate(reference);
                Object.DestroyImmediate(witch);
            }

            return shared;
        }

        /// <summary>The Chibi Witch's head, hair and hat baked in world space at its rest pose.</summary>
        private sealed class WitchParts
        {
            public readonly Mesh Hair, Hat;
            public readonly Bounds Head;
            public readonly Vector2 Axis;

            public WitchParts(GameObject witch)
            {
                SkinnedMeshRenderer Find(string name) => witch.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == name);
                Hair = BakedWorldMesh(Find("hair_low1"));
                Hat = BakedWorldMesh(Find("hat_low1"));
                Head = Encapsulate(BakedWorldVertices(Find("head_low1")));
                Axis = new Vector2(Head.center.x, Head.center.z);
            }
        }

        /// <summary>Maps the witch's bob from her head onto the chibi head: match width and depth, align the crowns.</summary>
        private static Vector3[] FitWitchBob(WitchParts witch, Bounds chibiHead)
        {
            var scale = new Vector3(chibiHead.size.x / witch.Head.size.x * BobFit, chibiHead.size.y / witch.Head.size.y,
                chibiHead.size.z / witch.Head.size.z * BobFit);
            var witchCrown = new Vector3(witch.Head.center.x, witch.Head.max.y, witch.Head.center.z);
            var chibiCrown = new Vector3(chibiHead.center.x, chibiHead.max.y, chibiHead.center.z);
            Matrix4x4 fit = Matrix4x4.Translate(chibiCrown) * Matrix4x4.Scale(scale) * Matrix4x4.Translate(-witchCrown);
            return witch.Hair.vertices.Select(v => fit.MultiplyPoint3x4(v)).ToArray();
        }

        /// <summary>
        /// Refits the witch's hat to a chibi hairstyle. The chibi head is far larger relative to the hat than the witch's,
        /// so a uniform scale either floats the hat or makes the brim enormous. Instead the crown is scaled so its opening
        /// matches the hair's width at brim height, the brim is scaled less, and the brim is seated just above the eyes,
        /// as the hat sits on the witch.
        /// </summary>
        private static Vector3[] FitWitchHat(WitchParts witch, Vector3[] hair, Vector2 axis, float brimY)
        {
            Vector3[] hat = witch.Hat.vertices;
            float hatHeight = witch.Hat.bounds.size.y;
            float Radial(Vector3 v, Vector2 centre) => new Vector2(v.x - centre.x, v.z - centre.y).magnitude;

            // On the witch: the brim plane, the brim radius and the hair (crown opening) radius at that height.
            float maxRadius = hat.Max(v => Radial(v, witch.Axis));
            float witchBrimY = Median(hat.Where(v => Radial(v, witch.Axis) > maxRadius * 0.7f).Select(v => v.y));
            float witchBrim = Percentile(hat.Where(v => Mathf.Abs(v.y - witchBrimY) < hatHeight * 0.1f).Select(v => Radial(v, witch.Axis)), 0.9f);
            Vector3[] witchHair = witch.Hair.vertices;
            float witchOpening = Percentile(BandOrAll(witchHair, witchBrimY, hatHeight * 0.06f).Select(v => Radial(v, witch.Axis)), 0.95f);

            // On the chibi: the same quantities for this hairstyle.
            float opening = Percentile(BandOrAll(hair, brimY, hatHeight * 0.06f).Select(v => Radial(v, axis)), 0.95f) * HatOpeningMargin;
            float brim = opening * (witchBrim / witchOpening) * BrimScale;
            float crown = opening / witchOpening;
            float brimLift = Mathf.Lerp(1f, crown, 0.5f);

            var fitted = new Vector3[hat.Length];
            for (int i = 0; i < hat.Length; i++)
            {
                Vector3 v = hat[i];
                var offset = new Vector2(v.x - witch.Axis.x, v.z - witch.Axis.y);
                float r = offset.magnitude;
                Vector2 direction = r > 1e-5f ? offset / r : Vector2.zero;
                float brimRadius = r <= witchOpening ? r * crown : opening + (r - witchOpening) * (brim - opening) / Mathf.Max(witchBrim - witchOpening, 1e-4f);
                // Blend from brim scaling to crown scaling just above the brim, so the crown and tip keep their shape.
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(witchBrimY + hatHeight * 0.05f, witchBrimY + hatHeight * 0.22f, v.y));
                float radius = Mathf.Lerp(brimRadius, r * crown, t);
                float height = (v.y - witchBrimY) * Mathf.Lerp(brimLift, crown * CrownHeight, t);
                fitted[i] = new Vector3(axis.x + direction.x * radius, brimY + height, axis.y + direction.y * radius);
            }

            return fitted;
        }

        /// <summary>The inside of a fitted hat, sampled in height bands, used to tuck hair inside its crown.</summary>
        private sealed class HatProfile
        {
            private const int Bands = 24;
            private readonly float baseY, bandHeight;
            private readonly Vector2[] centres = new Vector2[Bands];
            private readonly float[] radii = new float[Bands];

            public HatProfile(Vector3[] hat, float brimY)
            {
                baseY = brimY;
                bandHeight = (hat.Max(v => v.y) - brimY) / Bands;
                for (int b = 0; b < Bands; b++)
                {
                    float low = baseY + b * bandHeight;
                    Vector3[] band = hat.Where(v => v.y >= low && v.y < low + bandHeight).ToArray();
                    if (band.Length < 6 && b > 0)
                    {
                        centres[b] = centres[b - 1];
                        radii[b] = radii[b - 1] * 0.8f;
                        continue;
                    }

                    var centre = new Vector2(band.Average(v => v.x), band.Average(v => v.z));
                    centres[b] = centre;
                    radii[b] = band.Min(v => new Vector2(v.x - centre.x, v.z - centre.y).magnitude);
                }
            }

            public Vector3 Tuck(Vector3 v)
            {
                if (v.y <= baseY) return v;
                int b = Mathf.Min((int)((v.y - baseY) / bandHeight), Bands - 1);
                float y = Mathf.Min(v.y, baseY + bandHeight * (Bands - 1));
                var offset = new Vector2(v.x - centres[b].x, v.z - centres[b].y);
                float allowed = radii[b] * TuckMargin;
                if (offset.magnitude > allowed) offset = offset.normalized * allowed;
                return new Vector3(centres[b].x + offset.x, y, centres[b].y + offset.y);
            }
        }

        /// <summary>
        /// Copies the chibi body mesh keeping only the triangles skinned mainly to the head, for outfits that bring
        /// their own neck, hands and legs.
        /// </summary>
        private static Mesh BuildChibiHead(SkinnedMeshRenderer body, Transform headBone)
        {
            Mesh source = body.sharedMesh;
            int head = Array.IndexOf(body.bones, headBone);
            BoneWeight[] weights = source.boneWeights;
            bool OnHead(int i)
            {
                BoneWeight w = weights[i];
                return (w.boneIndex0 == head ? w.weight0 : 0f) + (w.boneIndex1 == head ? w.weight1 : 0f) +
                       (w.boneIndex2 == head ? w.weight2 : 0f) + (w.boneIndex3 == head ? w.weight3 : 0f) >= 0.5f;
            }

            Mesh mesh = Object.Instantiate(source);
            mesh.name = "ChibiHead";
            for (int s = 0; s < source.subMeshCount; s++)
            {
                int[] triangles = source.GetTriangles(s);
                var kept = new List<int>();
                for (int i = 0; i < triangles.Length; i += 3)
                    if (OnHead(triangles[i]) && OnHead(triangles[i + 1]) && OnHead(triangles[i + 2]))
                        kept.AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
                mesh.SetTriangles(kept, s);
            }

            return mesh;
        }

        /// <summary>
        /// Transfers the Chibi Witch's robe (body) and capelet onto the chibi skeleton. Both rigs are put in the same
        /// humanoid pose; every vertex then keeps its offset from the humanoid bone it is skinned to, scaled to the chibi's
        /// size, and is re-skinned to the matching chibi bone. Hands land on the chibi hands and boots on its feet.
        /// </summary>
        private static void TransferWitchRobe(GameObject witch, GameObject chibi, out SkinnedPiece robe, out SkinnedPiece cape)
        {
            Animator witchAnimator = witch.GetComponentInChildren<Animator>();
            Animator chibiAnimator = chibi.GetComponentInChildren<Animator>();
            NeutralPose(witchAnimator);
            NeutralPose(chibiAnimator);

            float Height(Animator a) => a.GetBoneTransform(HumanBodyBones.Neck).position.y -
                                        (a.GetBoneTransform(HumanBodyBones.LeftFoot).position.y + a.GetBoneTransform(HumanBodyBones.RightFoot).position.y) * 0.5f;
            float Width(Animator a) => Vector3.Distance(a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position, a.GetBoneTransform(HumanBodyBones.RightUpperArm).position);
            // The chibi body is shorter and broader than the witch's, so width and height are matched separately.
            float across = Width(chibiAnimator) / Width(witchAnimator);
            var scale = new Vector3(across, Height(chibiAnimator) / Height(witchAnimator), across);

            SkinnedMeshRenderer Find(string name) => witch.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == name);
            robe = TransferPiece(Find("body_low1"), witchAnimator, chibiAnimator, scale, "WitchRobe_Body");
            cape = TransferPiece(Find("body2_low1"), witchAnimator, chibiAnimator, scale, "WitchRobe_Capelet");
        }

        private static SkinnedPiece TransferPiece(SkinnedMeshRenderer source, Animator witch, Animator chibi, Vector3 scale, string name)
        {
            var witchHuman = new Dictionary<Transform, HumanBodyBones>();
            for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
            {
                Transform t = witch.GetBoneTransform(b);
                if (t != null) witchHuman[t] = b;
            }

            HumanBodyBones HumanAncestor(Transform t)
            {
                for (; t != null; t = t.parent)
                    if (witchHuman.TryGetValue(t, out HumanBodyBones b)) return b;
                return HumanBodyBones.Hips;
            }

            Transform ChibiBone(HumanBodyBones b)
            {
                Transform t;
                while ((t = chibi.GetBoneTransform(b)) == null) b = (HumanBodyBones)HumanTrait.GetParentBone((int)b);
                return t;
            }

            HumanBodyBones[] human = source.bones.Select(HumanAncestor).ToArray();
            Transform[] target = human.Select(ChibiBone).ToArray();
            Vector3[] pivot = human.Select(b => witch.GetBoneTransform(b).position).ToArray();
            List<Transform> bones = target.Distinct().ToList();

            Mesh posed = BakedWorldMesh(source);
            Mesh mesh = source.sharedMesh;
            Vector3[] vertices = posed.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            var outVertices = new Vector3[vertices.Length];
            var outWeights = new BoneWeight[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                BoneWeight w = weights[i];
                var influences = new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) };
                var merged = new Dictionary<int, float>();
                Vector3 position = Vector3.zero;
                float total = 0f;
                foreach ((int index, float weight) in influences)
                {
                    if (weight <= 0f) continue;
                    position += weight * (target[index].position + Vector3.Scale(scale, vertices[i] - pivot[index]));
                    total += weight;
                    int bone = bones.IndexOf(target[index]);
                    merged[bone] = merged.TryGetValue(bone, out float sum) ? sum + weight : weight;
                }

                outVertices[i] = total > 0f ? position / total : vertices[i];
                var top = merged.OrderByDescending(p => p.Value).Take(4).ToArray();
                float norm = top.Sum(p => p.Value);
                (int Bone, float Weight) Get(int k) => k < top.Length ? (top[k].Key, top[k].Value / norm) : (0, 0f);
                outWeights[i] = new BoneWeight
                {
                    boneIndex0 = Get(0).Bone, weight0 = Get(0).Weight, boneIndex1 = Get(1).Bone, weight1 = Get(1).Weight,
                    boneIndex2 = Get(2).Bone, weight2 = Get(2).Weight, boneIndex3 = Get(3).Bone, weight3 = Get(3).Weight
                };
            }

            // The renderer sits at the NPC root, which is at the origin during generation, so world space is mesh space.
            var result = new Mesh { name = name };
            if (vertices.Length > 65000) result.indexFormat = IndexFormat.UInt32;
            result.vertices = outVertices;
            result.normals = posed.normals;
            result.uv = mesh.uv;
            result.subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++) result.SetTriangles(mesh.GetTriangles(s), s);
            result.boneWeights = outWeights;
            result.bindposes = bones.Select(b => b.worldToLocalMatrix).ToArray();
            result.RecalculateBounds();
            result.RecalculateTangents();
            Object.DestroyImmediate(posed);
            return new SkinnedPiece { Mesh = SaveMeshAsset(result), Bones = bones.Select(b => b.name).ToArray() };
        }

        /// <summary>Puts a humanoid in Unity's neutral muscle pose, so two different rigs line up bone for bone.</summary>
        private static void NeutralPose(Animator animator)
        {
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            for (int i = 0; i < pose.muscles.Length; i++) pose.muscles[i] = 0f;
            handler.SetHumanPose(ref pose);
            handler.Dispose();
        }

        private static void AddSkinnedPiece(GameObject npc, Dictionary<string, Transform> bones, SkinnedPiece piece, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(npc.transform, false);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = piece.Mesh;
            renderer.bones = piece.Bones.Select(b => bones[b]).ToArray();
            renderer.rootBone = bones["Hips"];
            Bounds meshBounds = piece.Mesh.bounds;
            renderer.localBounds = new Bounds(renderer.rootBone.InverseTransformPoint(npc.transform.TransformPoint(meshBounds.center)), meshBounds.size * 1.2f);
            renderer.sharedMaterials = Enumerable.Repeat(material, piece.Mesh.subMeshCount).ToArray();
        }

        private static MeshBuilder BuildGlasses(Vector3[] eyes, Bounds head)
        {
            var builder = new MeshBuilder(1);
            float midX = eyes.Average(v => v.x);
            Bounds left = Encapsulate(eyes.Where(v => v.x < midX));
            Bounds right = Encapsulate(eyes.Where(v => v.x >= midX));
            float radius = Mathf.Max(left.size.x, left.size.y) * 0.85f;
            float tube = radius * 0.12f;
            float frontZ = Mathf.Max(left.max.z, right.max.z) + tube * 2f;
            Quaternion facing = Quaternion.Euler(90f, 0f, 0f);
            foreach (Bounds eye in new[] { left, right })
                builder.AddTorus(Matrix4x4.TRS(new Vector3(eye.center.x, eye.center.y, frontZ), facing, Vector3.one), radius, tube, 0);

            float y = (left.center.y + right.center.y) * 0.5f;
            float innerL = left.center.x + radius * Mathf.Sign(right.center.x - left.center.x);
            float innerR = right.center.x - radius * Mathf.Sign(right.center.x - left.center.x);
            builder.AddCylinder(new Vector3(innerL, y + radius * 0.25f, frontZ), new Vector3(innerR, y + radius * 0.25f, frontZ), tube, 0);

            // Temple arms run from the outer rim back towards the ears.
            foreach (Bounds eye in new[] { left, right })
            {
                float side = Mathf.Sign(eye.center.x - midX);
                float x = eye.center.x + side * radius;
                float earX = head.center.x + side * head.extents.x * 0.97f;
                builder.AddCylinder(new Vector3(x, eye.center.y, frontZ), new Vector3(earX, eye.center.y + radius * 0.2f, head.center.z), tube, 0);
            }

            return builder;
        }

        private static MeshBuilder BuildScarf(Vector3[] body, float neckY, float shoulderX)
        {
            var builder = new MeshBuilder(1);
            Bounds collar = Encapsulate(body.Where(v => v.y < neckY && v.y > neckY - 0.12f && Mathf.Abs(v.x) < shoulderX * 0.9f));
            float tube = collar.size.y * 0.32f + 0.025f;
            Vector3 centre = new Vector3(collar.center.x, neckY - 0.03f, collar.center.z);
            Matrix4x4 ring = Matrix4x4.TRS(centre, Quaternion.identity, new Vector3(1f, 1f, collar.size.z / Mathf.Max(collar.size.x, 0.01f)));
            builder.AddTorus(ring, collar.extents.x * 0.98f, tube, 0);

            // A short knotted tail hangs at the front, off-centre.
            Vector3 knot = new Vector3(collar.center.x + collar.extents.x * 0.35f, neckY - 0.06f, collar.max.z + tube * 0.4f);
            builder.AddSphere(Matrix4x4.TRS(knot, Quaternion.identity, Vector3.one * tube * 2.4f), 0);
            builder.AddBox(Matrix4x4.TRS(knot + new Vector3(0.015f, -tube * 2.4f, 0.01f), Quaternion.Euler(4f, 0f, -8f),
                new Vector3(tube * 1.9f, tube * 4f, tube * 0.7f)), 0);
            return builder;
        }

        private static MeshBuilder BuildBadge(Vector3[] body, Bounds torso, float chestY)
        {
            var builder = new MeshBuilder(2);
            float x = torso.center.x - torso.extents.x * 0.42f;
            var band = body.Where(v => Mathf.Abs(v.y - chestY) < 0.04f && Mathf.Abs(v.x - x) < 0.04f).ToArray();
            float frontZ = band.Length > 0 ? band.Max(v => v.z) : torso.max.z;
            Vector3 centre = new Vector3(x, chestY, frontZ + 0.012f);
            builder.AddBox(Matrix4x4.TRS(centre, Quaternion.identity, new Vector3(0.13f, 0.085f, 0.015f)), 0);
            builder.AddBox(Matrix4x4.TRS(centre + new Vector3(0f, 0.028f, 0.006f), Quaternion.identity, new Vector3(0.13f, 0.024f, 0.012f)), 1);
            return builder;
        }

        private static MeshBuilder BuildSatchelBag(Bounds torso, float hipsY)
        {
            var builder = new MeshBuilder(2);
            Vector3 size = new Vector3(0.11f, 0.28f, 0.34f);
            Vector3 centre = new Vector3(torso.min.x - size.x * 0.45f, hipsY + 0.06f, torso.center.z + 0.03f);
            builder.AddBox(Matrix4x4.TRS(centre, Quaternion.identity, size), 0);
            builder.AddBox(Matrix4x4.TRS(centre + new Vector3(-0.012f, 0.05f, 0f), Quaternion.identity,
                new Vector3(size.x + 0.02f, size.y * 0.55f, size.z + 0.02f)), 1);
            return builder;
        }

        private static MeshBuilder BuildSatchelStrap(Bounds torso, float neckY, float shoulderX)
        {
            // A sash: a belt-like loop around the torso, tilted so it runs from the right shoulder to the left hip.
            var builder = new MeshBuilder(1);
            Vector3 centre = torso.center;
            var shoulder = new Vector2(shoulderX * 0.55f, neckY - centre.y);
            float radius = shoulder.magnitude * 1.05f;
            float angle = Mathf.Atan2(shoulder.y, shoulder.x) * Mathf.Rad2Deg;
            float depth = (torso.extents.z + 0.03f) / radius;
            builder.AddTorus(Matrix4x4.TRS(centre, Quaternion.Euler(0f, 0f, angle), Vector3.one) * Matrix4x4.Scale(new Vector3(1f, 1f, depth)),
                radius, 0.02f, 0);
            return builder;
        }

        private static MeshBuilder BuildBook(Transform hand, float headHeight)
        {
            var builder = new MeshBuilder(2);
            Vector3 size = new Vector3(0.07f, headHeight * 0.36f, headHeight * 0.27f);
            Vector3 centre = hand.position + new Vector3(0.02f, -size.y * 0.32f, 0.03f);
            builder.AddBox(Matrix4x4.TRS(centre, Quaternion.identity, size), 0);
            builder.AddBox(Matrix4x4.TRS(centre + new Vector3(0f, 0f, 0.006f), Quaternion.identity,
                new Vector3(size.x * 0.8f, size.y * 0.94f, size.z * 0.96f)), 1);
            return builder;
        }

        private static MeshBuilder BuildClipboard(Transform hand, float headHeight)
        {
            var builder = new MeshBuilder(3);
            Vector3 size = new Vector3(0.025f, headHeight * 0.42f, headHeight * 0.31f);
            Vector3 centre = hand.position + new Vector3(0.025f, -size.y * 0.25f, 0.05f);
            Quaternion tilt = Quaternion.Euler(0f, -18f, 0f);
            builder.AddBox(Matrix4x4.TRS(centre, tilt, size), 0);
            builder.AddBox(Matrix4x4.TRS(centre + tilt * new Vector3(0.016f, -0.01f, 0f), tilt,
                new Vector3(0.008f, size.y * 0.86f, size.z * 0.88f)), 1);
            builder.AddBox(Matrix4x4.TRS(centre + tilt * new Vector3(0.02f, size.y * 0.44f, 0f), tilt,
                new Vector3(0.02f, size.y * 0.08f, size.z * 0.4f)), 2);
            return builder;
        }

        /// <summary>
        /// Bakes the staff into the right hand's local space for the idle pose: it stands on the ground, passes through
        /// the hand and leans outward, reaching the given height.
        /// </summary>
        private static Mesh BuildStaffMesh(Animator animator, float top, out Material[] materials)
        {
            var staff = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(StaffPrefab));
            try
            {
                MeshFilter[] filters = staff.GetComponentsInChildren<MeshFilter>();
                var materialList = new List<Material>();
                foreach (MeshFilter filter in filters)
                    foreach (Material material in filter.GetComponent<MeshRenderer>().sharedMaterials)
                        if (!materialList.Contains(material)) materialList.Add(material);

                Bounds local = Encapsulate(filters.SelectMany(f => f.sharedMesh.vertices.Select(v => f.transform.localToWorldMatrix.MultiplyPoint3x4(v))));
                int axis = local.size.x > local.size.y ? (local.size.x > local.size.z ? 0 : 2) : (local.size.y > local.size.z ? 1 : 2);
                Quaternion upright = axis == 0 ? Quaternion.Euler(0f, 0f, 90f) : axis == 2 ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;

                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform root = animator.transform;
                Quaternion lean = Quaternion.Euler(0f, 0f, -StaffLean * Mathf.Sign(hand.position.x - root.position.x));
                Vector3 direction = lean * Vector3.up;
                Vector3 grip = hand.position + new Vector3(0f, 0f, 0.05f);
                Vector3 foot = grip - direction * ((grip.y - root.position.y) / direction.y);
                float scale = (top - root.position.y) / direction.y / local.size[axis];

                Bounds rotated = Encapsulate(Corners(local).Select(c => upright * c));
                var bottom = new Vector3(rotated.center.x, rotated.min.y, rotated.center.z) * scale;
                Matrix4x4 place = Matrix4x4.TRS(foot, lean, Vector3.one) * Matrix4x4.Translate(-bottom) * Matrix4x4.TRS(Vector3.zero, upright, Vector3.one * scale);

                var builder = new MeshBuilder(materialList.Count);
                foreach (MeshFilter filter in filters)
                {
                    Material[] shared = filter.GetComponent<MeshRenderer>().sharedMaterials;
                    for (int s = 0; s < filter.sharedMesh.subMeshCount; s++)
                        builder.AddMesh(filter.sharedMesh, place * filter.transform.localToWorldMatrix, materialList.IndexOf(shared[Mathf.Min(s, shared.Length - 1)]), s);
                }

                Material wood = AssetDatabase.LoadAssetAtPath<Material>(StaffMaterial);
                materials = wood == null ? materialList.ToArray() : materialList.Select(_ => wood).ToArray();
                return SaveMesh("Staff", builder, hand);
            }
            finally
            {
                Object.DestroyImmediate(staff);
            }
        }

        // ---------------------------------------------------------------- parts and assets

        private readonly struct PartRef
        {
            public readonly string Model, Renderer;
            public PartRef(string model, string renderer) { Model = model; Renderer = renderer; }
        }

        /// <summary>
        /// Instantiates each cast model once so its skinned parts can be copied onto new NPCs. Katrina's dress is not
        /// offered: the arms clip through its sides in the idle pose. Her twin-tail hair does not share the common
        /// bind pose and stays unique to her.
        /// </summary>
        private sealed class PartLibrary : IDisposable
        {
            public static readonly PartRef LongHair = new PartRef("Cinna Cirra", "hair");
            public static readonly PartRef ShortHair = new PartRef("Orio", "Hair");
            public static readonly PartRef TeeRound = new PartRef("Cinna Cirra", "clothes");
            public static readonly PartRef TeeCropped = new PartRef("Stella", "clothes");
            public static readonly PartRef TeeLoose = new PartRef("Orio", "Clothes");
            public static readonly PartRef LeftShoe = new PartRef("Katarina", "leftshoe.");
            public static readonly PartRef RightShoe = new PartRef("Katarina", "rightshoe");

            private readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();

            public GameObject InstantiateBase()
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BaseModel));
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                return instance;
            }

            public Material Material(PartRef part) => Renderer(part).sharedMaterial;

            public Vector3[] WorldVertices(PartRef part) => BakedWorldVertices(Renderer(part));

            /// <summary>Saves a copy of a skinned hair part with every vertex above the brim tucked inside the hat.</summary>
            public Mesh SaveTuckedHair(PartRef part, string name, HatProfile hat)
            {
                SkinnedMeshRenderer source = Renderer(part);
                // Cast parts are authored in their bind pose, so the renderer transform maps mesh space to rest-pose world.
                Matrix4x4 toWorld = source.transform.localToWorldMatrix;
                Matrix4x4 toLocal = toWorld.inverse;
                Mesh mesh = Object.Instantiate(source.sharedMesh);
                mesh.name = name;
                mesh.vertices = mesh.vertices.Select(v => toLocal.MultiplyPoint3x4(hat.Tuck(toWorld.MultiplyPoint3x4(v)))).ToArray();
                mesh.RecalculateBounds();
                return SaveMeshAsset(mesh);
            }

            /// <summary>
            /// Copies a skinned part onto the NPC's skeleton. The part keeps its original transform relative to the model
            /// root so its bind poses stay valid; all cast models share the SM_Chibi bone names and rest pose.
            /// </summary>
            public void AddSkinnedPart(GameObject npc, Dictionary<string, Transform> bones, PartRef part, string name, Material material,
                Mesh meshOverride = null)
            {
                SkinnedMeshRenderer source = Renderer(part);
                Transform sourceRoot = models[part.Model].transform;
                Matrix4x4 relative = sourceRoot.worldToLocalMatrix * source.transform.localToWorldMatrix;

                var go = new GameObject(name);
                go.transform.SetParent(npc.transform, false);
                go.transform.localPosition = relative.GetColumn(3);
                go.transform.localRotation = relative.rotation;
                go.transform.localScale = relative.lossyScale;

                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = meshOverride != null ? meshOverride : source.sharedMesh;
                renderer.bones = source.bones.Select(b => bones[b.name]).ToArray();
                renderer.rootBone = bones[source.rootBone.name];
                renderer.localBounds = source.localBounds;
                renderer.sharedMaterials = Enumerable.Repeat(material, source.sharedMaterials.Length).ToArray();
            }

            private SkinnedMeshRenderer Renderer(PartRef part)
            {
                if (!models.TryGetValue(part.Model, out GameObject model))
                {
                    model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NpcFolder + part.Model + ".fbx"));
                    model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    models[part.Model] = model;
                }

                return model.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == part.Renderer);
            }

            public void Dispose()
            {
                foreach (GameObject model in models.Values) Object.DestroyImmediate(model);
                models.Clear();
            }
        }

        private static void AttachProp(Animator animator, HumanBodyBones boneId, string name, Mesh mesh, params Material[] materials)
        {
            Transform bone = animator.GetBoneTransform(boneId);
            var go = new GameObject(name);
            go.transform.SetParent(bone, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        private static Material SaveMaterial(string npc, string slot, Material template, Color colour)
        {
            var material = new Material(template) { name = npc + "_" + slot };
            material.SetColor("_BaseColor", colour);
            material.SetColor("_Color", colour);
            return SaveMaterialAsset(material);
        }

        private static Material SavePlain(string npc, string slot, Color colour, float smoothness)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = npc + "_" + slot };
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            return SaveMaterialAsset(material);
        }

        private static Material SaveWitchMaterial(string npc, string slot, Color? mainCloth = null, Color? details = null, Color? hair = null,
            Color? skin = null, Color? additional = null, Color? skirt = null)
        {
            var material = new Material(AssetDatabase.LoadAssetAtPath<Material>(WitchMaterial)) { name = npc + "_" + slot };
            if (mainCloth.HasValue) material.SetColor("_MainCloth_Color", mainCloth.Value);
            if (details.HasValue) material.SetColor("_ClothDetails_Color", details.Value);
            if (hair.HasValue) material.SetColor("_Hair_Color", hair.Value);
            if (skin.HasValue) material.SetColor("_Skin_Color", skin.Value);
            if (additional.HasValue) material.SetColor("_AdditionalCloth_Color", additional.Value);
            if (skirt.HasValue) material.SetColor("_Skirt_Color", skirt.Value);
            return SaveMaterialAsset(material);
        }

        private static Material SaveMaterialAsset(Material material)
        {
            string path = MaterialFolder + material.name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
                return material;
            }

            existing.shader = material.shader;
            existing.CopyPropertiesFromMaterial(material);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(material);
            return existing;
        }

        private static Mesh SaveMesh(string name, MeshBuilder builder, Transform bone) => SaveMeshAsset(builder.ToMesh(name, bone.worldToLocalMatrix));

        private static Mesh SaveMeshAsset(Mesh mesh)
        {
            string name = mesh.name;
            string path = MeshFolder + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            CopyMesh(mesh, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>
        /// Overwrites a mesh asset in place through the Mesh API (keeping its GUID). CopySerialized leaves meshes that are
        /// already loaded in the session showing their old geometry.
        /// </summary>
        private static void CopyMesh(Mesh source, Mesh target)
        {
            target.Clear();
            target.indexFormat = source.indexFormat;
            target.vertices = source.vertices;
            target.normals = source.normals;
            target.tangents = source.tangents;
            target.uv = source.uv;
            target.boneWeights = source.boneWeights;
            target.bindposes = source.bindposes;
            target.subMeshCount = source.subMeshCount;
            for (int s = 0; s < source.subMeshCount; s++) target.SetTriangles(source.GetTriangles(s), s);
            target.RecalculateBounds();
        }

        // ---------------------------------------------------------------- posing and geometry helpers

        /// <summary>
        /// Poses an instance in its controller's default (idle) clip. Animator.Update does not evaluate outside play
        /// mode in batch runs, so the clip is sampled directly.
        /// </summary>
        private static void PoseIdle(GameObject npc)
        {
            Animator animator = npc.GetComponentInChildren<Animator>();
            var controller = (animator.runtimeAnimatorController
                              ?? AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ChibiController)) as UnityEditor.Animations.AnimatorController;
            var idle = controller?.layers[0].stateMachine.defaultState?.motion as AnimationClip;
            if (idle == null) return;
            Transform root = animator.transform;
            root.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
            idle.SampleAnimation(animator.gameObject, IdleSampleTime);
            root.SetLocalPositionAndRotation(position, rotation);
        }

        private static Vector3[] BakedWorldVertices(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = BakedWorldMesh(renderer);
            Vector3[] vertices = mesh.vertices;
            Object.DestroyImmediate(mesh);
            return vertices;
        }

        /// <summary>Bakes a skinned renderer's current pose into a world-space mesh.</summary>
        private static Mesh BakedWorldMesh(SkinnedMeshRenderer renderer)
        {
            // Without useScale the bake already includes the renderer's scale (the cast hair is scaled ~180x), so only
            // position and rotation remain to be applied.
            var baked = new Mesh();
            renderer.BakeMesh(baked, false);
            Matrix4x4 toWorld = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
            baked.vertices = baked.vertices.Select(v => toWorld.MultiplyPoint3x4(v)).ToArray();
            baked.normals = baked.normals.Select(n => (toWorld.rotation * n).normalized).ToArray();
            baked.RecalculateBounds();
            return baked;
        }

        private static Dictionary<string, Transform> BonesByName(GameObject root)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            return bones;
        }

        private static Bounds Encapsulate(IEnumerable<Vector3> points)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (Vector3 p in points)
            {
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                else bounds.Encapsulate(p);
            }

            return bounds;
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        }

        private static void SaveRender(RenderTexture target, string path)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static void EnsureFolder(string folder)
        {
            string trimmed = folder.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(trimmed)) return;
            string parent = Path.GetDirectoryName(trimmed).Replace('\\', '/');
            EnsureFolder(parent + "/");
            AssetDatabase.CreateFolder(parent, Path.GetFileName(trimmed));
        }

        private static bool Has(Recipe recipe, Props prop) => (recipe.Props & prop) != 0;

        private static IEnumerable<Vector3> BandOrAll(Vector3[] points, float y, float halfHeight)
        {
            Vector3[] band = points.Where(v => Mathf.Abs(v.y - y) < halfHeight).ToArray();
            return band.Length > 0 ? band : points;
        }

        private static float Percentile(IEnumerable<float> values, float p)
        {
            float[] sorted = values.OrderBy(v => v).ToArray();
            return sorted.Length == 0 ? 0f : sorted[Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Length - 1)), 0, sorted.Length - 1)];
        }

        private static float Median(IEnumerable<float> values) => Percentile(values, 0.5f);

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;

        /// <summary>Accumulates world-space geometry in submeshes and writes it out in a bone's local space.</summary>
        private sealed class MeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int>[] triangles;

            public MeshBuilder(int submeshes)
            {
                triangles = Enumerable.Range(0, submeshes).Select(_ => new List<int>()).ToArray();
            }

            /// <summary>A single-submesh builder holding a mesh's topology, normals and UVs with new world positions.</summary>
            public static MeshBuilder FromWorld(Mesh topology, Vector3[] worldVertices)
            {
                var builder = new MeshBuilder(1);
                builder.vertices.AddRange(worldVertices);
                builder.normals.AddRange(topology.normals);
                builder.uvs.AddRange(topology.uv);
                builder.triangles[0].AddRange(topology.triangles);
                return builder;
            }

            public void AddMesh(Mesh mesh, Matrix4x4 transform, int submesh, int sourceSubmesh = -1)
            {
                Vector3[] v = mesh.vertices;
                Vector3[] n = mesh.normals;
                Vector2[] uv = mesh.uv;
                Matrix4x4 normalMatrix = transform.inverse.transpose;
                int start = vertices.Count;
                for (int i = 0; i < v.Length; i++)
                {
                    vertices.Add(transform.MultiplyPoint3x4(v[i]));
                    normals.Add(n.Length > i ? normalMatrix.MultiplyVector(n[i]).normalized : Vector3.up);
                    uvs.Add(uv.Length > i ? uv[i] : Vector2.zero);
                }

                int[] source = sourceSubmesh >= 0 ? mesh.GetTriangles(sourceSubmesh) : mesh.triangles;
                bool flip = transform.determinant < 0f;
                for (int i = 0; i < source.Length; i += 3)
                {
                    triangles[submesh].Add(start + source[i]);
                    triangles[submesh].Add(start + (flip ? source[i + 2] : source[i + 1]));
                    triangles[submesh].Add(start + (flip ? source[i + 1] : source[i + 2]));
                }
            }

            public void AddBox(Matrix4x4 transform, int submesh) => AddPrimitive(PrimitiveType.Cube, transform, submesh);

            public void AddSphere(Matrix4x4 transform, int submesh) => AddPrimitive(PrimitiveType.Sphere, transform, submesh);

            public void AddCylinder(Vector3 from, Vector3 to, float radius, int submesh)
            {
                Vector3 axis = to - from;
                Matrix4x4 m = Matrix4x4.TRS((from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, axis.normalized),
                    new Vector3(radius * 2f, axis.magnitude * 0.5f, radius * 2f));
                AddPrimitive(PrimitiveType.Cylinder, m, submesh);
            }

            /// <summary>Adds a torus lying in the transform's XZ plane.</summary>
            public void AddTorus(Matrix4x4 transform, float radius, float tube, int submesh, int segments = 28, int sides = 10)
            {
                Matrix4x4 normalMatrix = transform.inverse.transpose;
                int start = vertices.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float u = i / (float)segments * Mathf.PI * 2f;
                    var ring = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u));
                    for (int j = 0; j <= sides; j++)
                    {
                        float w = j / (float)sides * Mathf.PI * 2f;
                        Vector3 normal = ring * Mathf.Cos(w) + Vector3.up * Mathf.Sin(w);
                        vertices.Add(transform.MultiplyPoint3x4(ring * radius + normal * tube));
                        normals.Add(normalMatrix.MultiplyVector(normal).normalized);
                        uvs.Add(new Vector2(i / (float)segments, j / (float)sides));
                    }
                }

                for (int i = 0; i < segments; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        int a = start + i * (sides + 1) + j, b = a + sides + 1;
                        triangles[submesh].AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                    }
            }

            private void AddPrimitive(PrimitiveType type, Matrix4x4 transform, int submesh)
            {
                GameObject primitive = GameObject.CreatePrimitive(type);
                AddMesh(primitive.GetComponent<MeshFilter>().sharedMesh, transform, submesh);
                Object.DestroyImmediate(primitive);
            }

            public Mesh ToMesh(string name, Matrix4x4 toLocal)
            {
                Matrix4x4 normalMatrix = toLocal.inverse.transpose;
                var mesh = new Mesh { name = name };
                if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices.Select(v => toLocal.MultiplyPoint3x4(v)).ToList());
                mesh.SetNormals(normals.Select(n => normalMatrix.MultiplyVector(n).normalized).ToList());
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = triangles.Length;
                for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}
