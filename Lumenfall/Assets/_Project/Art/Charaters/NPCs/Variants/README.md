# Background NPC variants

This folder holds generated background NPCs: students, teachers and receptionists for populating the academy. Every file here is produced by [`NpcVariantGenerator`](../../../../Editor/NpcVariantGenerator.cs). Change the recipes in that script and regenerate rather than editing the prefabs, materials or meshes by hand.

## Roster

| Prefab | Role | Build |
| --- | --- | --- |
| `Student_01` | Student | Long hair, cropped tee, satchel |
| `Student_02` | Student | Short hair, loose tee, satchel, shoes |
| `Student_03` | Student | Bob, cropped tee, book, shoes |
| `Student_04` | Student | Short hair, round-neck tee, glasses, book |
| `Student_05` | Student | Long hair, round-neck tee, glasses, satchel, shoes |
| `Student_06` | Student | Bob, loose tee, satchel |
| `Teacher_01` | Teacher | Short hair, witch hat, glasses, staff, shoes |
| `Teacher_02` | Teacher | Long hair, witch hat, book, shoes |
| `Teacher_03` | Teacher | Bob, Chibi Witch robe and capelet with boots, witch hat, staff |
| `Receptionist_01` | Receptionist | Bob, navy uniform, gold scarf, name badge, glasses, book |
| `Receptionist_02` | Receptionist | Short hair, navy uniform, gold scarf, name badge, clipboard |

Students are scaled to about 0.95–0.98, receptionists to 1.04 and teachers to 1.1–1.12, so the cast (scale 1) sits among the students.

## How they are built

All eleven NPCs use the cast's rig. Every NPC starts from `Cinna Cirra.fbx`, which shares the `SM_Chibi` skeleton with the rest of the cast, and uses `NPC_Chibi.controller`, so it animates exactly as the cast do.

- **Cast parts:** hair and tees from the Cinna, Stella and Orio models and Katrina's shoes, rebound to the NPC's skeleton by bone name.
- **Bob haircut:** baked from the Chibi Witch and refitted to the chibi head.
- **Witch hats:** refitted per hairstyle (`WitchHat_<Hair>`). The crown opening matches that hair's width, the brim sits just above the eyes, and the hair above the brim is swapped for a tucked copy (`HatHair_<Hair>`) that stays inside the crown.
- **Witch robe (`Teacher_03`):** the Chibi Witch's body and capelet are transferred onto the chibi skeleton. Both rigs are put in the same humanoid pose, and every vertex keeps its offset from the humanoid bone it was skinned to, scaled to the chibi's width and height. It is then re-skinned to the matching chibi bone. The chibi body is reduced to its head (`ChibiHead`) because the robe brings its own neck, hands, legs and boots. The robe keeps the witch's textured material, recoloured per NPC.
- **Generated props:** glasses, scarf, badge, satchel, book and clipboard are meshes in [`Meshes`](Meshes). The staff is `Staff4_1_1` from the Wand pack, leaning out from the hand so its head rises beside the hat. That pack is no longer in the project, so the staff uses the shared URP material `NPC_Staff` (a plain wood colour) instead of the pack's, which had gone missing and drew pink. Rigid props are parented to humanoid bones (head, neck, chest, hips, right hand), so they follow the animation.
- **Materials:** each NPC has its own materials in [`Materials`](Materials), named `<Prefab>_<Slot>`.

Two cast parts are deliberately excluded:

- Katrina's dress, because the arms clip through its sides in the idle pose.
- Katrina's twin-tail hair, which does not share the common bind pose and stays unique to her.

Stella's hair and beanie are also not used, to keep her silhouette distinct.

## Regenerating and previewing

- **Lumenfall > NPCs > Generate Background NPCs** rebuilds every prefab, material and mesh at the same paths. GUIDs are kept, so existing references survive a regeneration.
- **Lumenfall > NPCs > Render Background NPC Previews** renders each NPC, plus the four cast models for comparison, to `Lumenfall/Logs/NpcPreviews`. That folder is not tracked.

Both commands work in a temporary additive scene and leave the open scene untouched. For a headless run, use `-executeMethod Lumenfall.EditorTools.NpcVariantGenerator.GenerateAndRender` without `-nographics`. If you remove an item from a recipe, delete its now-unused material or mesh by hand; the generator does not prune old files.

## Using them in the world

Assign a prefab to an entry's `prefab` field in [`NpcCatalog.asset`](../../../../World/Areas/NpcCatalog.asset). `campus_student` and `forest_student` currently use `NpcPlaceholder`. `DynamicNpc` adds its trigger collider at runtime, so the prefabs carry only the model and its Animator. Non-cast speakers use the shared `Random NPC` dialogue frame.

## Limitations

- The prefabs have been checked in editor renders of the idle pose only. Placement, scale against the environment, the camera view and the other `NPC_Chibi` animations have not been verified in play mode.
- The transferred robe is skinned only to humanoid bones. The witch's skirt and corrective bones are folded into their nearest humanoid parent, so the skirt moves rigidly with the hips.
- Rigid hand props (book, clipboard, staff) are placed for the idle pose. Large animations such as Angry or Pointing may swing them oddly.
- All NPCs using the same controller start their idle loop together, so a group can look synchronised.
