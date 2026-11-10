using System.Collections.Generic;
using Lumenfall.Audio;
using Lumenfall.Controls;
using Lumenfall.Cutscenes;
using Lumenfall.Startup;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Lumenfall.Story
{
    /// <summary>
    /// The 3D prologue from the game design document, which opens New Game (the <c>prologue</c> step of the opening
    /// cutscene) and can also be previewed from the editor (Lumenfall > Story > Preview Prologue). A tired student
    /// drives home at night while their phone buzzes with uni, work and the group chat. They swerve onto a dirt road,
    /// throw the phone into a field and scream, then lean on the car and look up at the stars. One star grows, dives at
    /// them, and the screen goes white, then dark.
    /// <para>
    /// It plays over whatever is on screen, including the title menu: its set (<see cref="PrologueSet"/>) is far from the
    /// game world, its camera renders into a texture drawn in front of every other overlay, it runs on unscaled time
    /// and it hushes the music. While it plays, the scene's own lights are switched off and the ambient light and fog
    /// are set for night; all of it is put back when it ends. In a game, Esc twice skips it; as a preview, Esc stops it.
    /// It saves nothing and changes no game state.
    /// The timeline is <see cref="PrologueScript"/>.
    /// </para>
    /// </summary>
    public sealed class PrologueScene : MonoBehaviour
    {
        private const float ToastSlideSeconds = 0.28f;
        private const int MaxToasts = 5;
        private static readonly Color Cream = new Color(1f, 0.96f, 0.88f);
        private static readonly Color Gold = new Color(0.96f, 0.8f, 0.45f);
        private static readonly Color Ink = new Color(0.07f, 0.05f, 0.12f);
        private static readonly Color StarGold = new Color(1f, 0.84f, 0.48f);
        private static readonly Color NightAmbient = new Color(0.09f, 0.1f, 0.17f);
        private static readonly Color NightFog = new Color(0.03f, 0.035f, 0.07f);

        private const float SkipConfirmSeconds = 2.5f;

        private static PrologueScene instance;

        /// <summary>
        /// Raised just before the prologue changes the lights, so anything else that has changed them (the dark forest)
        /// can put them back first and the prologue restores the real day when it ends.
        /// </summary>
        internal static event System.Action Starting;

        private bool preview;
        private float skipArmedUntil = float.NegativeInfinity;

        private PrologueSet set;
        private Camera view;
        private RenderTexture picture;
        private float started, previousTime = -1f;
        private bool calm;
        private readonly List<AudioClip> clips = new List<AudioClip>();
        private AudioSource effects, windLoop, engineLoop, cricketLoop;
        private AudioClip buzz, screech, engineOff, door, chime, whoosh, thud, starFall, starImpact;
        // Made in Begin: Unity refuses to create one in a MonoBehaviour's field initializer.
        private MaterialPropertyBlock block;
        private readonly List<(float time, Vector3 position)> trailPoints = new List<(float, Vector3)>();
        private readonly List<Mote> motes = new List<Mote>();
        private readonly System.Random random = new System.Random(1729);
        private float nextMote;
        private string animatorState;
        private float lastBuzz = -10f;
        private Vector3? releasePoint;
        private bool shutDown;

        // Restored when the prologue ends.
        private readonly List<Light> silenced = new List<Light>();
        private AmbientMode ambientMode;
        private Color ambientLight;
        private SphericalHarmonicsL2 ambientProbe;
        private bool fog;
        private FogMode fogMode;
        private Color fogColour;
        private float fogDensity, reflectionIntensity;
        private Light sun;
        private bool nightSet;
        private bool musicWasHushed;

        private Texture2D toastTexture, iconTexture, pixel;
        private GUIStyle senderStyle, bodyStyle, timeStyle, thoughtStyle, shoutStyle, clockStyle, hintStyle;
        private int styledHeight;

        private sealed class Mote
        {
            public Transform Transform;
            public Renderer Renderer;
            public Vector3 Velocity;
            public float Born, Life, Size;
            public Color Colour;
        }

        /// <summary>True while the prologue is playing.</summary>
        public static bool IsPlaying => instance != null;

        /// <summary>
        /// Plays the prologue from the start, replacing one already playing. False if it cannot be built. A preview
        /// stops on Esc and says it is a preview; in a game it is skipped with Esc twice, like a cutscene.
        /// </summary>
        public static bool Play(bool preview = true)
        {
            if (!Application.isPlaying) return false;
            Stop();
            if (!CutsceneArt.Ensure())
            {
                Debug.LogWarning("The prologue needs the Sprites/Default shader for its glows and could not start.");
                return false;
            }
            GameObject host = new GameObject("[Prologue]");
            instance = host.AddComponent<PrologueScene>();
            instance.preview = preview;
            Starting?.Invoke();
            try
            {
                instance.Begin();
            }
            catch (System.Exception exception)
            {
                // One clear error and a clean stop, rather than a half-built scene failing every frame.
                Debug.LogException(exception);
                Stop();
                return false;
            }
            return true;
        }

        /// <summary>Ends the prologue at once and puts the lights, fog and music back.</summary>
        public static void Stop()
        {
            if (instance == null) return;
            // Put back now rather than when the object is destroyed at the end of the frame, so a prologue started
            // straight after this one finds the day it should restore, not this one's night.
            instance.Shutdown();
            Destroy(instance.gameObject);
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        private void Begin()
        {
            useGUILayout = false;
            block = new MaterialPropertyBlock();
            started = Time.unscaledTime;
            calm = GameSettingsPanel.ReduceMotion;
            set = new PrologueSet();
            SetNight();
            BuildCamera();
            BuildAudio();
            musicWasHushed = CutscenePlayer.MusicHushed;
            CutscenePlayer.MusicHushed = true;
            Pose(0f);
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            Shutdown();
        }

        // Safe to call twice: Stop calls it at once, and OnDestroy again when the object goes.
        private void Shutdown()
        {
            if (shutDown) return;
            shutDown = true;
            RestoreDay();
            CutscenePlayer.MusicHushed = musicWasHushed;
            set?.Dispose();
            set = null;
            foreach (Mote mote in motes) if (mote.Transform != null) Destroy(mote.Transform.gameObject);
            motes.Clear();
            if (view != null) { view.targetTexture = null; view.enabled = false; }
            if (picture != null) { picture.Release(); Destroy(picture); }
            foreach (AudioClip clip in clips) if (clip != null) Destroy(clip);
            foreach (Texture2D texture in new[] { toastTexture, iconTexture, pixel }) if (texture != null) Destroy(texture);
        }

        // ---- Night and day ----

        private void SetNight()
        {
            // Everything is recorded before anything changes, so a stop at any point puts back exactly what was there.
            ambientMode = RenderSettings.ambientMode;
            ambientLight = RenderSettings.ambientLight;
            ambientProbe = RenderSettings.ambientProbe;
            fog = RenderSettings.fog;
            fogMode = RenderSettings.fogMode;
            fogColour = RenderSettings.fogColor;
            fogDensity = RenderSettings.fogDensity;
            reflectionIntensity = RenderSettings.reflectionIntensity;
            sun = RenderSettings.sun;
            nightSet = true;

            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (!light.enabled || light.transform.IsChildOf(set.Root)) continue;
                light.enabled = false;
                silenced.Add(light);
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = NightAmbient;
            SphericalHarmonicsL2 probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(NightAmbient);
            RenderSettings.ambientProbe = probe;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = NightFog;
            RenderSettings.fogDensity = 0.0065f;
            RenderSettings.reflectionIntensity = 0.1f;
            RenderSettings.sun = set.Moon;
        }

        private void RestoreDay()
        {
            foreach (Light light in silenced) if (light != null) light.enabled = true;
            silenced.Clear();
            // Nothing was changed if the night was never set.
            if (!nightSet) return;
            nightSet = false;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientProbe = ambientProbe;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogColor = fogColour;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            RenderSettings.sun = sun;
        }

        private void BuildCamera()
        {
            GameObject host = new GameObject("Prologue Camera");
            host.transform.SetParent(transform, false);
            view = host.AddComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(0.03f, 0.035f, 0.08f);
            view.cullingMask = 1 << PrologueSet.Layer;
            view.nearClipPlane = 0.05f;
            view.farClipPlane = 1000f;
            view.fieldOfView = 40f;
            view.allowHDR = false;
            view.depth = 100f;
            FitPicture();
        }

        // The camera draws into a screen-sized texture, which OnGUI puts in front of every other overlay.
        private void FitPicture()
        {
            int width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
            if (picture != null && picture.width == width && picture.height == height) return;
            if (picture != null) { view.targetTexture = null; picture.Release(); Destroy(picture); }
            picture = new RenderTexture(width, height, 24) { name = "Prologue", antiAliasing = 4 };
            view.targetTexture = picture;
            view.aspect = width / (float)height;
        }

        // ---- Audio ----

        private void BuildAudio()
        {
            effects = Source(false);
            windLoop = Source(true);
            engineLoop = Source(true);
            cricketLoop = Source(true);
            windLoop.clip = Keep(PrologueAudio.Wind());
            engineLoop.clip = Keep(PrologueAudio.Engine());
            cricketLoop.clip = Keep(PrologueAudio.Crickets());
            buzz = Keep(PrologueAudio.Buzz());
            screech = Keep(PrologueAudio.Screech());
            engineOff = Keep(PrologueAudio.EngineOff());
            door = Keep(PrologueAudio.Door());
            chime = Keep(PrologueAudio.Chime());
            whoosh = Keep(PrologueAudio.Whoosh());
            thud = Keep(PrologueAudio.Thud());
            starFall = Keep(FallingStarEffect.FallClip(PrologueScript.ImpactAt - PrologueScript.DiveAt, PrologueScript.DiveAt - PrologueScript.TwinkleAt));
            starImpact = Keep(FallingStarEffect.ImpactClip());
            foreach (AudioSource loop in new[] { windLoop, engineLoop, cricketLoop })
            {
                loop.volume = 0f;
                AudioPlayback.Play(loop);
            }
        }

        private AudioSource Source(bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }

        private AudioClip Keep(AudioClip clip)
        {
            clips.Add(clip);
            return clip;
        }

        private void Cue(AudioClip clip, float volume) => AudioPlayback.PlayOneShot(effects, clip, volume * AudioMixSettings.Sfx);

        // ---- Playing ----

        private void Update()
        {
            if (set == null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (preview || Time.unscaledTime <= skipArmedUntil)
                {
                    Stop();
                    return;
                }
                skipArmedUntil = Time.unscaledTime + SkipConfirmSeconds;
            }
            float t = Time.unscaledTime - started;
            if (t >= PrologueScript.Duration)
            {
                Stop();
                return;
            }
            FitPicture();
            Pose(t);
            PlayCues(previousTime, t);
            previousTime = t;
        }

        // After the animator has posed the student: their head is turned, the phone follows their hand, the star dives at
        // their head, and the camera frames the result.
        private void LateUpdate()
        {
            float t = Time.unscaledTime - started;
            if (set == null || t >= PrologueScript.Duration) return;
            Sit(t);
            LookUp(t);
            PosePhone(t);
            PoseStar(t);
            FrameShot(t);
            set.Sky.position = view.transform.position;
            Billboards(t);
        }

        private void Pose(float t)
        {
            DriveCar(t);
            PoseStudent(t);
            set.Twinkle(t);
            Loops(t);
            view.enabled = t < PrologueScript.InkFrom;
        }

        private void DriveCar(float t)
        {
            float distance = PrologueScript.CarDistance(t);
            Vector3 position = PrologueScript.CarPosition(distance, out Vector3 forward);
            float speed = PrologueScript.CarSpeed(t);
            float turn = PrologueScript.TurnAmount(distance);
            // It rolls out of the bend (turning left, so its right side dips) and dips its nose under braking, and the
            // road shakes it a little at speed.
            float roll = -turn * Mathf.Clamp01(speed / PrologueScript.CruiseSpeed) * 5f;
            float pitch = t > PrologueScript.SwerveAt && t < PrologueScript.StopAt ? 2.2f : 0f;
            float bounce = calm ? 0f : Mathf.Sin(t * 17f) * Mathf.Sin(t * 5.3f) * 0.012f * Mathf.Clamp01(speed / 4f);
            set.Car.localPosition = position + Vector3.up * (0.01f + bounce);
            set.Car.localRotation = Quaternion.LookRotation(forward) * Quaternion.Euler(pitch, 0f, roll);
            float spin = distance / 0.33f * Mathf.Rad2Deg;
            foreach (Transform wheel in set.Wheels) wheel.localRotation = Quaternion.Euler(spin, 0f, 0f);
            // The steering wheel yanked round to the left through the bend.
            set.SteeringWheel.localRotation = Quaternion.Euler(-65f, 0f, 0f) * Quaternion.Euler(0f, -110f * turn, 0f);

            float door = PrologueScript.Ease(t, PrologueScript.DoorOpensAt, PrologueScript.DoorOpensAt + 0.6f);
            set.DriverDoor.localRotation = Quaternion.Euler(0f, -62f * door, 0f);
            set.CabinLight.intensity = 0.9f * door;
            bool braking = t > PrologueScript.SwerveAt && t < PrologueScript.StopAt + 0.4f;
            set.TaillightMaterial.SetColor("_EmissionColor", new Color(0.85f, 0.08f, 0.06f) * (braking ? 4f : 1.2f));
            set.TailGlow.intensity = braking ? 1.6f : 0.5f;
        }

        private void PoseStudent(float t)
        {
            Transform student = set.Student;
            Vector3 rest = PrologueSet.RestPosition(out Vector3 forward, out Vector3 right);
            // Just outside the open door, which swings out ahead of them.
            Vector3 stepOut = rest + right * 1.4f - forward * 0.1f;
            Vector3 throwSpot = StudentThrowSpot(rest, forward, right);
            Vector3 lean = LeanSpot(rest, forward, right);
            Vector3 target = ThrowTarget(rest, forward, right);
            // Shuffled across the cushion towards the door as they turn.
            Vector3 turnedSeat = set.SeatPosition + Vector3.right * 0.15f;

            if (t < PrologueScript.HopAt)
            {
                // Seated (PrologueScene.Sit bends their legs and arms), then turning to the door.
                if (student.parent != set.Car) student.SetParent(set.Car, false);
                float turn = PrologueScript.Ease(t, PrologueScript.StepOutAt, PrologueScript.HopAt);
                student.localPosition = Vector3.Lerp(set.SeatPosition, turnedSeat, turn);
                student.localRotation = Quaternion.Euler(0f, 90f * turn, 0f);
                Animate("Idle");
                return;
            }
            if (student.parent != set.Root) student.SetParent(set.Root, false);
            if (t < PrologueScript.WalkOutAt)
            {
                // Hopping down out of the seat and onto the dirt, legs straightening on the way.
                float hop = PrologueScript.Ease(t, PrologueScript.HopAt, PrologueScript.WalkOutAt);
                Vector3 from = set.Root.InverseTransformPoint(set.Car.TransformPoint(turnedSeat));
                student.localPosition = Vector3.Lerp(from, stepOut, hop) + Vector3.up * (0.12f * Mathf.Sin(hop * Mathf.PI));
                student.localRotation = Quaternion.LookRotation(right);
                Animate("Idle");
            }
            else if (t < PrologueScript.ArriveOutAt)
            {
                float u = Mathf.InverseLerp(PrologueScript.WalkOutAt, PrologueScript.ArriveOutAt, t);
                student.localPosition = Vector3.Lerp(stepOut, throwSpot, u);
                student.localRotation = Quaternion.LookRotation(throwSpot - stepOut);
                Animate("Walk");
            }
            else if (t < PrologueScript.WalkBackAt)
            {
                student.localPosition = throwSpot;
                Vector3 facing = Flat(target - throwSpot);
                student.localRotation = Quaternion.Slerp(Quaternion.LookRotation(throwSpot - stepOut), Quaternion.LookRotation(facing),
                    PrologueScript.Ease(t, PrologueScript.ArriveOutAt, PrologueScript.ArriveOutAt + 0.35f));
                Animate(t < PrologueScript.YellAt ? "Angry" : t < PrologueScript.SlumpAt ? "Yelling" : "Sad");
            }
            else if (t < PrologueScript.ArriveBackAt)
            {
                float u = Mathf.InverseLerp(PrologueScript.WalkBackAt, PrologueScript.ArriveBackAt, t);
                student.localPosition = Vector3.Lerp(throwSpot, lean, u);
                student.localRotation = Quaternion.LookRotation(lean - throwSpot);
                Animate("Walk");
            }
            else
            {
                // Turned away from the car and leaning back against it.
                float settle = PrologueScript.Ease(t, PrologueScript.ArriveBackAt, PrologueScript.ArriveBackAt + 0.8f);
                Quaternion facing = Quaternion.Slerp(Quaternion.LookRotation(lean - throwSpot), Quaternion.LookRotation(right), settle);
                student.localPosition = lean;
                student.localRotation = facing * Quaternion.Euler(-6f * settle, 0f, 0f);
                Animate("Idle");
            }
        }

        // How far the student is sat down (legs bent at hip and knee) and how far their hands are up on the wheel.
        private static float SitAmount(float t) =>
            1f - PrologueScript.Ease(t, PrologueScript.HopAt, Mathf.Lerp(PrologueScript.HopAt, PrologueScript.WalkOutAt, 0.7f));
        private static float GripAmount(float t) => 1f - PrologueScript.Ease(t, PrologueScript.StepOutAt - 0.2f, PrologueScript.StepOutAt + 0.15f);

        // The model has no sitting animation, so after the animator has posed it standing, its thighs swing forward and
        // its knees bend, and while driving its arms reach forward to the wheel. The hips do not move, so the seat
        // position (PrologueSet.SeatPosition) puts them on the cushion.
        private void Sit(float t)
        {
            float sit = SitAmount(t), grip = GripAmount(t);
            Vector3 axis = set.Student.right;
            if (sit > 0f)
            {
                foreach (Transform thigh in set.Thighs) thigh.rotation = Quaternion.AngleAxis(-85f * sit, axis) * thigh.rotation;
                foreach (Transform shin in set.Shins) shin.rotation = Quaternion.AngleAxis(80f * sit, axis) * shin.rotation;
            }
            if (grip > 0f)
            {
                foreach (Transform arm in set.UpperArms) arm.rotation = Quaternion.AngleAxis(-50f * grip, axis) * arm.rotation;
                foreach (Transform arm in set.Forearms) arm.rotation = Quaternion.AngleAxis(-30f * grip, axis) * arm.rotation;
            }
        }

        // Out past the open door's edge, so the walk there and back never passes through it.
        private static Vector3 StudentThrowSpot(Vector3 rest, Vector3 forward, Vector3 right) => rest + right * 3f - forward * 0.4f;
        // Against the back of the car, behind the cabin, where the student's head is clear of the roof and pillars.
        private static Vector3 LeanSpot(Vector3 rest, Vector3 forward, Vector3 right) => rest + right * 1.5f - forward * 1.35f;
        private static Vector3 ThrowTarget(Vector3 rest, Vector3 forward, Vector3 right) => rest + right * 6.5f + forward * 7.5f;

        // Where the star waits in the sky before it dives: high above the far side of the car.
        private static Vector3 StarSkyPoint(Vector3 rest, Vector3 forward, Vector3 right) =>
            LeanSpot(rest, forward, right) - forward * 30f - right * 10f + Vector3.up * 45f;

        private void Animate(string state)
        {
            Animator animator = set.StudentAnimator;
            if (animator == null || state == animatorState || animator.runtimeAnimatorController == null) return;
            animatorState = state;
            int hash = Animator.StringToHash(state);
            if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, 0.2f);
        }

        // As they lean on the car, the student's head tips back to look at the sky, further still as the star comes.
        private void LookUp(float t)
        {
            if (set.Head == null || t < PrologueScript.ArriveBackAt) return;
            float angle = Mathf.Lerp(0f, 32f, PrologueScript.Ease(t, PrologueScript.LookUpFrom, PrologueScript.LookUpTo)) +
                          Mathf.Lerp(0f, 10f, PrologueScript.Ease(t, PrologueScript.DiveAt, PrologueScript.ImpactAt));
            set.Head.rotation = Quaternion.AngleAxis(-angle, set.Student.right) * set.Head.rotation;
        }

        private void PosePhone(float t)
        {
            Transform phone = set.Phone;
            Vector3 rest = PrologueSet.RestPosition(out Vector3 forward, out Vector3 right);
            Vector3 target = set.World(ThrowTarget(rest, forward, right) + Vector3.up * 0.04f);
            float sinceBuzz = t - lastBuzz;
            float flare = sinceBuzz < 0.6f ? 1f - sinceBuzz / 0.6f : 0f;
            float screen = 1f + flare * 1.6f;

            if (t < PrologueScript.StepOutAt + 0.3f)
            {
                if (phone.parent != set.PhoneMount) phone.SetParent(set.PhoneMount, false);
                phone.localPosition = Vector3.zero;
                phone.localRotation = Quaternion.identity;
            }
            else if (t < PrologueScript.ThrowAt)
            {
                // In the student's hand, grabbed on the way out.
                Transform hand = set.RightHand;
                if (phone.parent != set.Root) phone.SetParent(set.Root, true);
                phone.position = hand != null ? hand.position + hand.up * 0.06f : set.Student.position + Vector3.up * 0.9f + set.Student.right * 0.3f;
                phone.rotation = set.Student.rotation * Quaternion.Euler(-60f, 180f, 0f);
            }
            else
            {
                // Flung in a high arc into the lit field, tumbling, then lying face up in the grass, still glowing.
                float u = Mathf.Clamp01((t - PrologueScript.ThrowAt) / PrologueScript.ThrowSeconds);
                if (phone.parent != set.Root) phone.SetParent(set.Root, true);
                // It leaves from wherever the hand last held it.
                if (!releasePoint.HasValue) releasePoint = phone.position;
                Vector3 from = releasePoint.Value;
                Vector3 position = Vector3.Lerp(from, target, u) + Vector3.up * (4f * 2.6f * u * (1f - u));
                phone.position = position;
                phone.rotation = u < 1f ? Quaternion.Euler(u * 900f, u * 400f, 0f) : Quaternion.Euler(-90f, 30f, 0f);
                screen *= u < 1f ? 1f : Mathf.Lerp(1f, 0.45f, PrologueScript.Ease(t, PrologueScript.ThrowAt + PrologueScript.ThrowSeconds, PrologueScript.ArriveBackAt));
            }
            set.PhoneScreen.SetColor("_EmissionColor", new Color(0.68f, 0.8f, 1f) * screen);
            set.PhoneLight.intensity = 0.9f * screen;
            set.PhoneLight.range = t < PrologueScript.StepOutAt ? 1.5f : 2.4f;
        }

        private void PoseStar(float t)
        {
            GameObject star = set.StarCore.parent.gameObject;
            bool shown = t >= PrologueScript.TwinkleAt && t < PrologueScript.ImpactAt + 0.2f;
            if (star.activeSelf != shown) star.SetActive(shown);
            if (!shown) return;

            Vector3 rest = PrologueSet.RestPosition(out Vector3 forward, out Vector3 right);
            Vector3 sky = set.World(StarSkyPoint(rest, forward, right));
            Vector3 head = set.Head != null ? set.Head.position + Vector3.up * 0.1f : set.World(LeanSpot(rest, forward, right) + Vector3.up * 1.3f);
            Vector3 control = Vector3.Lerp(sky, head, 0.45f) + set.Root.TransformDirection(right * 6f + Vector3.up * 4f);
            Vector3 position;
            float core, halo, light;
            if (t < PrologueScript.DiveAt)
            {
                // Brightening and twinkling, almost still.
                float grow = PrologueScript.Ease(t, PrologueScript.TwinkleAt, PrologueScript.DiveAt);
                float flicker = 0.75f + 0.25f * Mathf.Sin(t * 17f) * Mathf.Sin(t * 5.3f);
                position = sky;
                core = Mathf.Lerp(0.4f, 1.6f, grow) * flicker;
                halo = Mathf.Lerp(1.5f, 5f, grow) * flicker;
                light = 0f;
            }
            else
            {
                // Accelerating the whole way down, as if pulled in.
                float u = Mathf.Pow(Mathf.Clamp01((t - PrologueScript.DiveAt) / (PrologueScript.ImpactAt - PrologueScript.DiveAt)), 2.2f);
                position = Bezier(sky, control, head, u);
                core = Mathf.Lerp(1.8f, 1.2f, u);
                halo = Mathf.Lerp(5f, 7f, u);
                light = Mathf.Lerp(0.5f, 14f, u);
                set.StarLight.range = Mathf.Lerp(8f, 24f, u);
                if (Time.unscaledTime >= nextMote)
                {
                    nextMote = Time.unscaledTime + 0.02f;
                    SpawnMote(position, new Vector3(Range(-0.4f, 0.4f), Range(-0.6f, 0.1f), Range(-0.4f, 0.4f)), Range(0.5f, 0.9f), Range(0.15f, 0.35f));
                }
            }
            set.StarCore.position = position;
            set.StarHalo.position = position;
            set.StarLight.transform.position = position;
            set.StarCore.localScale = Vector3.one * core;
            set.StarHalo.localScale = Vector3.one * halo;
            set.StarLight.intensity = light;
            CutsceneArt.Tint(set.StarCoreRenderer, block, new Color(1f, 0.97f, 0.88f), 1f);
            CutsceneArt.Tint(set.StarHaloRenderer, block, StarGold, 0.75f);
            UpdateTrail(t, position);
            TickMotes();
        }

        // The trail is drawn from recent positions on unscaled time, so it fades even while the title menu pauses the game.
        private void UpdateTrail(float t, Vector3 position)
        {
            LineRenderer trail = set.StarTrail;
            float now = Time.unscaledTime;
            if (t >= PrologueScript.DiveAt) trailPoints.Add((now, position));
            trailPoints.RemoveAll(point => now - point.time > 0.6f);
            trail.positionCount = trailPoints.Count;
            for (int i = 0; i < trailPoints.Count; i++) trail.SetPosition(i, trailPoints[i].position);
        }

        private void SpawnMote(Vector3 position, Vector3 velocity, float life, float size)
        {
            Transform mote = CutsceneArt.Quad(set.Root, "Star Mote", CutsceneArt.GlowMaterial, out Renderer moteRenderer);
            mote.gameObject.layer = PrologueSet.Layer;
            mote.position = position;
            mote.localScale = Vector3.one * size;
            Color colour = Range(0f, 1f) < 0.6f ? StarGold : Cream;
            motes.Add(new Mote { Transform = mote, Renderer = moteRenderer, Velocity = velocity, Born = Time.unscaledTime, Life = life, Size = size, Colour = colour });
        }

        private void TickMotes()
        {
            float now = Time.unscaledTime, deltaTime = Time.unscaledDeltaTime;
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                Mote mote = motes[i];
                float age = (now - mote.Born) / mote.Life;
                if (age >= 1f || mote.Transform == null)
                {
                    if (mote.Transform != null) Destroy(mote.Transform.gameObject);
                    motes.RemoveAt(i);
                    continue;
                }
                mote.Velocity *= Mathf.Exp(-2.5f * deltaTime);
                mote.Velocity += Vector3.down * 1.2f * deltaTime;
                mote.Transform.position += mote.Velocity * deltaTime;
                mote.Transform.localScale = Vector3.one * mote.Size * (1f - age * 0.6f);
                CutsceneArt.Tint(mote.Renderer, block, mote.Colour, 1f - age * age);
            }
        }

        private void Billboards(float t)
        {
            Quaternion facing = view.transform.rotation;
            set.StarCore.rotation = facing * Quaternion.Euler(0f, 0f, t * 40f);
            set.StarHalo.rotation = facing;
            foreach (Mote mote in motes) if (mote.Transform != null) mote.Transform.rotation = facing;
        }

        private void Loops(float t)
        {
            float speed = PrologueScript.CarSpeed(t);
            bool inside = PrologueScript.ShotAt(t, out _, out _).Shot == PrologueScript.Shot.Inside;
            float running = t < PrologueScript.StopAt ? 1f : 0f;
            float fadeIn = PrologueScript.Ease(t, 0f, PrologueScript.FadeInSeconds);
            float ended = t < PrologueScript.ImpactAt ? 1f : 0f;
            engineLoop.pitch = Mathf.Lerp(0.62f, 1f, speed / PrologueScript.CruiseSpeed);
            engineLoop.volume = running * fadeIn * (inside ? 0.55f : 0.32f) * AudioMixSettings.Sfx;
            windLoop.volume = fadeIn * ended * (inside ? 0.08f : 0.22f) * AudioMixSettings.Ambience;
            float crickets = PrologueScript.Ease(t, PrologueScript.StopAt, PrologueScript.StopAt + 2.5f) *
                             Mathf.Lerp(1f, 1.4f, PrologueScript.Ease(t, PrologueScript.ArriveBackAt, 33f)) *
                             Mathf.Lerp(1f, 0.3f, PrologueScript.Ease(t, PrologueScript.DiveAt, PrologueScript.ImpactAt - 1f));
            cricketLoop.volume = crickets * ended * 0.18f * AudioMixSettings.Ambience;
        }

        private void PlayCues(float from, float to)
        {
            foreach (PrologueScript.Toast toast in PrologueScript.Toasts)
                if (Crossed(toast.At, from, to))
                {
                    lastBuzz = toast.At;
                    Cue(buzz, 0.45f);
                }
            if (Crossed(PrologueScript.SwerveAt + 0.05f, from, to)) Cue(screech, 0.6f);
            if (Crossed(PrologueScript.StopAt, from, to)) Cue(engineOff, 0.6f);
            if (Crossed(PrologueScript.DoorOpensAt, from, to)) Cue(door, 0.6f);
            foreach (float ding in new[] { 19.9f, 20.7f, 21.5f })
                if (Crossed(ding, from, to)) Cue(chime, 0.25f);
            if (Crossed(PrologueScript.ThrowAt, from, to)) Cue(whoosh, 0.6f);
            if (Crossed(PrologueScript.ThrowAt + PrologueScript.ThrowSeconds, from, to)) Cue(thud, 0.4f);
            // One last buzz from the grass, far off, after the scream.
            if (Crossed(26.2f, from, to)) { lastBuzz = 26.2f; Cue(buzz, 0.12f); }
            if (Crossed(PrologueScript.TwinkleAt, from, to)) Cue(starFall, 0.55f);
            if (Crossed(PrologueScript.ImpactAt, from, to)) Cue(starImpact, 0.9f);
        }

        private static bool Crossed(float at, float from, float to) => from < at && to >= at;

        // ---- Shots ----

        private void FrameShot(float t)
        {
            PrologueScript.ShotSpan span = PrologueScript.ShotAt(t, out float time, out float length);
            float u = Mathf.Clamp01(time / length);
            float eased = Mathf.SmoothStep(0f, 1f, u);
            Vector3 rest = PrologueSet.RestPosition(out Vector3 forward, out Vector3 right);
            Vector3 lean = LeanSpot(rest, forward, right);
            Vector3 throwSpot = StudentThrowSpot(rest, forward, right);
            Vector3 carPosition = set.Car.localPosition;
            Vector3 carForward = set.Car.forward;
            Vector3 carRight = Vector3.Cross(Vector3.up, carForward);
            Vector3 headLocal = set.Head != null ? set.Root.InverseTransformPoint(set.Head.position) : set.Student.localPosition + Vector3.up * 1.25f;
            Vector3 position, target;
            float fieldOfView = 40f, shake = 0f;

            switch (span.Shot)
            {
                case PrologueScript.Shot.Highway:
                    // Tracking alongside from the front quarter round to the side as the car comes on.
                    position = carPosition + carRight * Mathf.Lerp(4.4f, 5.4f, eased) + carForward * Mathf.Lerp(7f, -0.5f, eased) + Vector3.up * Mathf.Lerp(1.2f, 2.3f, eased);
                    target = carPosition + Vector3.up * 0.95f + carForward * Mathf.Lerp(1.2f, 2.6f, eased);
                    break;
                case PrologueScript.Shot.Inside:
                    // From the passenger seat, on the student's face in the phone's light.
                    // Just behind the windscreen on the passenger's side, below the roof whatever its height.
                    position = set.Root.InverseTransformPoint(set.Car.TransformPoint(new Vector3(-0.6f, Mathf.Min(1.3f, set.RoofHeight - 0.2f), 0.42f)));
                    target = headLocal + Vector3.up * 0.15f;
                    fieldOfView = Mathf.Lerp(55f, 47f, eased);
                    break;
                case PrologueScript.Shot.Swerve:
                    // Fixed beside the bend as the car tears into it and skids along the dirt road.
                    position = new Vector3(-6f, 2.6f, 21f);
                    target = carPosition + Vector3.up * 0.6f + carForward * 2f;
                    fieldOfView = 46f;
                    shake = time < 1.6f ? 0.06f * (1f - time / 1.6f) : 0f;
                    break;
                case PrologueScript.Shot.Throw:
                    position = rest + right * 5.2f - forward * 2.6f + Vector3.up * 1.5f + forward * (0.4f * eased);
                    // Taking in the open door, the hop down and the walk out to where they throw.
                    target = rest + right * 2.1f + forward * 0.4f + Vector3.up * 0.9f;
                    if (t > PrologueScript.ThrowAt)
                        target = Vector3.Lerp(target, set.Root.InverseTransformPoint(set.Phone.position), 0.35f * PrologueScript.Ease(t, PrologueScript.ThrowAt, PrologueScript.ThrowAt + 0.5f));
                    break;
                case PrologueScript.Shot.Yell:
                {
                    Vector3 facing = Flat(ThrowTarget(rest, forward, right) - throwSpot).normalized;
                    position = throwSpot + facing * Mathf.Lerp(3.2f, 2.3f, eased) + Vector3.up * 0.75f;
                    target = headLocal + Vector3.down * 0.2f;
                    fieldOfView = 38f;
                    shake = t < PrologueScript.SlumpAt ? 0.035f : 0f;
                    break;
                }
                case PrologueScript.Shot.Lean:
                    position = lean + right * Mathf.Lerp(5.2f, 4.2f, eased) - forward * 0.3f + Vector3.up * 1.0f;
                    target = (t < PrologueScript.ArriveBackAt ? set.Student.localPosition : lean) + Vector3.up * 0.9f + right * 0.3f;
                    break;
                case PrologueScript.Shot.LookUp:
                {
                    // Low beside the student, tilting from their face up over the car roof into the sky.
                    position = lean + right * 1.4f + forward * 0.9f + Vector3.up * 0.45f;
                    Vector3 sky = position + (StarSkyPoint(rest, forward, right) - position).normalized * 20f;
                    target = Vector3.Lerp(headLocal, sky, PrologueScript.Ease(time, 0f, 4f));
                    fieldOfView = 48f;
                    break;
                }
                case PrologueScript.Shot.Dive:
                {
                    position = lean + right * 9f + forward * 2.5f + Vector3.up * 1.8f;
                    Vector3 framing = lean + Vector3.up * 3.5f - forward * 2f - right;
                    Vector3 star = set.Root.InverseTransformPoint(set.StarCore.position);
                    // Half way to the star, so it stays in frame as it falls and the camera pans down with it.
                    target = Vector3.Lerp(framing, star, 0.5f);
                    fieldOfView = 50f;
                    break;
                }
                default:
                    // Close on the student's face, lit gold from above, until the white.
                    position = lean + right * Mathf.Lerp(1.6f, 1.2f, eased) + forward * 0.25f + Vector3.up * 1.0f;
                    // The head bone sits at the base of the model's big head; the face is above it.
                    target = headLocal + Vector3.up * 0.2f;
                    fieldOfView = 36f;
                    shake = 0.03f * PrologueScript.Ease(t, 42.6f, PrologueScript.ImpactAt);
                    break;
            }

            if (!calm && shake > 0f)
                position += new Vector3(Mathf.PerlinNoise(t * 23f, 0f) - 0.5f, Mathf.PerlinNoise(0f, t * 23f) - 0.5f, Mathf.PerlinNoise(t * 23f, t * 23f) - 0.5f) * (shake * 2f);
            view.transform.position = set.World(position);
            view.transform.rotation = Quaternion.LookRotation(set.World(target) - view.transform.position);
            view.fieldOfView = fieldOfView;
        }

        // ---- Drawing ----

        private void OnGUI()
        {
            if (set == null) return;
            // In front of the cutscene overlay (-1000), the title menu and every other overlay.
            GUI.depth = -1001;
            Event current = Event.current;
            // The prologue takes every click, scroll and key while it plays, so nothing behind it reacts.
            if (current.type != EventType.Repaint && current.type != EventType.Layout)
            {
                if (current.isMouse || current.isKey || current.type == EventType.ScrollWheel) current.Use();
                return;
            }
            if (current.type != EventType.Repaint) return;
            EnsureStyles();
            float t = Time.unscaledTime - started;
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            Color previous = GUI.color;

            // Solid black first, so nothing behind shows through wherever the camera's picture is less than opaque.
            GUI.color = Color.black;
            GUI.DrawTexture(screen, pixel);
            if (t < PrologueScript.InkFrom && picture != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(screen, picture, ScaleMode.StretchToFill, false);
            }
            DrawToasts(t);
            DrawLines(t);
            DrawOverlay(t, screen);

            bool armed = Time.unscaledTime <= skipArmedUntil;
            GUI.color = new Color(1f, 1f, 1f, armed ? 0.9f : 0.35f);
            float scale = Screen.height / 1080f;
            string hint = preview ? "Prologue preview · Esc to stop" : armed ? "Press Esc again to skip" : "Esc twice to skip";
            GUI.Label(new Rect(Screen.width - 520f * scale, Screen.height - 50f * scale, 500f * scale, 32f * scale), hint, hintStyle);
            GUI.color = previous;
        }

        // Black at the start, the white of the impact, then ink, then black.
        private void DrawOverlay(float t, Rect screen)
        {
            Color colour;
            float alpha;
            if (t < PrologueScript.FadeInSeconds) { colour = Color.black; alpha = 1f - PrologueScript.Ease(t, 0f, PrologueScript.FadeInSeconds); }
            else if (t < PrologueScript.ImpactAt - 0.25f) { colour = Cream; alpha = 0f; }
            else if (t < PrologueScript.InkFrom)
            {
                colour = Cream;
                alpha = PrologueScript.Ease(t, PrologueScript.ImpactAt - 0.25f, PrologueScript.ImpactAt + 0.05f);
            }
            else if (t < PrologueScript.BlackFrom)
            {
                colour = Color.Lerp(Cream, Ink, PrologueScript.Ease(t, PrologueScript.InkFrom, PrologueScript.InkTo));
                alpha = 1f;
            }
            else
            {
                colour = Color.Lerp(Ink, Color.black, PrologueScript.Ease(t, PrologueScript.BlackFrom, PrologueScript.BlackTo));
                alpha = 1f;
            }
            if (alpha <= 0f) return;
            GUI.color = new Color(colour.r, colour.g, colour.b, alpha);
            GUI.DrawTexture(screen, pixel);
        }

        // The phone's notifications stack in the top right, newest first, each sliding in and shaking as it buzzes.
        // When the phone is thrown they all break loose and tumble away.
        private void DrawToasts(float t)
        {
            float scale = Screen.height / 1080f;
            float width = 470f * scale, height = 92f * scale, gap = 12f * scale, margin = 36f * scale;
            float shatter = t >= PrologueScript.ThrowAt ? (t - PrologueScript.ThrowAt) / 0.8f : -1f;
            if (shatter >= 1f) return;

            int shown = 0;
            for (int i = PrologueScript.Toasts.Length - 1; i >= 0; i--)
            {
                PrologueScript.Toast toast = PrologueScript.Toasts[i];
                if (toast.At > t) continue;
                // Older notifications move down as newer ones arrive; the oldest fade out of the bottom of the stack.
                float slot = 0f;
                for (int j = i + 1; j < PrologueScript.Toasts.Length; j++)
                    if (PrologueScript.Toasts[j].At <= t) slot += PrologueScript.Ease(t, PrologueScript.Toasts[j].At, PrologueScript.Toasts[j].At + ToastSlideSeconds);
                if (slot >= MaxToasts) continue;
                if (++shown > MaxToasts + 1) break;

                float arrive = PrologueScript.Ease(t, toast.At, toast.At + ToastSlideSeconds);
                float alpha = arrive * Mathf.Clamp01(MaxToasts - slot);
                Rect card = new Rect(Screen.width - margin - width + (1f - arrive) * (width + margin), margin + slot * (height + gap), width, height);
                float since = t - toast.At;
                if (!calm && since < 0.45f) card.x += Mathf.Sin(since * 90f) * 3f * scale * (1f - since / 0.45f);

                Matrix4x4 matrix = GUI.matrix;
                if (shatter >= 0f)
                {
                    float seed = i * 12.9898f;
                    Vector2 drift = new Vector2(Mathf.Sin(seed) * 900f + 300f, Mathf.Cos(seed * 1.7f) * 500f + 900f * shatter) * scale;
                    card.position += drift * shatter;
                    alpha *= 1f - shatter;
                    GUIUtility.RotateAroundPivot(Mathf.Sin(seed * 3.1f) * 140f * shatter, card.center);
                }
                DrawToast(card, toast, alpha, scale);
                GUI.matrix = matrix;
            }
        }

        private void DrawToast(Rect card, PrologueScript.Toast toast, float alpha, float scale)
        {
            GUI.color = new Color(1f, 1f, 1f, alpha * 0.92f);
            GUI.DrawTexture(card, toastTexture);
            Rect icon = new Rect(card.x + 16f * scale, card.y + (card.height - 44f * scale) * 0.5f, 44f * scale, 44f * scale);
            Color tint = IconColour(toast.Sender);
            GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
            GUI.DrawTexture(icon, iconTexture);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            float left = icon.xMax + 14f * scale;
            GUI.Label(new Rect(left, card.y + 12f * scale, card.width - (left - card.x) - 70f * scale, 30f * scale), toast.Sender, senderStyle);
            GUI.Label(new Rect(card.xMax - 80f * scale, card.y + 12f * scale, 64f * scale, 30f * scale), "now", timeStyle);
            GUI.Label(new Rect(left, card.y + 44f * scale, card.width - (left - card.x) - 16f * scale, 34f * scale), toast.Text, bodyStyle);
        }

        private static Color IconColour(string sender)
        {
            if (sender.StartsWith("Uni")) return new Color(0.45f, 0.62f, 1f);
            if (sender.StartsWith("Shift")) return new Color(1f, 0.62f, 0.3f);
            if (sender.StartsWith("Group")) return new Color(0.45f, 0.9f, 0.55f);
            if (sender.StartsWith("Mum")) return new Color(1f, 0.5f, 0.65f);
            if (sender.StartsWith("Bank")) return new Color(0.9f, 0.85f, 0.4f);
            return new Color(0.75f, 0.75f, 0.8f);
        }

        // The inner voice in the lower third, the shout shaking letter by letter, and the clock card in the corner.
        private void DrawLines(float t)
        {
            float scale = Screen.height / 1080f;
            foreach (PrologueScript.Line line in PrologueScript.Lines)
            {
                float alpha = PrologueScript.LineAlpha(line, t);
                if (alpha <= 0f) continue;
                switch (line.Style)
                {
                    case PrologueScript.LineStyle.Clock:
                        Shadowed(new Rect(48f * scale, Screen.height - 110f * scale, 400f * scale, 60f * scale), line.Text, clockStyle, alpha);
                        break;
                    case PrologueScript.LineStyle.Shout:
                        DrawShout(line.Text, t, alpha, scale);
                        break;
                    default:
                        float width = Mathf.Min(Screen.width - 64f, 1400f * scale);
                        Shadowed(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.76f, width, 80f * scale), line.Text, thoughtStyle, alpha);
                        break;
                }
            }
        }

        private void DrawShout(string text, float t, float alpha, float scale)
        {
            float total = 0f;
            Vector2[] sizes = new Vector2[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                sizes[i] = shoutStyle.CalcSize(new GUIContent(text[i].ToString()));
                total += sizes[i].x;
            }
            float x = (Screen.width - total) * 0.5f, y = Screen.height * 0.72f;
            for (int i = 0; i < text.Length; i++)
            {
                Vector2 jitter = calm ? Vector2.zero : new Vector2(Mathf.PerlinNoise(t * 30f, i * 7.1f) - 0.5f, Mathf.PerlinNoise(i * 3.3f, t * 30f) - 0.5f) * 10f * scale;
                Shadowed(new Rect(x + jitter.x, y + jitter.y, sizes[i].x + 4f, sizes[i].y), text[i].ToString(), shoutStyle, alpha);
                x += sizes[i].x;
            }
        }

        // A soft drop shadow first, so pale text reads over the bright headlights and the white of the impact.
        private static void Shadowed(Rect rect, string text, GUIStyle style, float alpha)
        {
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.6f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(rect, text, style);
        }

        private void EnsureStyles()
        {
            if (pixel == null)
            {
                pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Prologue Pixel" };
                pixel.SetPixel(0, 0, Color.white);
                pixel.Apply();
            }
            if (thoughtStyle != null && styledHeight == Screen.height) return;
            styledHeight = Screen.height;
            float scale = Screen.height / 1080f;
            if (toastTexture != null) Destroy(toastTexture);
            if (iconTexture != null) Destroy(iconTexture);
            toastTexture = MenuArt.Rounded(Mathf.Max(8, Mathf.RoundToInt(470f * scale)), Mathf.Max(8, Mathf.RoundToInt(92f * scale)), 22f * scale,
                new Color(0.1f, 0.1f, 0.13f, 0.9f), new Color(1f, 1f, 1f, 0.12f), 1.5f * scale);
            int icon = Mathf.Max(8, Mathf.RoundToInt(44f * scale));
            iconTexture = MenuArt.Rounded(icon, icon, 11f * scale, Color.white, Color.white, 0f);

            Font body = Resources.Load<Font>("Dialogue/Nunito-Regular");
            Font heading = Resources.Load<Font>("Dialogue/Fredoka-SemiBold");
            senderStyle = Style(body, 22f, FontStyle.Bold, TextAnchor.UpperLeft, Cream, scale);
            timeStyle = Style(body, 18f, FontStyle.Normal, TextAnchor.UpperRight, new Color(0.7f, 0.7f, 0.75f), scale);
            bodyStyle = Style(body, 21f, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.88f, 0.88f, 0.9f), scale);
            bodyStyle.clipping = TextClipping.Clip;
            thoughtStyle = Style(body, 36f, FontStyle.Italic, TextAnchor.MiddleCenter, Cream, scale);
            thoughtStyle.wordWrap = true;
            shoutStyle = Style(heading != null ? heading : body, 64f, FontStyle.Normal, TextAnchor.MiddleCenter, Cream, scale);
            clockStyle = Style(body, 30f, FontStyle.Italic, TextAnchor.MiddleLeft, Cream, scale);
            hintStyle = Style(body, 18f, FontStyle.Normal, TextAnchor.MiddleRight, Cream, scale);
        }

        // GUI.color multiplies the text colour where it is drawn, for fades.
        private static GUIStyle Style(Font font, float size, FontStyle fontStyle, TextAnchor alignment, Color colour, float scale)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(10, Mathf.RoundToInt(size * scale)),
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = false,
                richText = false
            };
            style.normal.textColor = colour;
            if (font != null) style.font = font;
            return style;
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction : Vector3.forward;
        }

        private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * a + 2f * inverse * t * b + t * t * c;
        }
    }
}
