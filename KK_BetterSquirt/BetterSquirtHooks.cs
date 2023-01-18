using HarmonyLib;
using static HandCtrl;

namespace KK_BetterSquirt
{
	internal static class BetterSquirtHooks
	{
		/// <summary>
		/// If all conditions are met, call Squirt() when the game enters any orgasm animation
		/// </summary>
		[HarmonyPostfix]
		[HarmonyPatch(typeof(HActionBase), "SetPlay")]
		public static void SetPlayPost(string _nextAnimation)
		{
			if (BetterSquirtController.CheckSquirtCondition())
			{
				if (_nextAnimation.Contains("Start"))
					BetterSquirtController.Squirt(sound: false);
				else if (_nextAnimation == "Orgasm") //masturbation orgasm
					BetterSquirtController.Squirt(sound: true, softSE: false);
			}
		}

		/// <summary>
		/// Vanilla game starts playing the squirt particles by calling HParticleCtrl.Play() with a parameter of 2, which we want to skip and instead run Squirt() if we want to customize the animation curve of the squirting pattern
		/// </summary>
		[HarmonyPrefix]
		[HarmonyPatch(typeof(HParticleCtrl), "Play")]
		public static bool ParticleCtrlPlayPre(ref bool __result, int _particle)
		{
			if (_particle == 2 && BetterSquirtPlugin.SquirtHD.Value)
			{
				__result = BetterSquirtController.Squirt(sound: false);
				return false;
			}		
			return true;			
		}

		/// <summary>
		/// Vanilla game decides whether the girl squirts or not by calling GlobalMethod.ShuffleRand.Get() on the field HAibu.randSio
		/// This affects whether the game plays any sound effects as well so we need to override the result to fully control the squirting behavior
		/// </summary>
		[HarmonyPrefix]
		[HarmonyPatch(typeof(GlobalMethod.ShuffleRand), "Get")]
		public static bool GetRandSioPre(ref int __result, GlobalMethod.ShuffleRand __instance)
		{		
			if (BetterSquirtController.CheckSquirtCondition() && BetterSquirtController.CheckRandSioReference(__instance))
			{
				__result = 1;
				return false;
			}
			return true;
		}
	}
}
