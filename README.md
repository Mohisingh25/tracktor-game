# Indian Tractor Simulator 3D: Stage 1

This is a Unity 6 (Built-in Render Pipeline, C#) Android project. Stage 1 gives you **one fully drivable tractor** with
realistic physics, manual and automatic transmissions, mobile touch controls and a proving ground in a North-Indian setting.

The whole world (terrain, tractor model, UI, audio) is **generated in code at runtime**, so you don't need to set
anything up in the Unity Editor. You push the repo to GitHub, the workflow builds the APK, and you install it.

---

## 1. Get the APK (no local Unity needed)

1. Create a new GitHub repo and upload everything in this folder (keep `.github/workflows/build-android.yml`).
2. In the repo, go to **Settings → Secrets and variables → Actions** and add these three secrets:
   * `UNITY_EMAIL`: your Unity account email
   * `UNITY_PASSWORD`: your Unity account password
   * `UNITY_LICENSE`: the full contents of your Unity Personal `.ulf` licence file
     (see game.ci → Docs → GitHub → Activation → Personal licence). These are the same secrets the spaceflight project uses.
3. Open the **Actions** tab, choose **Build Android APK**, then **Run workflow**. Every push to `main` also triggers it.
   The first build takes 30–60 minutes because it downloads Unity; later builds are faster thanks to caching.
4. Open the finished run and download the **IndianTractorSim-APK** artifact. It's a zip that contains `IndianTractorSim.apk`.
5. Copy the APK to your phone and install it. You'll need to allow installing from unknown sources.
   The build targets ARM64 (IL2CPP), which covers any Android phone from roughly 2017 onwards.

The Unity version is set in `ProjectSettings/ProjectVersion.txt` (`6000.3.8f1`). If game-ci can't find a Docker image for
that version, change it to any `6000.x` version listed on Docker Hub as `unityci/editor:ubuntu-<version>-android-3`.

**Opening it in the Unity Editor (optional):** open the folder with Unity 6, then run **Tractor Sim → Prepare Project** from the menu
(this creates the scene, materials and player settings) and press Play. Keyboard controls are listed in the in-game "How to drive" screen.

---

## 2. What works in Stage 1 (acceptance tests 1–7, 17–19)

| Feature | How it's implemented |
|---|---|
| Start / stop engine | Starter cranks the engine for about 0.7 s, then the diesel catches. A starter interlock means it won't crank in gear unless the clutch is pressed |
| Diesel engine | Real torque curve (peak 194 Nm at 1400 rpm), a mechanical droop governor (the throttle sets the rpm and the fuel rack opens under load), flywheel inertia, friction and compression braking |
| Manual transmission | Sliding-mesh 8F + 2R gearbox (lever R-N-1-2-3-4 plus L/H range). Shifting without the clutch **grinds**. Range changes only at standstill. Gear speeds match a 47 HP Indian tractor brochure (2.9 to 30.8 km/h) |
| Clutch | Analog pedal with a biting zone. The clutch slips, locks and stalls the engine if you dump it in a tall gear (verified by tests) |
| Automatic | Centrifugal-style auto clutch, load-aware shifting over all 8 gears, no upshift during wheelspin, and F/R direction change that brakes to a stop first |
| Hand throttle | Sticky lever, like a real tractor: set the rpm and crawl |
| Rear-wheel drive and differential | Engine, clutch, gearbox and open differential are solved together with the rear-wheel spin (engine inertia is reflected through the gear ratio). **Diff lock** is included |
| Steering | Front wheels physically steer with Ackermann geometry, a rate limit and speed-sensitive lock |
| Brakes | Rear-only service brakes with progressive pedal feel, a parking brake, engine braking and parked-on-slope stiction |
| Suspension and tyres | Sphere-cast spring-damper wheels, a tanh tyre model with a friction circle, rolling resistance and sinkage |
| Terrain physics | Nine surfaces: asphalt, dirt, grass, wet grass, mud, sand, rock, water and ploughed field. Each has different grip, rolling resistance, sinkage and bumpiness |
| Load sensitivity | Headless tests show a 6-tonne load on a 12° hill drops speed from 18 km/h to 3 km/h |
| Fuel, temperature, damage | BSFC-based L/h, a coolant thermostat model with overheat derating, impact damage that reduces power, and REFUEL & REPAIR in the pause menu |
| Stuck / rollover recovery | Detects both and offers a RECOVER button (it's free for now; it will have a cost once money arrives in Stage 6) |
| Cameras | Chase, driver seat (steering wheel, dash, levers and pedals all animate), side, front, hitch and cinematic. Drag to look around |
| Audio | Real-time synthesised 3-cylinder diesel that follows rpm and load, plus starter, horn, gear clunk, grind, indicator tick, impact and village ambience |
| Lights | Headlights (real spot lights), tail and brake lights, indicators and hazards |
| Effects | Exhaust smoke that goes black under load and on start-up, plus dust and mud spray from the rear tyres according to surface and slip |
| Touch controls | Steering wheel, arrows or tilt; analog accelerator, brake and clutch (slide down to feather); multi-touch; control size and steering sensitivity settings |
| Menus and saving | Main menu, pause (resume/restart/recover/service/settings/main menu) and settings (transmission, steering, graphics Low–Ultra, 30/60 FPS, render scale, time of day, volume). Settings, odometer, fuel and damage are saved locally and survive restarts |

**Proving ground:** asphalt road, dirt tracks, a mud pit with puddles, wet grass, sand dunes, a rock garden, Hill A (10° up / 18° down),
Hill B (18° up / 25° down), a river ford, a ploughed field, sugarcane and wheat plots, and a village with pucca and kutcha houses, a hand pump,
a water tank, a tea stall, electric poles with sagging wires, cattle and trees.

## 3. Verification done

* `Tools/DrivetrainTests/DrivetrainTests.cs` is a headless simulation of the drivetrain code that ships in the game. It runs 15 checks
  (idle, starter interlock, L1 pull-away, stall on clutch dump, grind, full 8-gear run, engine braking, braking distance,
  automatic shifting, auto reverse, hill climb, loaded hill, mud wheelspin, coolant temperature). All 15 pass; see `last-run.txt`.
  To run it: `mcs Tools/DrivetrainTests/DrivetrainTests.cs Assets/Scripts/Vehicle/Core/*.cs && mono DrivetrainTests.exe`
* All C# scripts were compile-checked against a stand-in of the Unity API. **They have not yet been compiled by a real Unity editor or
  run on a device.** The first GitHub Actions run is the real compile. If it fails, send me the log and I'll fix it.

## 4. Code map (the modular architecture the brief asked for)

```
Assets/Scripts
  Core/      GameBootstrap, GameManager (state machine, settings, autosave), SaveSystem, MatLib, ProcTex, MeshUtil
  Vehicle/   TractorController, VehiclePhysics, SteeringSystem (+BrakeSystem), TractorBuilder, TractorVisuals, Effects
  Vehicle/Core/  TractorSpec, EngineSystem, TransmissionSystem, Drivetrain (+AutomaticController), TireModel   ← pure C#, unit-tested
  Terrain/   SurfaceType (+SurfaceZone), TestGroundBuilder
  Input/     MobileInputManager (touch + tilt + keyboard)
  UI/        HUDController, MenuController, TouchControls, UIFactory
  Camera/    CameraManager
  Audio/     AudioManager, EngineSoundSynth
Assets/Editor/BuildScript.cs   ← creates scene/materials/player settings and builds the APK
```

A tractor is just a `TractorSpec` (power, torque curve, gear speeds, mass, wheel sizes, brakes, steering, traction).
Adding the other models in Stage 6 means adding new specs plus a model variant.

## 5. Next stages

* **Stage 2:** trolley with hitch articulation, load mass and tipping, using `VehiclePhysics.ExtraRollingForce` and a second rigidbody on a joint.
* **Stage 3:** implements (rotavator, cultivator, plough) that deform and mark soil.
* **Stage 4:** village map and Track 1 with checkpoints, timer, rewards and failure conditions.
* **Stages 5–8:** the other tracks, garage and progression, weather, traffic, and optimisation.
