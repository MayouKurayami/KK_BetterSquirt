using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI;
using KKAPI.MainGame;
using System.ComponentModel;

namespace KK_BetterSquirt
{
	[BepInPlugin(GUID, PluginName, Version)]
	[BepInProcess(KoikatuAPI.GameProcessName)]
	[BepInProcess(KoikatuAPI.VRProcessName)]
	[BepInProcess(KoikatuAPI.GameProcessNameSteam)]
	[BepInProcess(KoikatuAPI.VRProcessNameSteam)]
	[BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    public class BetterSquirt : BaseUnityPlugin
    {
        public const string PluginName = "KK_BetterSquirt";
        public const string GUID = "MK.KK_BetterSquirt";
        public const string Version = "1.0.1";

		internal const float DURATION_FULL = 4.8f;
		internal const float DURATION_MIN = 1f;
		internal const float COOLDOWN_PER_TOUCH = 1f;

		internal static new ManualLogSource Logger;
		internal static HFlag Flags;

		internal static ConfigEntry<SquirtMode> Cfg_Behavior { get; private set; }
		internal static ConfigEntry<KeyboardShortcut> Cfg_SquirtKey { get; private set; }
		internal static ConfigEntry<int> Cfg_TouchChance { get; private set; }
		internal static ConfigEntry<bool> Cfg_SquirtHD { get; private set; }
		internal static ConfigEntry<Behavior> Cfg_Duration { get; private set; }
		internal static ConfigEntry<Behavior> Cfg_Amount { get; private set; }

		private void Awake()
        {
			Logger = base.Logger;

			Cfg_Behavior = Config.Bind(
				section: "",
				key: "Squirt Behavior",
				defaultValue: SquirtMode.Aroused,
				"Default Behavior: Use the game's default behavior and only squirt occasionally during orgasm in caress mode" +
				"\n\nIf Girls is Aroused: Girl squirts during orgasm if her excitement gauge is over 70" +
				"\n\nAlways: Girl always squirts during orgasm");

			Cfg_SquirtKey = Config.Bind(
				section: "",
				key: "Squirt Shortcut Key",
				defaultValue: KeyboardShortcut.Empty,
				"Key to manually trigger squirting");

			Cfg_TouchChance = Config.Bind(
				section: "",
				key: "Touch Sensitivity",
				defaultValue: 25,
				new ConfigDescription("How frequently squirts are triggered when touching the girl's vagina/crotch. Affected by the girl's excitement gauge." +
				"\nSet to 0 to disable this feature",
					new AcceptableValueRange<int>(0, 100)));

			Cfg_SquirtHD = Config.Bind(
				section: "Better Squirt",
				key: "Improved Particles",
				defaultValue: true,
				"Replaces vanilla squirt with a more realistic one");

			Cfg_Duration = Config.Bind(
				section: "Better Squirt",
				key: "Manual Squirt Duration",
				defaultValue: Behavior.Auto,
				"Duration of the improved squirting when triggered manually by the hotkey" +
				"\n\nIn auto mode it depends on the girl's excitement gauge");

			Cfg_Amount = Config.Bind(
				section: "Better Squirt",
				key: "Manual Squirt Amount",
				defaultValue: Behavior.Auto,
				"Amount and volume of the improved squirting when triggered manually by the hotkey" +
				"\n\nIn auto mode it depends on the girl's excitement gauge");



			GameAPI.RegisterExtraBehaviour<BetterSquirtController>(GUID);
			var harmonyInstance = Harmony.CreateAndPatchAll(typeof(BetterSquirtController.Hooks), GUID);

			BetterSquirtController.Hooks.PatchVRHooksIfVR(harmonyInstance);
		}

		internal enum SquirtMode
		{
			[Description("Default Behavior")]
			Vanilla,
			[Description("If Girl is Aroused")]
			Aroused,
			Always
		}

		internal enum Behavior
		{
			Auto,
			Random,
			Minimum,
			Maximum
		}

		public enum TriggerType
		{
			Manual,
			Orgasm,
			Touch
		}
	}
}
