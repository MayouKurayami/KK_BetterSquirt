using HarmonyLib;
using System;
using UnityEngine;
using static HandCtrl;

namespace KK_BetterSquirt
{
	internal static class BetterSquirtHooks
	{
		internal static void PatchVRHooks(Harmony harmonyInstance)
		{
			Type vrHandType = Type.GetType("VRHandCtrl, Assembly-CSharp");
			if (vrHandType == null)
				return;
			harmonyInstance.Patch(
				AccessTools.Method(vrHandType, "Reaction"),
				prefix: new HarmonyMethod(typeof(BetterSquirtHooks), nameof(ReactionPre)));
			harmonyInstance.Patch(
				AccessTools.Method(vrHandType, nameof(HandCtrl.JudgeProc)),
				prefix: new HarmonyMethod(typeof(BetterSquirtHooks), nameof(JudgeProcPre)));

		}

		
		/// <summary>
		/// If all conditions are met, call Squirt() when the game enters any orgasm animation
		/// </summary>
		[HarmonyPostfix]
		[HarmonyPatch(typeof(HActionBase), nameof(HActionBase.SetPlay))]
		public static void SetPlayPost(string _nextAnimation)
		{
			if (BetterSquirtController.CheckOrgasmSquirtCondition())
			{
				if (_nextAnimation.Contains("Start"))
					BetterSquirtController.Squirt(sound: false, trigger: BetterSquirtController.TriggerType.Orgasm);
				else if (_nextAnimation == "Orgasm") //masturbation orgasm
					BetterSquirtController.Squirt(sound: true, trigger: BetterSquirtController.TriggerType.Orgasm);
			}
		}

		/// <summary>
		/// Vanilla game starts playing the squirt particles by calling HParticleCtrl.Play() with a parameter of 2, which we want to skip and instead run Squirt() if we want to customize the animation curve of the squirting pattern
		/// </summary>
		[HarmonyPrefix]
		[HarmonyPatch(typeof(HParticleCtrl), nameof(HParticleCtrl.Play))]
		public static bool ParticleCtrlPlayPre(ref bool __result, int _particle)
		{
			if (_particle == 2 && BetterSquirtPlugin.SquirtHD.Value)
			{
				__result = BetterSquirtController.Squirt(sound: false, trigger: BetterSquirtController.TriggerType.Orgasm);
				return false;
			}		
			return true;			
		}

		/// <summary>
		/// Vanilla game decides whether the girl squirts or not by calling GlobalMethod.ShuffleRand.Get() on the field HAibu.randSio
		/// This affects whether the game plays any sound effects as well so we need to override the result to fully control the squirting behavior
		/// </summary>
		[HarmonyPrefix]
		[HarmonyPatch(typeof(GlobalMethod.ShuffleRand), nameof(GlobalMethod.ShuffleRand.Get))]
		public static bool GetRandSioPre(ref int __result, GlobalMethod.ShuffleRand __instance)
		{		
			if (BetterSquirtController.CheckOrgasmSquirtCondition(__instance))
			{
				__result = 1;
				return false;
			}
			return true;
		}

		/// <summary>
		/// When the girl flinches, from touching during intercourse or touching non-caress body parts during aibu, a.k.a. "spank".
		/// </summary>
		[HarmonyPrefix]
		[HarmonyPatch(typeof(HandCtrl), "Reaction")]
		public static void ReactionPre(MonoBehaviour __instance, int _kindTouch)
		{
			BetterSquirtController.OnBoop(__instance, (AibuColliderKind)_kindTouch);
		}

		/// <summary>
		/// When caress is started by touching one of the caress body parts, and when clicking those body parts intermittently
		/// </summary>
		[HarmonyPrefix]
		[HarmonyPatch(typeof(HandCtrl), "JudgeProc")]
		public static void JudgeProcPre(MonoBehaviour __instance)
		{
			BetterSquirtController.OnCaressStart(__instance);
		}

		// When clicking a caress body part rapidly, the game stays in "drag mode" and thus not triggering JudgeProc(). 
		// HFlag.SetSelectArea() is called several times per click under that condition. 
		// (redundant triggering of squirts are prevented by additional checks in OnCaressClick()).
		// This situation is only relevant when not in VR
		[HarmonyPrefix]
		[HarmonyPatch(typeof(HFlag), "SetSelectArea")]
		public static void SetSelectAreaPre(int _area)
		{
			BetterSquirtController.OnCaressClick(_area);
		}
	}
}
