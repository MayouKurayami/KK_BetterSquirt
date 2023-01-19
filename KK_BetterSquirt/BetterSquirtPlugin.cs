using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI;
using KKAPI.MainGame;
using System;
using System.ComponentModel;

namespace KK_BetterSquirt
{
	[BepInPlugin(GUID, PluginName, Version)]
	[BepInProcess(KoikatuAPI.GameProcessName)]
	[BepInProcess(KoikatuAPI.VRProcessName)]
	[BepInProcess(KoikatuAPI.GameProcessNameSteam)]
	[BepInProcess(KoikatuAPI.VRProcessNameSteam)]
	[BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    public class BetterSquirtPlugin : BaseUnityPlugin
    {
        public const string PluginName = "KK_BetterSquirt";

        public const string GUID = "MK.KK_BetterSquirt";

        public const string Version = "1.0.1";

        internal static new ManualLogSource Logger;
		internal static ConfigEntry<SquirtMode> SquirtBehavior { get; private set; }
		internal static ConfigEntry<KeyboardShortcut> SquirtKey { get; private set; }
		internal static ConfigEntry<int> TouchChance { get; private set; }
		internal static ConfigEntry<bool> SquirtHD { get; private set; }
		internal static ConfigEntry<Behavior> SquirtDuration { get; private set; }
		internal static ConfigEntry<Behavior> SquirtAmount { get; private set; }

		private void Awake()
        {
            Logger = base.Logger;

			SquirtBehavior = Config.Bind(
				section: "",
				key: "Squirt Behavior",
				defaultValue: SquirtMode.Aroused,
				"Vanilla: Use game's default behavior: only squirting in caress mode." +
				"\n\nIf Girls is Aroused: Girl squirts during orgasm if excitement gauge is over 70" +
				"\n\nAlways: Girl always squirts during orgasm");

			SquirtKey = Config.Bind(
				section: "",
				key: "Squirt Shortcut Key",
				defaultValue: KeyboardShortcut.Empty,
				"Key to manually trigger squirting");

			TouchChance = Config.Bind(
				section: "",
				key: "Touch Sensitivity",
				defaultValue: 30,
				new ConfigDescription("Chance of triggering squirts by touching the girl's pussy." +
				"\nSet to 0 to disable this feature.",
					new AcceptableValueRange<int>(0, 100)));

			SquirtHD = Config.Bind(
				section: "Better Squirt",
				key: "Enable Improved Particles",
				defaultValue: true,
				"Replaces vanilla squirt with a more realistic one");
			SquirtHD.SettingChanged += (sender, args) =>
			{
				if (GameAPI.InsideHScene)
					BetterSquirtController.InitParticles(		
						FindObjectOfType(Type.GetType("VRHScene, Assembly-CSharp") ?? Type.GetType("HSceneProc, Assembly-CSharp")));
			};

			SquirtDuration = Config.Bind(
				section: "Better Squirt",
				key: "Manual Squirt Duration",
				defaultValue: Behavior.Auto,
				"Duration of the squirts when triggered manually by the hotkey." +
				"\nIn auto mode it depends on girl's excitement gauge and other contextual factors");

			SquirtAmount = Config.Bind(
				section: "Better Squirt",
				key: "Manual Squirt Amount",
				defaultValue: Behavior.Auto,
				"Amount and volume of the squirts when triggered manually by the hotkey." +
				"\nIn auto mode it depends on girl's excitement gauge and other contextual factors");



			GameAPI.RegisterExtraBehaviour<BetterSquirtController>(GUID);
			var harmonyInstance = Harmony.CreateAndPatchAll(typeof(BetterSquirtHooks), GUID);

			BetterSquirtHooks.PatchVRHooks(harmonyInstance);
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
	}
}
