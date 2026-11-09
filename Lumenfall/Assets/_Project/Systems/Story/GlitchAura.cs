using System.Collections.Generic;
using Lumenfall.Audio;
using Lumenfall.Controls;
using Lumenfall.DataDriven;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lumenfall.Story
{
    /// <summary>How strongly a character glitches.</summary>
    public enum GlitchAuraLevel
    {
        /// <summary>Nothing.</summary>
        Off,
        /// <summary>Almost nothing: every few seconds the split image flashes for a frame or two.</summary>
        Hint,
        /// <summary>A faint, steady shimmer that never goes away.</summary>
        Lingering,
        /// <summary>Everything at once, for the scene where the glitch shows itself.</summary>
        Full
    }

    /// <summary>
    /// A glitchy aura around a character, built in code like the spell effects. Its palette is a deep violet light with a
    /// magenta-red and electric-blue split image, near-white streaks and blocks, pale cyan Wingdings glyphs, a few gold
    /// embers and dark shards:
    /// <list type="bullet">
    /// <item>Red and blue copies of the character's current pose (<c>Lumenfall/Glitch Ghost</c>), offset either side of
    /// them, that jump out of line and snap back, with bands sliding sideways or dropping out.</item>
    /// <item>Vertical light streaks dripping through them and square block artefacts snapped to a fine grid around their
    /// outline.</item>
    /// <item>Wingdings glyphs (<see cref="WingdingsText"/>), embers and shards drifting upwards.</item>
    /// <item>A violet light that tints the room, and at <see cref="GlitchAuraLevel.Full"/> the character themselves
    /// blinking out for a frame, leaving only the copies, over a low beating drone.</item>
    /// </list>
    /// The story sets <see cref="Level"/> (the cutscene <c>aura</c> step); the editor preview
    /// (<see cref="GlitchAuraPreview.PreviewLevel"/>) can raise it for testing, and the stronger of the two shows.
    /// Reduced Motion keeps the split as a still red and blue outline: no jumps, slices, flicker, streaks or blocks.
    /// </summary>
    public sealed class GlitchAura : MonoBehaviour
    {
        public static readonly Color Red = new Color(1f, 0.176f, 0.333f);       // #FF2D55
        public static readonly Color Blue = new Color(0.239f, 0.482f, 1f);      // #3D7BFF
        public static readonly Color White = new Color(0.91f, 0.94f, 1f);       // #E8F0FF
        public static readonly Color Cyan = new Color(0.561f, 0.827f, 1f);      // #8FD3FF
        public static readonly Color Gold = new Color(1f, 0.702f, 0.278f);      // #FFB347
        public static readonly Color Shard = new Color(0.071f, 0.047f, 0.122f); // #120C1F
        public static readonly Color Violet = new Color(0.48f, 0.29f, 1f);      // a lit #4B2A7A

        private const int MaxLines = 72;
        private const int MaxGlyphs = 8;
        private const string GlyphLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ!#$%&*+<>?@";

        /// <summary>The level the story asks for.</summary>
        public GlitchAuraLevel Level { get; set; }

        /// <summary>What shows: the story's level, or the editor preview's if that is stronger.</summary>
        public GlitchAuraLevel EffectiveLevel =>
            (GlitchAuraLevel)Mathf.Max((int)Level, (int)GlitchAuraPreview.PreviewLevel);

        private struct Profile
        {
            public float ghostAlpha, jump, jumpEvery, slice, dropout, streaks, blocks, embers, shards, glyphs, light, blink;
        }

        private enum Kind { Streak, Block, Ember, ShardBit }

        private sealed class Bit
        {
            public LineRenderer line;
            public Kind kind;
            public float born, life, size, spin;
            public Vector3 position, velocity, direction;
            public Color color;
        }

        private sealed class Glyph
        {
            public TextMesh text;
            public float born, life;
            public Vector3 position, velocity;
        }

        private sealed class Source
        {
            public Renderer renderer;
            public SkinnedMeshRenderer skinned;
            public Mesh mesh;
            public bool baked;
        }

        private readonly List<Source> sources = new List<Source>();
        private readonly List<Bit> bits = new List<Bit>();
        private readonly List<Glyph> glyphs = new List<Glyph>();
        private Transform fxRoot;
        private Material redGhost, blueGhost, lineMaterial;
        private Light glow;
        private AudioSource drone;
        private AudioClip droneClip;
        private float droneVolume;
        private bool hidden;
        private bool warnedShader;
        private float nextJump, nextBlink, blinkUntil, nextHint, hintUntil;
        private Vector2 redOffset, blueOffset;
        private float spawnStreak, spawnBlock, spawnEmber, spawnShard, spawnGlyph;

        private void OnEnable()
        {
            CollectSources();
            if (fxRoot == null)
            {
                fxRoot = new GameObject("Glitch Aura FX").transform;
                fxRoot.SetParent(transform, false);
            }
        }

        private void OnDisable()
        {
            SetHidden(false);
            foreach (Bit bit in bits) if (bit.line != null) bit.line.enabled = false;
            foreach (Glyph glyph in glyphs) if (glyph.text != null) glyph.text.gameObject.SetActive(false);
            if (glow != null) glow.enabled = false;
            if (drone != null) drone.Stop();
            droneVolume = 0f;
        }

        private void OnDestroy()
        {
            foreach (Source source in sources) if (source.baked && source.mesh != null) Destroy(source.mesh);
            if (redGhost != null) Destroy(redGhost);
            if (blueGhost != null) Destroy(blueGhost);
            if (lineMaterial != null) Destroy(lineMaterial);
            if (droneClip != null) Destroy(droneClip);
            if (fxRoot != null) Destroy(fxRoot.gameObject);
        }

        private void CollectSources()
        {
            sources.Clear();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (fxRoot != null && renderer.transform.IsChildOf(fxRoot)) continue;
                if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                    sources.Add(new Source { renderer = renderer, skinned = skinned, mesh = new Mesh { name = "Glitch Ghost Pose" }, baked = true });
                else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                    sources.Add(new Source { renderer = renderer, mesh = filter.sharedMesh });
            }
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            bool reduce = GameSettingsPanel.ReduceMotion;
            GlitchAuraLevel level = EffectiveLevel;
            Profile profile = ProfileFor(level, reduce);
            float now = Time.time;
            float dt = Time.deltaTime;
            UpdateDrone(level == GlitchAuraLevel.Full ? 0.45f : 0f);

            // Hint: nothing at all, then a burst of the full split for a couple of frames.
            if (level == GlitchAuraLevel.Hint && !reduce)
            {
                if (now >= nextHint)
                {
                    hintUntil = now + Random.Range(0.05f, 0.11f);
                    nextHint = now + Random.Range(2.5f, 6f);
                    for (int i = 0; i < 4; i++) SpawnBlockOrStreak(camera, true);
                }
                if (now < hintUntil) profile = ProfileFor(GlitchAuraLevel.Full, false);
                profile.blink = 0f;
                profile.streaks = profile.blocks = profile.embers = profile.shards = profile.glyphs = 0f;
            }

            if (level == GlitchAuraLevel.Off || camera == null)
            {
                SetHidden(false);
                if (glow != null) glow.enabled = false;
                AnimateBits(camera, now, dt);
                AnimateGlyphs(camera, now, dt);
                return;
            }

            if (!TryGetBounds(out Bounds bounds)) return;
            UpdateOffsets(profile, reduce, now);
            DrawGhosts(camera, profile, reduce, now);
            UpdateBlink(profile, now);
            UpdateLight(bounds, profile, reduce, now);
            Spawn(camera, profile, dt);
            AnimateBits(camera, now, dt);
            AnimateGlyphs(camera, now, dt);
        }

        // A low drone under the full aura: two close tones beating against each other, an octave above them and a little
        // hiss. Built once, looped, and eased in and out with the level.
        private void UpdateDrone(float target)
        {
            droneVolume = Mathf.MoveTowards(droneVolume, target, Time.unscaledDeltaTime / 0.6f);
            if (droneVolume <= 0f)
            {
                if (drone != null && drone.isPlaying) drone.Stop();
                return;
            }
            if (drone == null)
            {
                drone = fxRoot.gameObject.AddComponent<AudioSource>();
                drone.playOnAwake = false;
                drone.loop = true;
                drone.spatialBlend = 0f;
                droneClip = DroneClip();
                drone.clip = droneClip;
            }
            drone.volume = droneVolume * AudioMixSettings.Sfx;
            if (!drone.isPlaying) AudioPlayback.Play(drone);
        }

        private static AudioClip DroneClip()
        {
            const int rate = 22050;
            const float seconds = 4f;
            int length = Mathf.RoundToInt(rate * seconds);
            float[] data = new float[length];
            System.Random random = new System.Random(77);
            float hiss = 0f;
            for (int i = 0; i < length; i++)
            {
                // Whole numbers of cycles in four seconds, so the loop has no seam.
                float t = i / (float)rate;
                hiss = Mathf.Lerp(hiss, (float)(random.NextDouble() * 2.0 - 1.0), 0.05f);
                data[i] = 0.32f * Mathf.Sin(2f * Mathf.PI * 55f * t) + 0.28f * Mathf.Sin(2f * Mathf.PI * 58f * t) +
                          0.12f * Mathf.Sin(2f * Mathf.PI * 110.25f * t) + 0.08f * hiss;
            }
            AudioClip clip = AudioClip.Create("Glitch Drone", length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static Profile ProfileFor(GlitchAuraLevel level, bool reduce)
        {
            Profile p = level switch
            {
                GlitchAuraLevel.Full => new Profile
                {
                    ghostAlpha = 1f, jump = 0.12f, jumpEvery = 0.12f, slice = 1f, dropout = 0.22f, streaks = 16f,
                    blocks = 22f, embers = 4f, shards = 2f, glyphs = 1.4f, light = 2.6f, blink = 1f
                },
                GlitchAuraLevel.Lingering => new Profile
                {
                    ghostAlpha = 0.45f, jump = 0.04f, jumpEvery = 2.5f, slice = 0.15f, dropout = 0.05f, streaks = 0.5f,
                    blocks = 0.8f, embers = 0.3f, shards = 0f, glyphs = 0.25f, light = 0.5f, blink = 0f
                },
                GlitchAuraLevel.Hint => new Profile(),
                _ => new Profile()
            };
            if (reduce)
            {
                p.jump = 0f;
                p.slice = 0f;
                p.dropout = 0f;
                p.streaks = 0f;
                p.blocks = 0f;
                p.blink = 0f;
                p.embers *= 0.5f;
                p.shards *= 0.5f;
                p.glyphs *= 0.5f;
                if (level == GlitchAuraLevel.Hint) p.ghostAlpha = 0f;
            }
            return p;
        }

        // The copies rest a little either side of the character and every so often jump somewhere else for a moment.
        private void UpdateOffsets(Profile profile, bool reduce, float now)
        {
            Vector2 restRed = new Vector2(-0.025f, 0f);
            Vector2 restBlue = new Vector2(0.025f, 0f);
            if (reduce || profile.jump <= 0f)
            {
                redOffset = restRed;
                blueOffset = restBlue;
                return;
            }
            if (now >= nextJump)
            {
                nextJump = now + Random.Range(profile.jumpEvery * 0.4f, profile.jumpEvery * 1.6f);
                bool jumps = Random.value < 0.6f;
                redOffset = restRed + (jumps ? Random.insideUnitCircle * profile.jump : Vector2.zero);
                blueOffset = restBlue + (jumps ? Random.insideUnitCircle * profile.jump : Vector2.zero);
            }
        }

        private void DrawGhosts(Camera camera, Profile profile, bool reduce, float now)
        {
            if (profile.ghostAlpha <= 0f || !EnsureGhostMaterials()) return;
            Vector3 right = camera.transform.right;
            Vector3 up = Vector3.up;
            Configure(redGhost, Red, profile, right * redOffset.x + up * redOffset.y, reduce, now, 1.7f);
            Configure(blueGhost, Blue, profile, right * blueOffset.x + up * blueOffset.y, reduce, now, 5.3f);
            RenderParams red = new RenderParams(redGhost) { layer = gameObject.layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            RenderParams blue = new RenderParams(blueGhost) { layer = gameObject.layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };

            foreach (Source source in sources)
            {
                if (source.renderer == null || !source.renderer.gameObject.activeInHierarchy) continue;
                Matrix4x4 matrix;
                if (source.skinned != null)
                {
                    // Baked with its transform's scale, so drawn with position and rotation only.
                    source.skinned.BakeMesh(source.mesh, true);
                    Transform t = source.skinned.transform;
                    matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                }
                else matrix = source.renderer.transform.localToWorldMatrix;
                for (int i = 0; i < source.mesh.subMeshCount; i++)
                {
                    Graphics.RenderMesh(red, source.mesh, i, matrix);
                    Graphics.RenderMesh(blue, source.mesh, i, matrix);
                }
            }
        }

        private static void Configure(Material material, Color color, Profile profile, Vector3 offset, bool reduce, float now, float seed)
        {
            material.SetColor("_Color", new Color(color.r, color.g, color.b, profile.ghostAlpha));
            material.SetVector("_GhostOffset", offset);
            material.SetFloat("_Slice", profile.slice);
            material.SetFloat("_Dropout", profile.dropout);
            material.SetFloat("_GlitchTime", reduce ? 0f : now);
            material.SetFloat("_Seed", seed);
        }

        private bool EnsureGhostMaterials()
        {
            if (redGhost != null && blueGhost != null) return true;
            Shader shader = Resources.Load<Shader>("GlitchGhost");
            if (shader == null || !shader.isSupported)
            {
                if (!warnedShader) Debug.LogWarning("The glitch aura's ghost shader is missing or unsupported; the aura draws without its split image.", this);
                warnedShader = true;
                return false;
            }
            redGhost = new Material(shader) { name = "Glitch Ghost (Red)" };
            blueGhost = new Material(shader) { name = "Glitch Ghost (Blue)" };
            return true;
        }

        // At full strength the character blinks out for a frame or two now and then, leaving only the copies.
        private void UpdateBlink(Profile profile, float now)
        {
            if (profile.blink <= 0f) { SetHidden(false); return; }
            if (now >= nextBlink)
            {
                blinkUntil = now + Random.Range(0.04f, 0.09f);
                nextBlink = now + Random.Range(0.6f, 2f);
            }
            SetHidden(now < blinkUntil);
        }

        private void SetHidden(bool hide)
        {
            if (hidden == hide) return;
            hidden = hide;
            foreach (Source source in sources) if (source.renderer != null) source.renderer.forceRenderingOff = hide;
        }

        private void UpdateLight(Bounds bounds, Profile profile, bool reduce, float now)
        {
            if (profile.light <= 0f)
            {
                if (glow != null) glow.enabled = false;
                return;
            }
            if (glow == null)
            {
                glow = new GameObject("Glitch Light").AddComponent<Light>();
                glow.transform.SetParent(fxRoot, false);
                glow.type = LightType.Point;
                glow.color = Violet;
                glow.range = 5f;
                glow.shadows = LightShadows.None;
            }
            glow.enabled = true;
            glow.transform.position = bounds.center + Vector3.up * 0.4f;
            float flicker = reduce ? 1f : 0.75f + 0.25f * Mathf.PerlinNoise(now * 9f, 0.3f) + (Random.value < 0.03f ? -0.6f : 0f);
            glow.intensity = profile.light * flicker;
        }

        private void Spawn(Camera camera, Profile profile, float dt)
        {
            spawnStreak += profile.streaks * dt;
            spawnBlock += profile.blocks * dt;
            spawnEmber += profile.embers * dt;
            spawnShard += profile.shards * dt;
            spawnGlyph += profile.glyphs * dt;
            for (; spawnStreak >= 1f; spawnStreak--) SpawnStreak(camera);
            for (; spawnBlock >= 1f; spawnBlock--) SpawnBlock(camera);
            for (; spawnEmber >= 1f; spawnEmber--) SpawnRising(camera, Kind.Ember);
            for (; spawnShard >= 1f; spawnShard--) SpawnRising(camera, Kind.ShardBit);
            for (; spawnGlyph >= 1f; spawnGlyph--) SpawnGlyph(camera);
        }

        private void SpawnBlockOrStreak(Camera camera, bool block)
        {
            if (camera == null) return;
            if (block) SpawnBlock(camera);
            else SpawnStreak(camera);
        }

        // A point on the plane through the character facing the camera, nudged towards the camera so it is not inside them.
        private bool TryPlace(Camera camera, float across, float height, out Vector3 position)
        {
            position = default;
            if (camera == null || !TryGetBounds(out Bounds bounds)) return false;
            float halfWidth = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.8f;
            position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) + camera.transform.right * (across * halfWidth) +
                       Vector3.up * (height * bounds.size.y) - camera.transform.forward * 0.3f;
            return true;
        }

        private void SpawnStreak(Camera camera)
        {
            if (!TryPlace(camera, Random.Range(-1.3f, 1.3f), Random.Range(0.55f, 1.15f), out Vector3 top)) return;
            Bit bit = Take(Kind.Streak);
            if (bit == null) return;
            bit.position = top;
            bit.velocity = Vector3.down * Random.Range(1.5f, 3.2f);
            bit.size = Random.Range(0.2f, 0.7f);
            bit.life = Random.Range(0.15f, 0.4f);
            float pick = Random.value;
            bit.color = pick < 0.5f ? White : pick < 0.75f ? Blue : Red;
        }

        private void SpawnBlock(Camera camera)
        {
            // Around the outline rather than over the face: mostly beyond either side, sometimes just inside.
            float side = Random.value < 0.5f ? -1f : 1f;
            if (!TryPlace(camera, side * Random.Range(0.7f, 1.5f), Random.Range(0.05f, 1.05f), out Vector3 position)) return;
            Bit bit = Take(Kind.Block);
            if (bit == null) return;
            const float grid = 0.06f;
            position.x = Mathf.Round(position.x / grid) * grid;
            position.y = Mathf.Round(position.y / grid) * grid;
            position.z = Mathf.Round(position.z / grid) * grid;
            bit.position = position;
            bit.velocity = Vector3.zero;
            bit.size = grid * Random.Range(1, 3);
            bit.life = Random.Range(0.05f, 0.2f);
            float pick = Random.value;
            bit.color = pick < 0.4f ? White : pick < 0.65f ? Cyan : pick < 0.83f ? Blue : Red;
        }

        private void SpawnRising(Camera camera, Kind kind)
        {
            if (!TryPlace(camera, Random.Range(-1.4f, 1.4f), Random.Range(0f, 0.6f), out Vector3 position)) return;
            Bit bit = Take(kind);
            if (bit == null) return;
            bit.position = position;
            bit.velocity = Vector3.up * Random.Range(0.2f, 0.45f) + camera.transform.right * Random.Range(-0.08f, 0.08f);
            bit.life = Random.Range(1.6f, 3.2f);
            bit.size = kind == Kind.Ember ? Random.Range(0.02f, 0.035f) : Random.Range(0.05f, 0.09f);
            bit.color = kind == Kind.Ember ? Gold : Shard;
            bit.direction = Random.onUnitSphere;
            bit.spin = Random.Range(-4f, 4f);
        }

        private Bit Take(Kind kind)
        {
            Bit free = null;
            foreach (Bit bit in bits)
                if (!bit.line.enabled) { free = bit; break; }
            if (free == null)
            {
                if (bits.Count >= MaxLines || !EnsureLineMaterial()) return null;
                LineRenderer line = new GameObject("Glitch Bit").AddComponent<LineRenderer>();
                line.transform.SetParent(fxRoot, false);
                line.sharedMaterial = lineMaterial;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.alignment = LineAlignment.View;
                line.numCapVertices = 0;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                free = new Bit { line = line };
                bits.Add(free);
            }
            free.kind = kind;
            free.born = Time.time;
            free.line.enabled = true;
            return free;
        }

        private void AnimateBits(Camera camera, float now, float dt)
        {
            Vector3 right = camera != null ? camera.transform.right : Vector3.right;
            foreach (Bit bit in bits)
            {
                if (!bit.line.enabled) continue;
                float age = (now - bit.born) / bit.life;
                if (age >= 1f) { bit.line.enabled = false; continue; }
                bit.position += bit.velocity * dt;
                Vector3 from, to;
                float width, alpha;
                switch (bit.kind)
                {
                    case Kind.Streak:
                        from = bit.position;
                        to = bit.position + Vector3.down * bit.size;
                        width = 0.012f + 0.012f * (1f - age);
                        alpha = 1f - age;
                        break;
                    case Kind.Block:
                        // A line as long as it is wide is a square facing the camera.
                        from = bit.position - right * (bit.size * 0.5f);
                        to = bit.position + right * (bit.size * 0.5f);
                        width = bit.size;
                        alpha = 0.9f;
                        break;
                    case Kind.ShardBit:
                        bit.direction = Quaternion.AngleAxis(bit.spin * 60f * dt, Vector3.up) * bit.direction;
                        from = bit.position;
                        to = bit.position + bit.direction * bit.size;
                        width = 0.018f;
                        alpha = 0.85f * Mathf.Sin(age * Mathf.PI);
                        break;
                    default:
                        from = bit.position;
                        to = bit.position + Vector3.up * bit.size;
                        width = bit.size;
                        alpha = Mathf.Sin(age * Mathf.PI) * (0.7f + 0.3f * Mathf.Sin(now * 20f + bit.born * 7f));
                        break;
                }
                bit.line.SetPosition(0, from);
                bit.line.SetPosition(1, to);
                bit.line.startWidth = bit.line.endWidth = width;
                Color color = new Color(bit.color.r, bit.color.g, bit.color.b, alpha);
                bit.line.startColor = bit.line.endColor = color;
            }
        }

        private void SpawnGlyph(Camera camera)
        {
            Font font = WingdingsText.Font;
            if (font == null || !TryPlace(camera, Random.Range(-1.6f, 1.6f), Random.Range(0.15f, 1.1f), out Vector3 position)) return;
            Glyph glyph = null;
            foreach (Glyph g in glyphs)
                if (!g.text.gameObject.activeSelf) { glyph = g; break; }
            if (glyph == null)
            {
                if (glyphs.Count >= MaxGlyphs) return;
                GameObject host = new GameObject("Glitch Glyph");
                host.transform.SetParent(fxRoot, false);
                TextMesh text = host.AddComponent<TextMesh>();
                text.font = font;
                text.fontSize = 64;
                text.characterSize = 0.012f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                MeshRenderer renderer = host.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = font.material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                glyph = new Glyph { text = text };
                glyphs.Add(glyph);
            }
            char letter = GlyphLetters[Random.Range(0, GlyphLetters.Length)];
            glyph.text.text = WingdingsText.ForFont(font, letter.ToString());
            glyph.text.gameObject.SetActive(true);
            glyph.born = Time.time;
            glyph.life = Random.Range(2f, 4f);
            glyph.position = position;
            glyph.velocity = Vector3.up * Random.Range(0.08f, 0.18f) + camera.transform.right * Random.Range(-0.06f, 0.06f);
        }

        private void AnimateGlyphs(Camera camera, float now, float dt)
        {
            foreach (Glyph glyph in glyphs)
            {
                if (!glyph.text.gameObject.activeSelf) continue;
                float age = (now - glyph.born) / glyph.life;
                if (age >= 1f || camera == null) { glyph.text.gameObject.SetActive(false); continue; }
                glyph.position += glyph.velocity * dt;
                glyph.text.transform.position = glyph.position;
                glyph.text.transform.rotation = camera.transform.rotation;
                glyph.text.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f * Mathf.Sin(age * Mathf.PI));
            }
        }

        // Sprites/Default is unlit and alpha blended, like the spell effects, so dark shards show as well as light.
        private bool EnsureLineMaterial()
        {
            if (lineMaterial != null) return true;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return false;
            lineMaterial = new Material(shader) { name = "Glitch Aura Bits" };
            return true;
        }

        private bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Source source in sources)
            {
                if (source.renderer == null || !source.renderer.gameObject.activeInHierarchy) continue;
                if (!any) { bounds = source.renderer.bounds; any = true; }
                else bounds.Encapsulate(source.renderer.bounds);
            }
            return any;
        }
    }

    /// <summary>
    /// An editor-only preview of <see cref="GlitchAura"/> on the receptionist Marigold, at <see cref="PreviewLevel"/>,
    /// set from <c>Lumenfall &gt; Secrets &gt; Glitch Aura</c>. The story's own aura (the key glitch in
    /// <c>chapter1_room_ready</c>) does not depend on it, and builds never show the preview.
    /// </summary>
    public sealed class GlitchAuraPreview : MonoBehaviour
    {
        public const string PreviewPrefKey = "lumenfall.debug.glitch-aura";
        public const string TargetNpcId = SettlingInScene.ReceptionistNpcId;

        private static GlitchAuraPreview instance;
        private float nextScan;

        /// <summary>The level the preview shows, kept in the editor's PlayerPrefs. Always Off in a build.</summary>
        public static GlitchAuraLevel PreviewLevel
        {
            get
            {
#if UNITY_EDITOR
                if (cachedLevel < 0) cachedLevel = Mathf.Clamp(PlayerPrefs.GetInt(PreviewPrefKey, 0), 0, (int)GlitchAuraLevel.Full);
                return (GlitchAuraLevel)cachedLevel;
#else
                return GlitchAuraLevel.Off;
#endif
            }
            set
            {
#if UNITY_EDITOR
                cachedLevel = (int)value;
                PlayerPrefs.SetInt(PreviewPrefKey, (int)value);
                PlayerPrefs.Save();
                if (instance != null) instance.nextScan = 0f;
#endif
            }
        }

        private static int cachedLevel = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            cachedLevel = -1;
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Glitch Aura Preview]");
            instance = host.AddComponent<GlitchAuraPreview>();
            DontDestroyOnLoad(host);
        }
#endif

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.25f;
            if (PreviewLevel == GlitchAuraLevel.Off) return;
            GameObject area = DataDrivenWorldLoader.Instance != null ? DataDrivenWorldLoader.Instance.CurrentAreaRoot : null;
            if (area == null) return;
            // The aura reads the preview level itself; this only makes sure Marigold has one.
            foreach (DynamicNpc npc in area.GetComponentsInChildren<DynamicNpc>(true))
                if (npc.Definition != null && npc.Definition.id == TargetNpcId && npc.GetComponent<GlitchAura>() == null)
                    npc.gameObject.AddComponent<GlitchAura>();
        }
    }
}
