# Dialogue frame art provenance

The four principal dialogue frames were generated on 2026-09-22 with the built-in Codex `image_gen` tool, using the imagegen skill's default generation workflow. Each prompt requested a polished 2D fantasy game UI frame in a consistent wide 3:1 landscape layout, an opaque pale writing area, a quiet center reserved for the speaker name and dialogue, decorative perimeter only, and no baked text, letters, numerals, or watermark.

| Resource | Character direction | Decorative cues |
| --- | --- | --- |
| `Resources/Dialogue/Stella.png` | Fashionable, academic magical engineer; red bow and glasses cues | Red ribbon, gears, compass, academy books, warm rose and gold |
| `Resources/Dialogue/CinnaCirra.png` | Kind, nervous, idealistic sky and cloud lover | Powder-blue clouds, kites, stars, silver and lavender |
| `Resources/Dialogue/Orio.png` | Relaxed tropical gamer who enjoys automation | Tropical leaves, waves, flip-flops, controller, gears |
| `Resources/Dialogue/Katrina.png` | Ambitious fashion and celebrity culture fan | Pearls, jewels, boutique accents, white and cobalt ribbons |

The generated PNGs are 2172x724 pixels (3:1). Visual QA confirmed each has a pale, uncluttered center suitable for the existing 760x240 IMGUI panel and distinct perimeter decoration. Unity `.meta` files configure the outputs as sRGB, non-mipmapped UI textures with alpha support. The generated files are intentionally retained in the project under `Lumenfall/Assets/Resources/Dialogue/` for runtime loading and review.
