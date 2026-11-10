using System.Collections.Generic;

namespace TractorSim
{
    public enum MissionKind { Route, Cargo, Field, Park }

    /// <summary>One job in the career. Places are names registered in <see cref="WorldAnchors"/> by the map.</summary>
    public class MissionDef
    {
        public string id, title, subtitle, story;
        public int map;                     // 0 village, 1 mountain
        public MissionKind kind;
        public string spawn;                // spawn anchor (null = map default)
        public string route;                // checkpoint route
        public string load, deliver;        // zones (Route missions: deliver = finish yard, optional)
        public CargoType cargo;
        public float tons;
        public bool tandem;                 // needs the 10 t tandem trolley
        public string implement;            // Field missions
        public byte fieldState; public float fieldPercent;
        public string parkZone;             // Park missions
        // rules
        public float timeLimitSec;
        public float maxSpill = -1f;        // fraction of the load that may be lost (-1 = only the 60 % hard limit)
        public float maxDamage = -1f;       // damage points that may be added during the job
        public float speedLimitKmh;         // 0 = none
        public bool night, fog;
        public int traffic = -1;            // -1 map default, 0 none, 1 light, 2 heavy
        public int reward;
        public int difficulty;              // 1..5

        public string RulesText()
        {
            var r = new List<string>();
            r.Add("Time limit " + MissionRunner.FormatTime(timeLimitSec));
            if (kind == MissionKind.Cargo) r.Add(tons.ToString("0.#") + " t of " + cargo + (tandem ? " · tandem trolley" : ""));
            if (kind == MissionKind.Field) r.Add(FieldArea.StateNames[fieldState] + " " + fieldPercent.ToString("0") + "% of the field with the " + implement);
            if (maxSpill >= 0f) r.Add("Spill ≤ " + (maxSpill * 100f).ToString("0") + "%");
            if (maxDamage >= 0f) r.Add("Damage ≤ " + maxDamage.ToString("0") + "%");
            if (speedLimitKmh > 0f) r.Add("Speed limit " + speedLimitKmh.ToString("0") + " km/h");
            if (night) r.Add("Night");
            if (fog) r.Add("Dense fog");
            if (traffic == 2) r.Add("Heavy traffic"); else if (traffic == 1) r.Add("Traffic");
            return string.Join("  ·  ", r.ToArray());
        }
    }

    /// <summary>The 20 career missions, from a village warm-up to the night-and-fog Himalayan legend run.</summary>
    public static class MissionCatalog
    {
        static MissionDef[] all;
        public static MissionDef[] All { get { if (all == null) all = Build(); return all; } }
        public static int Count { get { return All.Length; } }

        static MissionDef Route(string id, string title, string sub, int map, string route, string finish, float min, int reward, int diff, string story)
        {
            return new MissionDef { id = id, title = title, subtitle = sub, map = map, kind = MissionKind.Route, route = route, deliver = finish, timeLimitSec = min * 60f, reward = reward, difficulty = diff, story = story };
        }

        static MissionDef Cargo(string id, string title, string sub, int map, string load, string deliver, string route, CargoType c, float tons, float min, int reward, int diff, string story)
        {
            return new MissionDef { id = id, title = title, subtitle = sub, map = map, kind = MissionKind.Cargo, load = load, deliver = deliver, route = route, cargo = c, tons = tons, tandem = tons > 5f, timeLimitSec = min * 60f, reward = reward, difficulty = diff, story = story };
        }

        static MissionDef[] Build()
        {
            var l = new List<MissionDef>();
            // ---------------- village ----------------
            l.Add(Route("m01", "Gaon ki Sair", "Village loop", 0, "loop", null, 4f, 1500, 1,
                "Learn the tractor: take the loop past the fields, splash through the river ford and come back to the village. Keep it on the track!"));
            var m2 = Route("m02", "Kichad Mein Kamaal", "Mud pit challenge", 0, "mud", null, 2.5f, 2000, 2,
                "The monsoon has turned the west pit into soup. Get through every gate in the mud and the wet grass without getting stuck. Low gear, diff lock, no stopping.");
            l.Add(m2);
            var m3 = Route("m03", "Pahadi Practice", "Hill training", 0, "hills", null, 4f, 2500, 2,
                "Climb Hill A, then take Hill B from the steep side (25° up!), finish through the rock garden. Damage counts.");
            m3.maxDamage = 25f; l.Add(m3);
            var m4 = Cargo("m04", "Ganna Mill Tak", "Sugarcane to the mill", 0, "cane_bay", "mill", "cane_to_mill", CargoType.Sugarcane, 3f, 7f, 4000, 2,
                "Hitch a trolley, load 3 t of cut cane at the plot and haul it to the sugar mill. Cane is bulky – brake gently or it falls off.");
            m4.maxSpill = 0.15f; l.Add(m4);
            var m5 = new MissionDef { id = "m05", title = "Khet Taiyar", subtitle = "Rotavate the field", map = 0, kind = MissionKind.Field, implement = "rotavator", fieldState = FieldArea.Rotavated, fieldPercent = 40f, timeLimitSec = 9f * 60f, reward = 5000, difficulty = 2,
                story = "Sowing season is close. Fetch the rotavator from the implement yard, lower it, engage the PTO and rotavate 40% of the farm field." };
            l.Add(m5);
            var m6 = Cargo("m06", "Eent Ki Dhulai", "Bricks from the kiln", 0, "kiln", "village_yard", "kiln_to_village", CargoType.Bricks, 4.5f, 8f, 5000, 3,
                "4.5 t of fresh bricks from the bhatta for a new house in the village. Heavy and top-heavy – a crash will cost you.");
            m6.maxDamage = 25f; l.Add(m6);
            var m7 = new MissionDef { id = "m07", title = "Reverse Ustaad", subtitle = "Park the trolley in the shed", map = 0, kind = MissionKind.Park, parkZone = "shed_bay", timeLimitSec = 4f * 60f, maxDamage = 10f, reward = 3500, difficulty = 3,
                story = "Hitch a trolley and reverse it all the way into the narrow shed east of the road. Trolley fully inside, tractor stopped. Don't touch the walls." };
            l.Add(m7);
            var m8 = Cargo("m08", "Raat Ki Ret", "Night sand run", 0, "sand_pit", "village_yard", "sand_to_village", CargoType.Sand, 5f, 9f, 6000, 3,
                "A contractor needs 5 t of river sand tonight. Load at the sand pit, cross the ford in the dark and deliver to the village yard.");
            m8.night = true; l.Add(m8);
            var m9 = new MissionDef { id = "m09", title = "Buwai Ka Mausam", subtitle = "Sow the field", map = 0, kind = MissionKind.Field, implement = "seeddrill", fieldState = FieldArea.Sown, fieldPercent = 35f, timeLimitSec = 12f * 60f, reward = 8000, difficulty = 3,
                story = "Take the seed drill and sow 35% of the field before the rain. Straight lines, steady speed." };
            l.Add(m9);
            var m10 = Cargo("m10", "Bhaari Ganna", "9 t sugarcane", 0, "cane_bay", "mill", "cane_to_mill", CargoType.Sugarcane, 9f, 10f, 9000, 4,
                "The mill wants a full tandem load: 9 t of cane. Hitch the tandem trolley – it is long and heavy, and cane falls off at the slightest jerk.");
            m10.maxSpill = 0.10f; l.Add(m10);

            // ---------------- Himalayan ghat ----------------
            var m11 = Route("m11", "Ghat Darshan", "Drive the ghat road", 1, "ghat_up", "summit", 9f, 5000, 2,
                "Your first trip up the ghat: 11 hairpins and 4 km to Pahadgaon. Stay on the left, mind the traffic, use low gears.");
            m11.traffic = 1; l.Add(m11);
            l.Add(Cargo("m12", "Dhaba Supply", "Rice to the dhaba", 1, "depot", "dhaba", "ghat_dhaba", CargoType.Rice, 3f, 9f, 6000, 2,
                "Sher-e-Pahad dhaba has run out of rice. Load 3 t at the depot and deliver it to the dhaba halfway up the ghat."));
            var m13 = Cargo("m13", "Seb Pahadgaon", "Apples uphill", 1, "depot", "summit", "ghat_up", CargoType.Apples, 4.5f, 18f, 9000, 3,
                "4.5 t of apple crates must reach the Pahadgaon mandi. Every bruised apple costs money – keep the spill under 15%.");
            m13.maxSpill = 0.15f; l.Add(m13);
            var m14 = Cargo("m14", "Utraai Ka Khel", "Apples downhill", 1, "summit", "depot", "ghat_down", CargoType.Apples, 5f, 16f, 10000, 4,
                "Bring 5 t of apples DOWN the ghat. Police checking speed: never above 40 km/h. Use the engine brake – the brakes alone will not hold a loaded trolley.");
            m14.spawn = "summit"; m14.speedLimitKmh = 40f; l.Add(m14);
            var m15 = Cargo("m15", "Kohra", "Rice in thick fog", 1, "depot", "summit", "ghat_up", CargoType.Rice, 4f, 20f, 11000, 4,
                "The clouds have come down onto the road. 4 t of rice to Pahadgaon with 40 m of visibility. Follow the guard stones.");
            m15.fog = true; l.Add(m15);
            var m16 = Cargo("m16", "Traffic Jam", "Cement in heavy traffic", 1, "depot", "summit", "ghat_up", CargoType.Cement, 5f, 22f, 12000, 4,
                "Tourist season: buses and trucks everywhere. Get 5 t of cement up without a scratch – damage over 25% and the job is off.");
            m16.traffic = 2; m16.maxDamage = 25f; l.Add(m16);
            var m17 = Cargo("m17", "Raat Ka Ghat", "Night cement run", 1, "depot", "summit", "ghat_up", CargoType.Cement, 4f, 20f, 12000, 4,
                "The bridge site needs cement by morning. Climb the ghat at night with 4 t – headlights on, hairpins appear out of nowhere.");
            m17.night = true; l.Add(m17);
            var m18 = Cargo("m18", "Ghaas Ka Pahad", "Hay mountain", 1, "depot", "summit", "ghat_up", CargoType.Hay, 2f, 18f, 10000, 4,
                "2 t of hay heaped far above the trolley sides. Light but tall and wobbly – lose more than 10% and the farmer won't pay.");
            m18.maxSpill = 0.10f; l.Add(m18);
            var m19 = Cargo("m19", "Das Ton", "10 t of cement", 1, "depot", "summit", "ghat_up", CargoType.Cement, 10f, 30f, 20000, 5,
                "Ten tonnes on the tandem trolley up an 11% pitch. Only a strong tractor will make it – choose wisely.");
            l.Add(m19);
            var m20 = Cargo("m20", "Himalayan Legend", "The final run", 1, "depot", "summit", "ghat_up", CargoType.Bricks, 9f, 28f, 35000, 5,
                "Night. Fog. Heavy traffic. 9 t of bricks on the tandem. Spill ≤ 10%, damage ≤ 30%. Finish this and you are a legend of the ghat.");
            m20.night = true; m20.fog = true; m20.traffic = 2; m20.maxSpill = 0.10f; m20.maxDamage = 30f; l.Add(m20);
            return l.ToArray();
        }

        // ---------------- progress ----------------
        public static bool Done(int i) { var d = SaveSystem.Data; return d != null && (d.missionMask & (1L << i)) != 0; }
        public static int DoneCount() { int c = 0; for (int i = 0; i < Count; i++) if (Done(i)) c++; return c; }
        public static bool Unlocked(int i) { return i <= DoneCount() + 2 || Done(i); }
        public static float Best(int i) { var d = SaveSystem.Data; return d != null && d.missionBest != null && i < d.missionBest.Length ? d.missionBest[i] : 0f; }

        public static void MarkDone(int i, float seconds)
        {
            var d = SaveSystem.Data;
            d.missionMask |= 1L << i;
            if (d.missionBest == null || d.missionBest.Length < Count) { var nb = new float[Count]; if (d.missionBest != null) System.Array.Copy(d.missionBest, nb, d.missionBest.Length); d.missionBest = nb; }
            if (d.missionBest[i] <= 0f || seconds < d.missionBest[i]) d.missionBest[i] = seconds;
            SaveSystem.SaveProgress();
        }
    }
}
