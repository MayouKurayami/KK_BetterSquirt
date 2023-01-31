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

		
		// Detect when the game is entering orgasm by the next animation clip being played, and add squirting if conditions are met
		[HarmonyPostfix]
		[HarmonyPatch(typeof(HActionBase), nameof(HActionBase.SetPlay))]
		public static void SetPlayPost(string _nextAnimation)
		{
			if (BetterSquirtController.CheckOrgasmSquirtCondition())
			{
				//Intercourse orgasms. Game already plays ejaculation sounds so no need for squirting sound effect
				if (_nextAnimation.Contains("Start")) 
					BetterSquirtController.Squirt(sound: false, softSE: false, trigger: BetterSquirtController.TriggerType.Orgasm);
				//Masturbation orgasm. play the long & loud sound effect to match with aibu orgasm
				else if (_nextAnimation == "Orgasm") 
					BetterSquirtController.Squirt(sound: true, softSE: false, trigger: BetterSquirtController.TriggerType.Orgasm);
			}
		}

		// During caress mode, vanilla game starts playing the squirt particles by calling HParticleCtrl.Play() with a parameter of 2,
		// which we want to skip and instead run Squirt() if we want to customize the animation curve of the squirting pattern
		[HarmonyPrefix]
		[HarmonyPatch(typeof(HParticleCtrl), nameof(HParticleCtrl.Play))]
		public static bool ParticleCtrlPlayPre(ref bool __result, int _particle)
		{
			if (_particle == 2 && BetterSquirtPlugin.SquirtHD.Value)
			{
				__result = BetterSquirtController.Squirt(sound: false, softSE: false, trigger: BetterSquirtController.TriggerType.Orgasm);
				return false;
			}		
			return true;			
		}

		// Vanilla game decides whether the girl squirts or not by calling GlobalMethod.ShuffleRand.Get() on the field HAibu.randSio
		// This affects whether the game plays any sound effects as well so we need to override the result to fully control the squirting behavior
		[HarmonyPrefix]
		[HarmonyPatch(typeof(GlobalMethod.ShuffleRand), nameof(GlobalMethod.ShuffleRand.Get))]
		public static bool GetRandSioPre(ref int __result, GlobalMethod.ShuffleRand __instance)
		{		
			if (BetterSquirtController.AibuSquirtBypass(__instance))
			{
				__result = BetterSquirtController.CheckOrgasmSquirtCondition() ? 1 : 0;
				return false;
			}
			else
				return true;
		}



		#region Trigger squirts by touch/caress
		//There are three scenarios where squirts can be triggered by touch, each taken care of by a hook below (see comment for each)

		//In each of these scenarios, we need to know which body part is being touched since we are only interested in the vagina.
		//And if the scenario happens in 3P, we also need to know which female is being touched.


		//When the girl flinches, from touching during intercourse or touching non-caress body parts during aibu, a.k.a. "spank" or "boop"
		//__instance will allow us to know which female is being touched, and is casted from HandCtrl or VRHandCtrl to MonoBehavior to ensure cross compatibility.
		[HarmonyPrefix]
		[HarmonyPatch(typeof(HandCtrl), "Reaction")]
		public static void ReactionPre(MonoBehaviour __instance, int _kindTouch)
		{
			BetterSquirtController.OnBoop(__instance, (AibuColliderKind)_kindTouch);
		}


		//When caress is started by touching one of the caress body parts, and after that when clicking those body parts no more frequently than one second per click
		//__instance will allow us to know which female is being touched, and is casted from HandCtrl or VRHandCtrl to MonoBehavior to ensure cross compatibility.
		// This does not happen in 3P
		[HarmonyPrefix]
		[HarmonyPatch(typeof(HandCtrl), "JudgeProc")]
		public static void JudgeProcPre(MonoBehaviour __instance)
		{
			BetterSquirtController.OnCaressStart(__instance);
		}


		// After caress is already started, clicking a caress body part rapidly with each click less than one second apart
		// HFlag.SetSelectArea() is the only hook point I could find that provides the necessary information for which body part is being touched without having to deal with reflection mess.
		// But it is called several times per click, so redundant triggering of squirts are prevented by additional checks in OnCaressClick()
		// Does not happen in VR or 3P
		[HarmonyPrefix]
		[HarmonyPatch(typeof(HFlag), "SetSelectArea")]
		public static void SetSelectAreaPre(int _area)
		{
			BetterSquirtController.OnCaressClick(_area);
		}


		#endregion
	}
}
