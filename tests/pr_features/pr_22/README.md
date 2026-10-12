# PR22 area audio acceptance

Target: `686ebe99dcab007ff0c7dad68edf2e6b81efc654`. Category: `PR22`. PlayMode: 4 cases.

New vectors:
- The same clip can play in independent music/ambience channels at different gains; stopping one leaves the other playing.
- A gain-only update reuses the running source and playback position while fading from its current volume.
- Negative fade durations settle immediately; negative gain clears silent voices, and a subsequent positive gain can start the requested clip.
- Disposing one channel releases its two owned native AudioSources after Unity destruction while the sibling still plays.

Private LoopChannel is constructed/invoked through reflection; real AudioSource playback and Unity destruction are observed. This keeps fixtures independent of DataDrivenWorldLoader singletons and save-provider registration. No listener/master setting is modified.

Manual limits: listening quality, audible clicks, track licensing/selection, mixer routing, portal event routing, and a platform's actual audio device need manual/controller integration review. Existing PR tests cover loader routing and crossfades. Native PlayMode playback is required; headless/no-audio environments may not establish isPlaying evidence.
