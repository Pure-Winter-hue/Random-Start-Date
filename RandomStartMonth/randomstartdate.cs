using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace RandomStartDate
{
    public class ModEntry : ModSystem
    {
        private ICoreServerAPI sapi;
        private const string SaveKeyApplied = "randomstartdate.applied";
        private const string CfgFile = "randomstartdate.json";
        private Config cfg;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            LoadOrCreateConfig();

            // After the world loads but BEFORE any player can join:
            api.Event.SaveGameLoaded += () =>
            {
                if (ReadBool(SaveKeyApplied)) return;

                api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
                {
                    try
                    {
                        var cal = sapi.World.Calendar;
                        if (cal == null) return;

                        if (IsVanillaFreshStart(cal))
                        {
                            ApplyConfiguredStart(cal);
                            WriteBool(SaveKeyApplied, true);
                            sapi.Logger.Notification("[RandomStartDate] Applied during RunGame (pre-join).");
                        }
                        else
                        {
                            sapi.Logger.Event("[RandomStartDate] Skipped: world not at vanilla fresh start.");
                        }
                    }
                    catch (Exception e)
                    {
                        sapi.Logger.Error("[RandomStartDate] RunGame apply failed: {0}", e);
                    }
                });
            };
        }

        // ---- Core -----------------------------------------------------------

        private void ApplyConfiguredStart(IGameCalendar cal)
        {
            // Calendar parameters (uniform months)
            float H = Math.Max(1f, cal.HoursPerDay);      // usually 24
            int D = Math.Max(1, cal.DaysPerMonth);        // usually 9 (server-configurable)
            int M = 12;

            // Resolve month (0..11)
            int monthIndex = cfg.RandomizeMonth
                ? sapi.World.Rand.Next(0, M)
                : Clamp(cfg.FixedMonth - 1, 0, M - 1);          // FixedMonth is human 1..12

            // Resolve day index (0..D-1)
            int dayIndex = cfg.RandomizeDay
                ? sapi.World.Rand.Next(0, D)
                : Clamp(cfg.FixedDay - 1, 0, D - 1);            // FixedDay is human 1..D

            // Resolve hour (0..H-1), integer hours
            int maxHour = Math.Max(1, (int)Math.Floor(H));
            int hour = cfg.RandomizeHour
                ? sapi.World.Rand.Next(0, maxHour)
                : Clamp(cfg.FixedHour, 0, maxHour - 1);

            double monthHours = D * H;
            double yearHours = M * monthHours;

            // Target this year = month start + dayIndex*H + hour
            double targetThisYear = monthIndex * monthHours + dayIndex * H + hour;

            double now = cal.TotalHours;
            double yearsPassed = Math.Floor(now / yearHours);
            double curYearStart = yearsPassed * yearHours;

            // If already past that point in the current year, schedule for next year so we never go backward
            double targetAbs = (now - curYearStart > targetThisYear - 0.001)
                ? (yearsPassed + 1) * yearHours + targetThisYear
                : curYearStart + targetThisYear;

            double delta = targetAbs - now;
            if (delta > 0.001f)
            {
                cal.Add((float)delta);
                sapi.Logger.Event($"[RandomStartDate] month={monthIndex} (0=Jan), day={dayIndex + 1}, hour={hour}; advanced +{delta:F1}h. (DPM={D}, HPD={H})");
            }
            else
            {
                sapi.Logger.Event("[RandomStartDate] No-op: target not ahead.");
            }
        }

        private bool IsVanillaFreshStart(IGameCalendar cal)
        {
            float H = Math.Max(1f, cal.HoursPerDay);
            int D = Math.Max(1, cal.DaysPerMonth);
            int M = 12;

            double monthHours = D * H;
            double yearHours = M * monthHours;

            double now = cal.TotalHours;
            int curMonth = (int)Math.Floor((now % yearHours) / monthHours); // 0..11
            double hoursIntoMonth = now - Math.Floor(now / monthHours) * monthHours;
            int curDayIndex = (int)Math.Floor(hoursIntoMonth / H);          // 0-based
            double hoursIntoDay = hoursIntoMonth - curDayIndex * H;

            // Vanilla new worlds start May 1, early morning-ish.
            return curMonth == 4 && curDayIndex == 0 && hoursIntoDay < 12.0;
        }

        // ---- Config ---------------------------------------------------------

        private void LoadOrCreateConfig()
        {
            cfg = sapi.LoadModConfig<Config>(CfgFile);
            if (cfg == null)
            {
                cfg = Config.Default();
                sapi.StoreModConfig(cfg, CfgFile);
                sapi.Logger.Event($"[RandomStartDate] Created default config '{CfgFile}'.");
            }

            // Basic sanity clamps (handles users editing JSON)
            cfg.FixedMonth = Clamp(cfg.FixedMonth, 1, 12);
            cfg.FixedDay = Math.Max(1, cfg.FixedDay);
            cfg.FixedHour = Clamp(cfg.FixedHour, 0, 23); // will clamp against HoursPerDay at runtime
        }

        // ---- Utils ----------------------------------------------------------

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        private bool ReadBool(string key)
        {
            byte[] data = sapi.WorldManager.SaveGame.GetData(key);
            return data != null && SerializerUtil.Deserialize<bool>(data);
        }

        private void WriteBool(string key, bool val)
        {
            sapi.WorldManager.SaveGame.StoreData(key, SerializerUtil.Serialize(val));
        }
    }

    // ---- Config DTO ---------------------------------------------------------

    public class Config
    {
        // Month
        public bool RandomizeMonth { get; set; } = true;
        /// <summary> 1..12 when RandomizeMonth=false </summary>
        public int FixedMonth { get; set; } = 1;

        // Day
        public bool RandomizeDay { get; set; } = true;
        /// <summary> 1..(DaysPerMonth) when RandomizeDay=false </summary>
        public int FixedDay { get; set; } = 1;

        // Hour
        public bool RandomizeHour { get; set; } = true;
        /// <summary> 0..23 (clamped to HoursPerDay-1 at runtime) when RandomizeHour=false </summary>
        public int FixedHour { get; set; } = 6;

        public static Config Default() => new Config
        {
            RandomizeMonth = true,
            FixedMonth = 1,
            RandomizeDay = true,
            FixedDay = 1,
            RandomizeHour = true,
            FixedHour = 6
        };
    }
}
