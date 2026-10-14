# Cat video brief (for a video generation model)

CatSaver shows a real-looking cat when it's time to drink water. Each video is background-removed with a green screen and turned into a looping sprite inside the app:

- **Walking cat**: strolls back and forth along the bottom of the screen. The app moves it across the screen, so the cat walks **in place**.
- **Sitting / acting cat**: shown in the middle of the screen next to a speech bubble saying 「起來喝水了！」.

Generate each clip separately with the settings below. If several clips use **the same cat** (same breed, coat and markings), the app can make that one cat walk, stop, sit and drink.

---

## Technical requirements (apply to every clip)

```
Background: solid, flat chroma-key green (#00B140 or #00FF00), evenly lit edge to edge.
            No gradient, no vignette, no floor texture, no props in green, no scenery.
Camera:     locked-off tripod shot. No pan, no zoom, no camera shake, no cuts.
Framing:    the whole cat always in frame (ears, paws and full tail), about 10% margin on all sides.
            The cat fills about 60% of the frame height.
Lighting:   soft and even studio light. Minimal shadow on the background, no rim of green light on the fur.
Cat:        photorealistic, cute, clean fur, natural proportions.
            Eyes amber / yellow / blue / copper (NOT green). No green collar or accessories.
Motion:     smooth and natural. No motion blur, sharp fur edges.
Loop:       the last frame should match the first frame (seamless loop).
Output:     MP4 (H.264, high bitrate) or ProRes / PNG sequence. 1920x1080 (minimum 1280x720), 24–30 fps.
            4–8 seconds. No audio needed. No text, logos or watermarks.
```

**Negative prompt (if supported):**
`green eyes, green objects, gradient background, vignette, shadows on background, camera movement, zoom, cut, motion blur, text, watermark, extra limbs, deformed paws, cropped tail, cropped ears, multiple cats`

---

## Clips

### 1. Walk cycle, side view (most important)

> A cute photorealistic [orange tabby] cat walking in place on a treadmill-like spot, seen exactly from the side, facing right, against a flat chroma-key green screen. Calm, natural walking pace with a steady, repeating gait; head level, tail held up with a gentle curve. The cat stays horizontally centered in the frame the whole time and never turns. Locked-off camera, even studio lighting, full body and full tail visible, 4 seconds, seamless loop.

- Must face **right** (the app mirrors it for walking left).
- It must **not move across the frame**. Paws step in place, like walking on a treadmill.

### 1b. Walk, then sit down

> The same cat walking in place toward the right (exactly like clip 1) for about 2 seconds, then stopping and sitting down facing the camera. Flat chroma-key green screen, locked-off camera, whole cat in frame, 6 seconds.

The app plays this clip when the cat arrives in the middle of the screen. It also plays the sitting-down part backwards to make the cat stand up.

### 2. Sitting idle

> The same cute [orange tabby] cat sitting upright facing the camera at a slight three-quarter angle on a flat chroma-key green screen. It looks around curiously, blinks slowly, twitches an ear and swishes its tail gently, then returns to the starting pose. Locked-off camera, even lighting, whole cat in frame, 6 seconds, seamless loop.

### 3. Drinking water (fits the app perfectly)

> The same cute [orange tabby] cat crouching beside a small white ceramic water bowl, side view facing right, lapping water with its tongue, small ripples in the bowl, then looking up at the camera and licking its lips. Flat chroma-key green screen, locked-off camera, even lighting, whole cat and bowl in frame, 6 seconds.

- The bowl must be white or blue, **not green**.

### 4. Meowing / calling you

> The same cute [orange tabby] cat sitting facing the camera on a flat chroma-key green screen, looks straight into the lens and meows twice (mouth clearly opening), then tilts its head adorably. Locked-off camera, even lighting, whole cat in frame, 5 seconds, ends in the same pose it started.

### 5. Stretch and yawn (the "take a break" moment)

> The same cute [orange tabby] cat standing on a flat chroma-key green screen, side view facing right, does a long front stretch (front legs extended, rear up), then a big yawn, then sits down. Locked-off camera, even lighting, whole cat in frame, 6 seconds.

### 6. Loaf and slow blink (calm)

> The same cute [orange tabby] cat lying in a "loaf" pose (paws tucked under) facing the camera on a flat chroma-key green screen, slowly blinking and purring, ears relaxed, tiny head movements. Locked-off camera, even lighting, whole cat in frame, 6 seconds, seamless loop.

### Extra cats (optional, for variety)

Generate clips 1 and 2 again with different cats, replacing `[orange tabby]`:
- `fluffy white kitten with blue eyes`
- `black cat with copper eyes`
- `grey British Shorthair`
- `calico cat`
- `Siamese cat`

---

## After generating

1. Put the videos in `tools/source/`, for example:
   - `tools/source/orange-walk.mp4`
   - `tools/source/orange-sit.mp4`
2. Add them to `CLIPS` in `tools/make_real_cats.py` with a role:
   - `walk`
   - `sitdown`
   - `sit`
   - `act`: meow / wave / drink / ...

   For example:
   ```python
   {"file": "tools/source/orange-walk.mp4", "role": "walk", "fps": 24},
   {"file": "tools/source/orange-sit.mp4", "role": "sit", "fps": 8, "start": 0, "length": 5},
   ```
3. Run `python tools/make_real_cats.py`, then build.
   - The walk speed is measured automatically from the planted paws, so they don't slide.
   - All clips share one scale, so keep the camera framing the same across clips.
