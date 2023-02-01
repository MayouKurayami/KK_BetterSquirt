using HarmonyLib;
using KKAPI.MainGame;
using KKAPI.Utilities;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using Random = UnityEngine.Random;
using static UnityEngine.ParticleSystem;
using StrayTech;
using Unity.Linq;
using Illusion.Game;
using static HParticleCtrl;
using static HandCtrl;
using static KK_BetterSquirt.BetterSquirtPlugin;
using System.Reflection;

namespace KK_BetterSquirt
{
	public class BetterSquirtController : GameCustomFunctionController
	{
		public static List<ParticleGroup> ParticleGroups { get; private set; }
		public static BetterSquirtController Instance { get; private set; }
		private static HFlag flags { get; set; }
		private static object _randSio;
		private static HVoiceCtrl _hVoiceCtrl;
		private static Vector2 _lastDragVector;

		private static float _touchAmount = 0;

		private const float DURATION_FULL = 4.8f;
		private const float DURATION_MIN = 1f;
		private const float TOUCH_THRESHOLD = 7f;
		private const string ASSETBUNDLE = "addcustomeffect.unity3d";
		private const string ASSETNAME = "SprayRefractive";

		public class ParticleGroup
		{		
			public ParticleSystem VanillaParticle { get; set; }
			public ParticleSystem NewParticle { get; set; }
			public MonoBehaviour Hand { get; set; }
			public float Cooldown { get; set; }
		}

		private void Awake()
		{
			Instance = this;
		}

		private void Update()
		{
			if (flags == null)
				return;

			if (Input.GetKeyDown(SquirtKey.Value.MainKey) && SquirtKey.Value.Modifiers.All(x => Input.GetKey(x)))
			{
				Squirt(softSE: true, trigger: TriggerType.Manual);
			}
				
			if (flags.drag)
				OnDrag();

			foreach (var particle in ParticleGroups)
			{
				if (particle.Cooldown > 0)
					particle.Cooldown -= Time.deltaTime;
			}		

			if (_touchAmount > 0)
				_touchAmount -= Time.deltaTime;
		}


		protected override void OnStartH(BaseLoader proc, HFlag hFlag, bool vr)
		{
			flags = hFlag;
			var procTraverse = Traverse.Create(proc);

			procTraverse
				.Field("lstProc")
				.GetValue<List<HActionBase>>()
				.OfType<HAibu>()
				.FirstOrDefault()
				.GetFieldValue("randSio", out _randSio);

			_hVoiceCtrl = procTraverse.Field("voice").GetValue<HVoiceCtrl>();
			_lastDragVector = new Vector2(0.5f, 0.5f);

			InitParticles(proc);		
		}


		internal static bool InitParticles(object proc)
		{
			if (!GameAPI.InsideHScene)
			{
				BetterSquirtPlugin.Logger.LogDebug("Not in H. No particles to initialize");
				return false;
			}

			//Before adding particles, destroy existing particles created by this plugin to prevent duplicates
			if (ParticleGroups != null)
			{
				foreach (ParticleGroup particleGroup in ParticleGroups)
				{
					if (particleGroup.NewParticle != null)
						Destroy(particleGroup.NewParticle.gameObject);
				}
			}		

			ParticleGroups = new List<ParticleGroup>();

			Traverse procTraverse = Traverse.Create(proc);

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


			GameObject asset = GetParticleAsset();			

			for (int i = 0; i < vanillaParticles.Count; i++)
			{
				if (vanillaParticles[i] == null || handCtrls[i] == null)
				{
					BetterSquirtPlugin.Logger.LogError("Null reference to vanilla ParticleSystem or HandCtrl. List index mismatch?");
					continue;
				}				

				GameObject newGameObject = Instantiate(asset);
				newGameObject.name = asset.name;
				newGameObject.transform.SetParent(vanillaParticles[i].transform.parent);
				//Adjust the position and rotation of the new Particle System to make sure the stream comes out of the manko at the right place and the right angle
				newGameObject.transform.localPosition = new Vector3(-0.01f, 0f, 0f);
				newGameObject.transform.localRotation = Quaternion.Euler(new Vector3(60f, 0, 0f));
				newGameObject.transform.localScale = Vector3.one;

				ParticleGroups.Add(new ParticleGroup() { 
					VanillaParticle = vanillaParticles[i], 
					Hand = handCtrls[i], 
					NewParticle = newGameObject.GetComponent<ParticleSystem>() });
			}

			if (ParticleGroups.Count() == 0)
			{
				BetterSquirtPlugin.Logger.LogDebug("Failed to initialize particles");
				return false;
			}

			//If loaded asset is not destroyed, the game would crash upon leaving H scene due to memory spike
			Destroy(asset);

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
				BetterSquirtPlugin.Logger.LogError("Particles resources not found");
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
				BetterSquirtPlugin.Logger.LogError("WaterStreamEffectCnstSpd5 not found. Check unity3d file");

			//fix obfuscation by pantyhose
			foreach (var renderer in asset.GetComponentsInChildren<ParticleSystemRenderer>())
				renderer.material.renderQueue = 3080;

			return asset;
		}


		/// <param name="handCtrl">If specified, only the ParticleGroup with the matching HandCtrl object will be triggered for squirting. This essentially selects which female should squirt</param>
		internal static bool Squirt(bool softSE, TriggerType trigger, bool sound = true, MonoBehaviour handCtrl = null)
		{
			bool anyParticlePlayed = false;
			Type handType = Type.GetType("VRHandCtrl, Assembly-CSharp") ?? Type.GetType("HandCtrl, Assembly-CSharp");
			MethodInfo hitReactionPlayInfo = AccessTools.Method(handType, "HitReactionPlay", new Type[] { typeof(AibuColliderKind), typeof(bool) });
			Utils.Sound.Setting setting = new Utils.Sound.Setting
			{
				type = Manager.Sound.Type.GameSE3D,
				assetBundleName = softSE ? @"sound/data/se/h/00/00_00.unity3d" : @"sound/data/se/h/12/12_00.unity3d",
				assetName = softSE ? "khse_10" : "hse_siofuki",
			};


			for (int i = 0; i < ParticleGroups.Count; i++)
			{
				//prevent overly frequent squirts caused by touch spamming
				if (ParticleGroups[i].Cooldown > 0 && trigger == TriggerType.Touch)
					continue;

				if (handCtrl != null && ParticleGroups[i].Hand != handCtrl)
					continue;

				ParticleSystem particle = SquirtHD.Value ? ParticleGroups[i].NewParticle : ParticleGroups[i].VanillaParticle;
				if (particle == null)
				{
					BetterSquirtPlugin.Logger.LogError("Null ParticleSystem in ParticleGroups list");
					continue;
				}				
	
				//Default to full duration in case vanilla squirt is run
				float duration = DURATION_FULL;
				//Cache HVoiceCtrl.Voice to prevent race condition between the for loop iterator and coroutine 
				HVoiceCtrl.Voice voiceState = _hVoiceCtrl.nowVoices[i];
				Transform soundReference = particle.transform.parent;
				var hitReactionPlayDel = (Func<AibuColliderKind, bool, bool>)Delegate.CreateDelegate(
					typeof(Func<AibuColliderKind, bool, bool>), ParticleGroups[i].Hand, hitReactionPlayInfo);


				if (SquirtHD.Value)
				{
					GenerateSquirtParameters(trigger, out duration, out int streamCount);
					AnimationCurve emissionCurve = GenerateSquirtPattern(duration, out AnimationCurve speedCurve, out List<float> burstTimes);

					//Magic numbers for the minimum and maximum initial velocity are purely based on taste
					ApplyCurve(particle.gameObject, emissionCurve, speedCurve, 5.5f, 6.5f);
					foreach (GameObject gameObject in particle.gameObject.Children())
					{
						if (gameObject.name == "SubWaterStreamEffectCnstSpd5")
						{
							AnimationCurve subCurve = GenerateSquirtPattern(duration, out AnimationCurve subSpeed, out _);
							ApplyCurve(gameObject, subCurve, subSpeed, 5.5f, 6.5f);
							gameObject.SetActive(streamCount > 1);
						}
						else if (gameObject.name == "SideWaterStreamEffectCnstSpd5")
						{
							ApplyCurve(gameObject, speedCurve, 4.5f, 5.5f);
							gameObject.SetActive(streamCount > 2);
						}
					}

					//Things to do at each burst
					Action burstActions = null;

					if (sound)
					{
						if (softSE)
							//If playing the soft and short sound effect, play it once at each burst.
							burstActions += () => PlaySetting(setting, soundReference);		
						else
							//If playing the regular long sound effect, just play it once since it cannot be synchronized to the bursts
							PlaySetting(setting, soundReference);
					}

					//Do not play twitch animation during orgasm, since orgasm already has its own animation
					if (trigger != TriggerType.Orgasm)
						burstActions += () => hitReactionPlayDel(AibuColliderKind.reac_bodydown, voiceState.state != HVoiceCtrl.VoiceKind.voice);

					if (burstActions != null)
						Instance.StartCoroutine(OnEachBurst(burstActions, burstTimes));
				}
				else
				//If improved squirt is not enabled, play the sound effect and twitch animation once since there are no bursts to synchronize to
				{
					if (sound)
						PlaySetting(setting, soundReference);

					//Do not play twitch animation during orgasm, since orgasm already has its own animation
					if (trigger != TriggerType.Orgasm)
						hitReactionPlayDel(AibuColliderKind.reac_bodydown, voiceState.state != HVoiceCtrl.VoiceKind.voice);
				}

				particle.Simulate(0f);
				particle.Play();
				anyParticlePlayed = true;
				ParticleGroups[i].Cooldown = duration;
			}

			if (!anyParticlePlayed)
			{
				BetterSquirtPlugin.Logger.LogDebug("Could not initialize squirting");
				return false;
			}
			
			return true;
		}

		private static IEnumerator OnEachBurst(Action action, List<float> burstTimes)
		{
			float lastTime = 0;
	
			foreach (float burstTime in burstTimes)
			{
				//Delay each delegate invocation by the difference in time between the timestamps of each burst
				yield return new WaitForSeconds(burstTime - lastTime);
				action.Invoke();
				lastTime = burstTime;
			}

			yield return null;
		}

		private static bool PlaySetting(Utils.Sound.Setting setting, Transform referenceInfo)
		{
			Transform soundsource = Utils.Sound.Play(setting);
			if (soundsource != null)
			{
				soundsource.transform.SetParent(referenceInfo, false);
				return true;
			}
			else
			{
				BetterSquirtPlugin.Logger.LogError("Failed to play sound effect");
				return false;
			}
		}

		private static void GenerateSquirtParameters(TriggerType trigger, out float duration, out int streamCount)
		{
			//Default values here are for when behavior is set to random, so we don't need to check for that  
			float min = DURATION_MIN;
			float max = DURATION_FULL;
			//UnityEngine.Random.Range() for int is ([inclusive], [exclusive])
			streamCount = Random.Range(1, 4);
			bool isAroused = flags.gaugeFemale >= 70f;

			//Magic numbers below can be adjusted to taste
			if (trigger == TriggerType.Orgasm)
			{
				min = !isAroused ? 3.5f : DURATION_FULL;
				max = DURATION_FULL;
				streamCount = !isAroused ? Random.Range(1, 3) : 3;
			}
			else if (trigger == TriggerType.Touch)
			{
				min = !isAroused ? 1 : 2f;
				max = !isAroused ? 1.9f : 2.69f;
				streamCount = !isAroused ? 1 : Random.Range(1, 3);
			}
			else
			{
				switch (SquirtDuration.Value)
				{
					case Behavior.Maximum:
						min = max = DURATION_FULL;
						break;

					case Behavior.Minimum:
						min = max = DURATION_MIN;
						break;

					case Behavior.Auto:
						min = !isAroused ? DURATION_MIN : 2.7f;
						max = !isAroused ? 2.5f : DURATION_FULL;
						break;
				}

				switch (SquirtAmount.Value)
				{
					case Behavior.Maximum:
						streamCount = 3;
						break;

					case Behavior.Minimum:
						streamCount = 1;
						break;

					case Behavior.Auto:
						streamCount = flags.gaugeFemale < 70f ? Random.Range(1, 3) : Random.Range(2, 4);
						break;
				}
			}

			duration = Random.Range(min, max);
		}


		private static AnimationCurve GenerateSquirtPattern(float duration, out AnimationCurve initSpeed, out List<float> burstTimes)
		{
			//Each AnimationCurve here is a pre-defined pattern of squirting according to taste.
			var patterns = new AnimationCurve[]
			{
				new AnimationCurve(
					new Keyframe(0.1f/DURATION_FULL, Random.Range(1f, 1.2f)),
					new Keyframe(0.25f/DURATION_FULL, Random.Range(1f, 1.2f)),
					new Keyframe(0.6f/DURATION_FULL, 0),
					new Keyframe(DURATION_MIN/DURATION_FULL, Random.Range(0, 0.1f))),

				new AnimationCurve(
					new Keyframe(0.3f/DURATION_FULL, Random.Range(0.6f, 0.8f)),
					new Keyframe(1.5f/DURATION_FULL, Random.Range(0.2f, 0.4f))),

				new AnimationCurve(
					new Keyframe(0.2f/DURATION_FULL, Random.Range(0.8f, 1)), 
					new Keyframe(2.4f/DURATION_FULL, Random.Range(0.9f, 1.2f)),
					new Keyframe(2.7f/DURATION_FULL, Random.Range(0.1f, 0.3f)))

			};

			//Time value for each Keyframe is normalized between 0 and 1 relative to the total duration of the particle system.
			//So we need to normalize the time value given by the caller
			duration /= DURATION_FULL;
			AnimationCurve emissionCurve = new AnimationCurve();
			initSpeed = new AnimationCurve();
			burstTimes = new List<float>();
			float timeElapsed = 0;

			//Pick patterns at random and add them to the output AnimationCurve until duration specified by the caller is met, or the maximum duration (normalized as 1) is met
			while (timeElapsed < Mathf.Min(duration, 1f))
			{
				//Get all the patterns whose length would fit within the remaining time, and pick one randomly
				List<AnimationCurve> patternRoster = patterns.Where(c => c.keys.Last().time <= (duration - timeElapsed)) .ToList();
				if (patternRoster.Count() == 0) 
					break;
				AnimationCurve pattern = patternRoster[Random.Range(0, patternRoster.Count())];

				foreach (Keyframe key in pattern.keys)
				{
					emissionCurve.AddKey(key.time + timeElapsed, key.value);
					//Clamp the value of the initial velocity within a subjectively reasonable range to lessen the fluctuation of the emission speed, which adds a bit more realism(TM)
					initSpeed.AddKey(key.time + timeElapsed, Mathf.Clamp(key.value, 0.5f, 1f));
				}
				//Use the first Keyframe of the pattern and some more magic numbers as thresholds to determine whether the pattern is "bursty" enough
				if ((pattern.keys.First().time < 0.3f && pattern.keys.First().value > 0.9f) || timeElapsed == 0)
					burstTimes.Add(timeElapsed * DURATION_FULL);

				timeElapsed += pattern.keys.Last().time;
			}
			//Smoothly end the squirting AnimationCurve by bringing the emission down to 0 after a 0.5 second ramp down
			emissionCurve.AddKey(duration + (0.5f / DURATION_FULL), 0);
			initSpeed.AddKey(duration + (0.5f / DURATION_FULL), 0);

			return emissionCurve;
		}

		/// <summary>
		/// Apply two independent AnimationCurves to the emission and initial velocity of all ParticleSystems of a GameObject and its children, with the initial velocity's multipler picked randomly between a given range
		/// </summary>
		/// <param name="minSpeed">lower bound of the possible multiplier value of the initial velocity curve</param>
		/// <param name="maxSpeed">upper bound of the possible multiplier value of the initial velocity curve</param>
		private static void ApplyCurve(GameObject particleGameObject, AnimationCurve emissionCurve, AnimationCurve speedCurve, float minSpeed, float maxSpeed )
		{
			foreach (var particle in particleGameObject.GetComponentsInChildren<ParticleSystem>(true))
			{
				if (particle.main.duration == DURATION_FULL)
				{
					EmissionModule emission = particle.emission;
					float multiplier = emission.rateOverTimeMultiplier;
					emission.rateOverTime = new MinMaxCurve(multiplier, emissionCurve);
				}
			}
			ApplyCurve(particleGameObject, speedCurve, minSpeed, maxSpeed);
		}

		/// <summary>
		/// Apply an AnimationCurve to the initial velocity of all ParticleSystems of a GameObject and its children, with a multipler picked randomly between a given range
		/// </summary>
		/// <param name="minSpeed">lower bound of the possible multiplier value of the initial velocity curve</param>
		/// <param name="maxSpeed">upper bound of the possible multiplier value of the initial velocity curve</param>
		private static void ApplyCurve(GameObject particleGameObject , AnimationCurve speedCurve, float minSpeed, float maxSpeed)
		{
			foreach (var particle in particleGameObject.GetComponentsInChildren<ParticleSystem>(true))
			{
				if (particle.main.duration == DURATION_FULL && particle.main.startSpeed.mode != ParticleSystemCurveMode.Constant)
				{
					MainModule main = particle.main;
					main.startSpeed = new MinMaxCurve(Random.Range(minSpeed, maxSpeed), speedCurve);		
				}
			}
		}
		
		

		internal static bool CheckOrgasmSquirtCondition()
		{
			return GameAPI.InsideHScene && (SquirtBehavior.Value == SquirtMode.Always || (SquirtBehavior.Value == SquirtMode.Aroused && flags.gaugeFemale > 70f));
		}

		internal static bool AibuSquirtBypass(GlobalMethod.ShuffleRand shuffleRandInstance)
		{
			return shuffleRandInstance == _randSio && SquirtBehavior.Value != SquirtMode.Vanilla;
		}



		/// <summary>
		/// Accumulate the amount the user has dragged the vagina for and check if it has exceeded the threshold for the squirting procedure to proceed.
		/// Not applicable in 3P
		/// </summary>
		private static void OnDrag()
		{		
			Vector2 currentDrag = flags.xy[(int)AibuColliderKind.kokan - 2];
			//truncate the x and y values to two decimal places to prevent superfluous values being added to _touchAmount caused by the imprecise nature of floats
			_touchAmount += ((int)(Mathf.Abs(currentDrag.y - _lastDragVector.y) * 100) + (int)(Mathf.Abs(currentDrag.x - _lastDragVector.x) * 100)) / 100f;
			_lastDragVector = currentDrag;

			if (_touchAmount > TOUCH_THRESHOLD)
			{
				_touchAmount = 0;
				if (TouchChanceCalc())
					Squirt(softSE: true, trigger: TriggerType.Touch);		
			}	
		}

		internal static void OnBoop(MonoBehaviour handCtrl, AibuColliderKind touchArea)
		{
			if (touchArea == AibuColliderKind.reac_bodydown && TouchChanceCalc())
			{
				Squirt(softSE: true, trigger: TriggerType.Touch, handCtrl: handCtrl);			
			}
		}

		internal static void OnCaressStart(MonoBehaviour handCtrl)
		{
			if (TouchChanceCalc() &&
				Traverse.Create(handCtrl).Field("selectKindTouch").GetValue<AibuColliderKind>() == AibuColliderKind.kokan)
			{
				Squirt(softSE: true, trigger: TriggerType.Touch, handCtrl: handCtrl);		
			}					
		}

		internal static void OnCaressClick(int _area)
		{
			//check if the vagina area was clicked and if the click happened in the current frame, to make sure this only runs once per click 
			if (_area == (int)AibuColliderKind.kokan - 2 && Input.GetMouseButtonDown(0))
			{
				//run squirt every (number added below / TOUCH_THRESHOLD) clicks to prevent too much squirting caused by click spamming
				_touchAmount += 1.5f;

				if (_touchAmount > TOUCH_THRESHOLD)
				{	
					_touchAmount = 0;
					if (TouchChanceCalc())
						Squirt(softSE: true, trigger: TriggerType.Touch);
				}
			}
		}

		/// <summary>
		/// If the girl is not aroused, scale the user configued probability of touch triggered squirts to a curve with concavity determined by the excitement gauge value.
		/// This prevents excessive squirting when the girl is barely aroused.
		/// </summary>
		private static bool TouchChanceCalc()
		{
			int random = Random.Range(0, 100);

			if (flags.gaugeFemale > 70f)
				return random < TouchChance.Value;
			else
			{
				//Modify TouchChance.Value by (-a^5x)/(x-a^5-1), with "a" being the excitement gauge value converted between 0.55 to 1.4
				float multiplier = (flags.gaugeFemale / 70f) * (1.4f - 0.55f) + 0.55f;

				//Alledgely faster than Math.Pow(). Not actually tested. In StackOverflow we trust.
				for (int i = 0; i < 5; i++)
					multiplier *= multiplier;

				float chanceNormal = TouchChance.Value / 100f;
				float chanceMod = (0 - multiplier) * chanceNormal / (chanceNormal - multiplier - 1);

				return random < chanceMod * 100;
			}
		}


		internal enum TriggerType
		{
			Manual,
			Orgasm,
			Touch
		}
	}
}
