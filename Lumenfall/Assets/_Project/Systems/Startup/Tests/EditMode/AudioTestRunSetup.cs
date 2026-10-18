#if UNITY_EDITOR
// Assembly scope also covers filtered runs of a single fixture or case.
[assembly: UnityEngine.TestTools.PrebuildSetup("Lumenfall.Audio.Editor.AudioTestPlaybackGuard")]
[assembly: UnityEngine.TestTools.PostBuildCleanup("Lumenfall.Audio.Editor.AudioTestPlaybackGuard")]
#endif
