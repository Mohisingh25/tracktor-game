# Making it look ultra-realistic: adding real 3D art

The game runs straight away with art generated in code: tractors, implements, trolleys, terrain, trees and houses.
To get closer to the Farming Simulator look in your reference screenshot, you need **artist-made models and photo textures**.
The code already has slots for them. You drag assets into one file, push to GitHub, and the next APK and DMG use them.
If a slot is empty, the game keeps using the built-in art, so nothing breaks.

## 1. One-time setup on your Mac

1. In **Unity Hub → Installs → Install Editor**, choose **6000.3.8f1** (any 6000.3.x works).
   Tick **Android Build Support** and **Mac Build Support (Mono)**.
2. **Unity Hub → Projects → Add** → pick your `IndianTractorSim` folder, then open it.
   The first import takes a few minutes.
3. In the top menu, run **Tractor Sim → Prepare Project**. This creates the scene, the materials and `Assets/Resources/ArtCatalog.asset`.
4. Open `Assets/Scenes/Main.unity` and press **Play** to test in the editor.
   Keyboard: W/S accelerate/brake, A/D steer, Shift clutch, E/Q gears, I start, G attach, V lift, P PTO, B tip, N load, C camera.
5. Install Git LFS once, because texture packs are big: `brew install git-lfs && git lfs install`.

## 2. Get realistic assets

From the Unity Asset Store, signed in with the same Unity account. Use **Window → Package Manager → My Assets → Import**:

| What | Pack | Price | Use it for |
|---|---|---|---|
| Ground textures | **Realistic Terrain Textures FREE** | Free | grass, dirt, rock, mud, field slots |
| Ground textures | **4K Realistic Outdoor Materials** | Free | grass, mud and dirt alternatives |
| Terrain layers & heightmaps | **Terrain Sample Asset Pack** (Unity) | Free | rock, grass and gravel textures. Its vegetation needs Shader Graph, so skip that part |
| Tractors | Realistic tractor models such as **Tractor SX** or the **2 Tractors Bundle** series, or models from CGTrader/Sketchfab with a game-use licence | Paid ($20–60 each) | Your 5 brands. Free realistic tractors are rare; free ones are usually low-poly or cartoon |
| Trees | Any built-in-pipeline conifer or deodar pack | Free or paid | `trees` slot (mountains and farm) |

Pick packs marked **Built-in Render Pipeline** compatible. This project uses the built-in pipeline.

## 3. Plug them in (Assets/Resources/ArtCatalog.asset)

Select `ArtCatalog` in the Project window. The Inspector shows these slots.

**Tractors:** there is one row per brand: `golu35`, `anshu50`, `haldhar47`, `bharatveer60`, `mohit150`.
* **Model:** drag the tractor prefab or FBX here.
* **Offset / Euler / Scale:** line the model up so its wheels sit on the ground. The tractor origin is at ground level, midway between the axles, with +Z forward.
  Most FBX models need **Scale 0.01** (they're in centimetres) or **Euler Y 180** (if they face backwards).
* **Wheel FL / FR / RL / RR:** type the names of the wheel objects inside the model, for example `Wheel_FL`, so they spin and steer.
  Leave these empty to keep the built-in wheels.
* **Hide Brand Decals:** leave this off to keep the GOLU CHAUDHARY, ANSHU SINGH and MOHIT SINGH stripes on the bonnet.

**Implements:** `trolley_single`, `trolley_tandem`, `rotavator`, `cultivator`, `plough`, `harrow`, `seeddrill`, `sprayer`, `leveller`.
The hitch point is the implement's origin; it does the work behind that, toward −Z.

**Ground:** set **Albedo**, **Normal** and **Tile Size** (in metres) for grass, dirt, rock, mud, gravel road and field. These texture the mountain terrain and the roads.

**Grass Blades:** a grass texture with transparency, used for the terrain's waving grass.

**Trees / Rocks / Bushes:** prefabs that get scattered on the mountains.

**Skybox:** an HDRI or procedural sky material.

Press Play to check. When it looks right:

```bash
git add -A
git commit -m "Real art"
git push
```

GitHub Actions then builds the APK and the DMG with your art.

## 4. Honest expectations

* The screenshot you shared is at the level of a PC game (Farming Simulator class). On phones, even the best tractor games use much simpler art.
  With good asset packs, Ultra graphics and post-processing on, you can get a convincing, realistic look. It still won't be identical to a PC render.
* Use **High** or **Ultra** graphics on flagship phones and **Medium** on mid-range ones.
  Post-processing (ACES film tone mapping, colour grading, bloom) is on from Medium up.
