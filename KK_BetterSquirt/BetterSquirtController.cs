using HarmonyLib;
using KKAPI.MainGame;
using KKAPI.Utilities;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using Random = UnityEngine.Random;
using StrayTech;
using static KK_BetterSquirt.BetterSquirt;
using static HParticleCtrl;
using static HandCtrl;

namespace KK_BetterSquirt
{
	public class BetterSquirtController : GameCustomFunctionController
	{
		public static List<CharaSquirt> CharaSquirtList { get; private set; }

		private const string ASSETBUNDLE = "addcustomeffect.unity3d";
		private const string ASSETNAME = "SprayRefractive";

		private static object _randSio;
		private static Vector2 _lastDragVector;
		

		private void Update()
		{
			if (Flags == null)
				return;

			if (Input.GetKeyDown(Cfg_SquirtKey.Value.MainKey) && Cfg_SquirtKey.Value.Modifiers.All(x => Input.GetKey(x)))
			{
				RunSquirts(softSE: true, trigger: TriggerType.Manual);
			}
				
			if (Flags.drag)
				OnDrag();
		}


		protected override void OnStartH(BaseLoader proc, HFlag hFlag, bool vr)
		{
			Flags = hFlag;
			var procTraverse = Traverse.Create(proc);

			procTraverse
				.Field("lstProc")
				.GetValue<List<HActionBase>>()
				.OfType<HAibu>()
				.FirstOrDefault()
				.GetFieldValue("randSio", out _randSio);

			_lastDragVector = new Vector2(0.5f, 0.5f);

			InitParticles(proc);
		}


		public static bool InitParticles(BaseLoader proc)
		{
			if (!GameAPI.InsideHScene)
			{
				BetterSquirt.Logger.LogDebug("Not in H. No particles to initialize");
				return false;
			}

			//Before adding particles, destroy existing particles created by this plugin to prevent duplicates
			if (CharaSquirtList != null)
			{
				foreach (CharaSquirt charaSquirt in CharaSquirtList)
				{
					if (charaSquirt.NewParticle != null)
						Destroy(charaSquirt.NewParticle.gameObject);
				}
			}		

			CharaSquirtList = new List<CharaSquirt>();

			Traverse procTraverse = Traverse.Create(proc);
			HVoiceCtrl hVoiceCtrl = procTraverse.Field("voice").GetValue<HVoiceCtrl>();
			GameObject asset = GetParticleAsset();

			//Get vanilla ParticleSystems that are not null, so theoretically there would be one ParticleSystem per female.
			//"particle" corresponds to the first female, and "particle1" corresponds to the second female if she exists
			string[] particleFields = { "particle", "particle1" };
			List<ParticleSystem> vanillaParticles = particleFields
				.Select(field => procTraverse.Field(field).GetValue<HParticleCtrl>())
				.Select(partCtrl => Traverse.Create(partCtrl).Field("dicParticle").GetValue<Dictionary<int, ParticleInfo>>())
				.Where(dic => dic != null && dic.TryGetValue(2, out ParticleInfo pInfo) && pInfo?.particle != null)
				.Select(dic => dic[2].particle)
				.ToList();

			//Get HandCtrl objects. Each one corresponds to each female
			List<MonoBehaviour> handCtrls = new List<MonoBehaviour>();
			if (Type.GetType("VRHScene, Assembly-CSharp") == null)
			{
				string[] handCtrlFields = { "hand", "hand1" };
				handCtrls = handCtrlFields.Select(field => procTraverse.Field(field).GetValue<MonoBehaviour>()).ToList();
			}
			else
				handCtrls = procTraverse.Field("vrHands").GetValue<MonoBehaviour[]>().ToList();
						

			for (int i = 0; i < vanillaParticles.Count; i++)
			{
				if (vanillaParticles[i] == null || handCtrls[i] == null)
				{
					BetterSquirt.Logger.LogError("Null reference to vanilla ParticleSystem or HandCtrl. List index mismatch?");
					continue;
				}				

				GameObject newGameObject = Instantiate(asset);
				newGameObject.name = asset.name;
				newGameObject.transform.SetParent(vanillaParticles[i].transform.parent);
				//Adjust the position and rotation of the new Particle System to make sure the stream comes out of the manko at the right place and the right angle
				newGameObject.transform.localPosition = new Vector3(-0.01f, 0f, 0f);
				newGameObject.transform.localRotation = Quaternion.Euler(new Vector3(60f, 0, 0f));
				newGameObject.transform.localScale = Vector3.one;
			
				var charaSquirt = proc.gameObject.AddComponent<CharaSquirt>();
				charaSquirt.Init(vanillaParticles[i], newGameObject.GetComponent<ParticleSystem>(), handCtrls[i], hVoiceCtrl.nowVoices[i]);
				CharaSquirtList.Add(charaSquirt);
			}

			//If loaded asset is not destroyed, the game would crash upon leaving H scene due to memory spike
			Destroy(asset);

			if (CharaSquirtList.Count() == 0)
			{
				BetterSquirt.Logger.LogDebug("Failed to initialize particles");
				return false;
			}
		
			return true;
		}

		/// <summary>
		/// Load particles resources into a GameObject and modify it to prepare for squirting
		/// </summary>
		private static GameObject GetParticleAsset()
		{
			//Attempt to load particle asset from zipmod first before loading the embedded resource
			//"clone" is set to true to allow the loaded asset to be destroyed after being loaded (to prevent crash) without causing cab-string conflict the next time the asset is loaded
			GameObject asset = CommonLib.LoadAsset<GameObject>($"studio/{ASSETBUNDLE}", ASSETNAME, clone: true);
			//If failed to load from zipmod, load from embedded resource
			if (asset == null)
			{
				AssetBundle bundle = AssetBundle.LoadFromMemory(ResourceUtils.GetEmbeddedResource(ASSETBUNDLE));
				asset = bundle.LoadAsset<GameObject>(ASSETNAME);
				bundle.Unload(false);
			}
			if (asset == null || asset.GetComponent<ParticleSystem>() == null)
			{
				BetterSquirt.Logger.LogError("Particles resources not found");
				return null;
			}

			foreach (var particle in asset.GetComponentsInChildren<ParticleSystem>())
			{
				var main = particle.main;
				main.loop = false;
				main.playOnAwake = false;
				if (main.duration < DURATION_FULL)
					main.duration = DURATION_FULL;
			}

			//Create two additional water streams that can be toggled on/off later on
			//One overlaps with the original stream to enhance its intensity or to split off vertically, while the other goes off at a slightly off angle to split off horizontally
			GameObject stream = asset.FindChild("WaterStreamEffectCnstSpd5");
			if (stream != null)
			{
				GameObject side = Instantiate(stream, parent: asset.transform);
				side.name = "SideWaterStreamEffectCnstSpd5";
				side.transform.localRotation = Quaternion.Euler(new Vector3(0f, -1, 0f));

				GameObject sub = Instantiate(stream, parent: asset.transform);
				sub.name = "SubWaterStreamEffectCnstSpd5";
				stream.transform.localRotation = sub.transform.localRotation = Quaternion.Euler(new Vector3(0f, 1, 0f));
			}
			else
				BetterSquirt.Logger.LogError("WaterStreamEffectCnstSpd5 not found. Check unity3d file");

			//fix obfuscation by pantyhose
			foreach (var renderer in asset.GetComponentsInChildren<ParticleSystemRenderer>())
				renderer.material.renderQueue = 3080;

			return asset;
		}



		/// <summary>
		/// Iterate through the CharaSquirt objects and make them squirt according to the parameters
		/// </summary>
		/// <param name="handCtrl">If specified, only the CharaSquirt with the matching HandCtrl object will be triggered for squirting. This essentially selects which female should squirt</param>
		/// <param name="setTouchCooldown">If triggered by touch, this parameter determines if simply the attempt to squirt will add cooldown before the next attempt is allowed. Useful to prevent spamming</param>
		public static bool RunSquirts(bool softSE, TriggerType trigger, bool sound = true, MonoBehaviour handCtrl = null, bool setTouchCooldown = true)
		{
			bool anySquirtFired = false;
			
			foreach(CharaSquirt charaSquirt in CharaSquirtList)
			{
				//This makes sure that squirt only runs on the specific HandCtrl (female) that's associated with the ParticleSystem.
				//Skip if there is only one charaSquirt loaded, meaning that there is only one female.
				//In VR the different hand objects are associated with each controller instead of each female, so running this check when there is only one female would break squirting. When there are more than one female in VR, touch and caress are completely broken anyway so running this check doesn't matter.
				if (CharaSquirtList.Count > 1 && handCtrl != null && charaSquirt.Hand != handCtrl)
					continue;

				if (trigger == TriggerType.Touch)
				{
					if (charaSquirt.TouchCooldown > 0)
						continue;

					if (setTouchCooldown)
						charaSquirt.TouchCooldown = COOLDOWN_PER_TOUCH;

					//Skip squirting if triggered by touch and RNGesus says no
					if (!TouchChanceCalc())
						continue;
				}

				anySquirtFired = charaSquirt.Squirt(softSE, trigger, sound) || anySquirtFired;
			}

			return anySquirtFired;
		}


		/// <summary>
		/// If the girl is not aroused, scale the user configued probability of touch triggered squirts to a curve with concavity determined by the excitement gauge value.
		/// This prevents excessive squirting when the girl is barely aroused.
		/// </summary>
		private static bool TouchChanceCalc()
		{
			int random = Random.Range(0, 100);

			if (Flags.gaugeFemale > 70f)
				return random < Cfg_TouchChance.Value;
			else
			{
				//Modify TouchChance.Value by (-a^5x)/(x-a^5-1), with "a" being the excitement gauge value converted between 0.55 to 1.4
				float multiplier = (Flags.gaugeFemale / 70f) * (1.4f - 0.55f) + 0.55f;

				//Alledgely faster than Math.Pow(). Not actually tested. In StackOverflow we trust.
				for (int i = 0; i < 5; i++)
					multiplier *= multiplier;

				float chanceNormal = Cfg_TouchChance.Value / 100f;
				float chanceMod = (0 - multiplier) * chanceNormal / (chanceNormal - multiplier - 1);

				return random < chanceMod * 100;
			}
		}

		/// <summary>
		/// Accumulate the amount the user has dragged the vagina for and check if it has exceeded the threshold for the squirting procedure to proceed.
		/// Not applicable in 3P
		/// </summary>
		private static void OnDrag()
		{
			Vector2 currentDrag = Flags.xy[(int)AibuColliderKind.kokan - 2];

			if (Mathf.Abs(currentDrag.y - _lastDragVector.y) > 0.02f || Mathf.Abs(currentDrag.x - _lastDragVector.x) > 0.02f)
			{
				RunSquirts(softSE: true, trigger: TriggerType.Touch);
			}
			_lastDragVector = currentDrag;
		}

		private static bool CheckOrgasmSquirtCondition()
		{
			return GameAPI.InsideHScene && (Cfg_Behavior.Value == SquirtMode.Always || (Cfg_Behavior.Value == SquirtMode.Aroused && Flags.gaugeFemale > 70f));
		}



		internal static class Hooks
		{
			internal static void PatchVRHooksIfVR(Harmony harmonyInstance)
			{
				Type vrHandType = Type.GetType("VRHandCtrl, Assembly-CSharp");
				if (vrHandType == null)
					return;

				harmonyInstance.Patch(
					AccessTools.Method(vrHandType, "Reaction"),
					prefix: new HarmonyMethod(typeof(Hooks), nameof(OnBoop)));
				harmonyInstance.Patch(
					AccessTools.Method(vrHandType, nameof(HandCtrl.JudgeProc)),
					prefix: new HarmonyMethod(typeof(Hooks), nameof(OnCaressStart)));
			}

			
			[HarmonyPostfix]
			[HarmonyPatch(typeof(HActionBase), nameof(HActionBase.SetPlay))]
			private static void OnOrgasm(string _nextAnimation)
			{
				if (CheckOrgasmSquirtCondition())
				{
					//Intercourse orgasms. Game already plays ejaculation sounds so no need for squirting sound effect
					if (_nextAnimation.Contains("Start"))
						RunSquirts(sound: false, softSE: false, trigger: TriggerType.Orgasm);
					//Masturbation orgasm. play the long & loud sound effect to match with aibu orgasm
					else if (_nextAnimation == "Orgasm")
						RunSquirts(sound: true, softSE: false, trigger: TriggerType.Orgasm);
				}
			}

			// Vanilla game decides whether the girl squirts or not by calling GlobalMethod.ShuffleRand.Get() on the field HAibu.randSio
			// This affects whether the game plays any sound effects as well so we need to override the result to fully control the squirting behavior
			[HarmonyPrefix]
			[HarmonyPatch(typeof(GlobalMethod.ShuffleRand), nameof(GlobalMethod.ShuffleRand.Get))]
			private static bool OnCaressOrgasmCheck(ref int __result, GlobalMethod.ShuffleRand __instance)
			{
				if (__instance == _randSio && Cfg_Behavior.Value != SquirtMode.Vanilla)
				{
					__result = CheckOrgasmSquirtCondition() ? 1 : 0;
					return false;
				}
				else
					return true;
			}

			// During caress mode, vanilla game starts playing the squirt particles by calling HParticleCtrl.Play() with a parameter of 2,
			// which we want to skip and instead run Squirt() if we want to customize the animation curve of the squirting pattern
			[HarmonyPrefix]
			[HarmonyPatch(typeof(HParticleCtrl), nameof(HParticleCtrl.Play))]
			private static bool OnCaressOrgasmPlay(ref bool __result, int _particle)
			{
				if (_particle == 2 && Cfg_SquirtHD.Value)
				{
					__result = RunSquirts(sound: false, softSE: false, trigger: TriggerType.Orgasm);
					return false;
				}
				return true;
			}


			#region Trigger squirts by touch/caress
			//There are three scenarios where squirts can be triggered by touch, each taken care of by a hook below (see comment for each)

			//In each of these scenarios, we need to know which body part is being touched since we are only interested in the vagina.
			//And if the scenario happens in 3P, we also need to know which female is being touched.


			//When the girl flinches, from touching during intercourse or touching non-caress body parts during aibu, a.k.a. "spank" or "boop"
			//__instance will allow us to know which female is being touched, and is casted from HandCtrl or VRHandCtrl to MonoBehavior to ensure VR compatibility.
			[HarmonyPrefix]
			[HarmonyPatch(typeof(HandCtrl), "Reaction")]
			private static void OnBoop(MonoBehaviour __instance, int _kindTouch)
			{
				if (_kindTouch == (int)AibuColliderKind.reac_bodydown)
					//Don't set touch cooldown per touch, since this action is unlikely to be spammed unless the intent is to squirt
					RunSquirts(softSE: true, trigger: TriggerType.Touch, handCtrl: __instance, setTouchCooldown: false);
			}


			//When caress is started by touching one of the caress body parts, and after that when clicking those body parts no more frequently than one second per click
			//__instance will allow us to know which female is being touched, and is casted from HandCtrl or VRHandCtrl to MonoBehavior to ensure VR compatibility.
			// This does not happen in 3P
			[HarmonyPrefix]
			[HarmonyPatch(typeof(HandCtrl), "JudgeProc")]
			private static void OnCaressStart(MonoBehaviour __instance)
			{
				if (Traverse.Create(__instance).Field("selectKindTouch").GetValue<AibuColliderKind>() == AibuColliderKind.kokan)
					RunSquirts(softSE: true, trigger: TriggerType.Touch);
			}


			// After caress is already started, clicking a caress body part rapidly with each click less than one second apart
			// HFlag.SetSelectArea() is the only hook point I could find that provides the necessary information for which body part is being touched without having to deal with reflection mess.
			// But it is called several times per click, so redundant triggering of squirts are prevented by additional checks in OnCaressClick()
			// Does not happen in VR or 3P
			[HarmonyPrefix]
			[HarmonyPatch(typeof(HFlag), "SetSelectArea")]
			private static void OnCaressRapidClick(int _area)
			{
				//check if the vagina area was clicked and if the click happened in the current frame, to make sure this only runs once per click 
				if (_area != (int)AibuColliderKind.kokan - 2 || !Input.GetMouseButtonDown(0))
					return;

				RunSquirts(softSE: true, trigger: TriggerType.Touch);
			}

			#endregion

		}
	}
}
