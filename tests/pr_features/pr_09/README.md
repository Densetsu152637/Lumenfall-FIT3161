# PR09 audio signal acceptance
Target: `dd4c0787612f68984a4e8c68604983f969d66189`.

Three EditMode cases cover context notification coalescing across equivalent maps and unrelated flags, null-binding detachment during active dialogue, and sleep cue rearming after falling edges. Each owns and disposes its signal bridge and local runtime; no audio preferences are changed.

Unity compilation/execution is delegated to the catalog runner. Manual limits: audible balance, approved media, device routing, and AudioSource pause/resume require listening/PlayMode checks.
