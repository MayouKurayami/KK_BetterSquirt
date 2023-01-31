using HarmonyLib;
using KKAPI.MainGame;
using KKAPI.Utilities;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static UnityEngine.ParticleSystem;
using StrayTech;
using Unity.Linq;
using Illusion.Game;
using static HParticleCtrl;
using static KK_BetterSquirt.BetterSquirtPlugin;


namespace KK_BetterSquirt
{
	public class BetterSquirtController : GameCustomFunctionController
	{
		public static List<ParticleSystem> SquirtParticles { get; private set; }
		private static HFlag flags { get; set; }
		private static object _randSio;
		private static bool _fancyParticlesLoaded = false;
		private static Vector2 _lastDragVector;
		private static float _touchAmount = 0;
		private static float _squirtCooldown = 0;

		private const float DURATION_FULL = 4.8f;
		private const float DURATION_MIN = 1f;
		private const float TOUCH_THRESHOLD = 7f;
		private const string ASSETBUNDLE = "addcustomeffect.unity3d";
		private const string ASSETNAME = "SprayRefractive";


		private void Update()
		{
			if (flags == null)
				return;

			if (Input.GetKeyDown(SquirtKey.Value.MainKey) && SquirtKey.Value.Modifiers.All(x => Input.GetKey(x)))
				Squirt(softSE: true);

			if (flags.drag)
				OnDrag();

			if (_squirtCooldown > 0)
				_squirtCooldown -= Time.deltaTime;

			if (_touchAmount > 0)
				_touchAmount -= Time.deltaTime;
		}


		protected override void OnStartH(BaseLoader proc, HFlag hFlag, bool vr)
		{
			flags = hFlag;
			Traverse
				.Create(proc)
				.Field("lstProc")
				.GetValue<List<HActionBase>>()
				.OfType<HAibu>()
				.FirstOrDefault()
				.GetFieldValue("randSio", out _randSio);

			InitParticles(proc);
			_lastDragVector = new Vector2(0.5f, 0.5f);
		}

		protected override void OnEndH(BaseLoader proc, HFlag hFlag, bool vr)
		{
			_fancyParticlesLoaded = false;
		}


		internal static bool InitParticles(object proc)
		{
			if (!GameAPI.InsideHScene)
			{
				BetterSquirtPlugin.Logger.LogDebug("Not in H. No particles to update");
				return false;
			}

			string[] fields = { "particle", "particle1" };
			List<ParticleSystem> vanillaParticles = fields
				.Select(field => Traverse.Create(proc).Field(field).GetValue<HParticleCtrl>())
				.Where(partCtrl => partCtrl != null)
				.Select(partCtrl => Traverse.Create(partCtrl).Field("dicParticle").GetValue<Dictionary<int, ParticleInfo>>())
				.Where(dic => dic != null && dic.TryGetValue(2, out ParticleInfo pInfo) && pInfo?.particle != null)
				.Select(dic => dic[2].particle)
				.ToList();

			if (vanillaParticles.Count() == 0)
			{
				BetterSquirtPlugin.Logger.LogDebug("Failed to access vanilla squirt particles");
				return false;
			}		
			
			if (!SquirtHD.Value)
			{
				foreach (ParticleSystem particle in SquirtParticles)
				{
					//var fancyParticle = particle.transform.parent.Cast<Transform>().FirstOrDefault(t => t.name == ASSETNAME);
					if (particle != null && particle.name == ASSETNAME)
						Destroy(particle.gameObject);
				}
				SquirtParticles = vanillaParticles;		
				_fancyParticlesLoaded = false;
			}
			else
			{
				//Attempt to load from zipmod first
				GameObject asset = CommonLib.LoadAsset<GameObject>($"studio/{ASSETBUNDLE}", ASSETNAME);
				//If failed to load from zipmod, load from embedded resource
				if (asset == null)
				{
					AssetBundle bundle = AssetBundle.LoadFromMemory(ResourceUtils.GetEmbeddedResource(ASSETBUNDLE));
					asset = bundle.LoadAsset<GameObject>(ASSETNAME);
					bundle.Unload(false);
				}
				if (asset == null || asset.GetComponent<ParticleSystem>() == null)
				{
					BetterSquirtPlugin.Logger.LogError("Particles resource was not found. Fancy squirt not loaded");
					return false;
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


				SquirtParticles = new List<ParticleSystem>();
				foreach (var particle in vanillaParticles)
				{
					GameObject newGameObject = Instantiate(asset);
					newGameObject.name = asset.name;
					newGameObject.transform.SetParent(particle.transform.parent);
					//Adjust the position and rotation of the new Particle System to make sure the stream comes out of the manko at the right place and the right angle
					newGameObject.transform.localPosition = new Vector3(-0.01f, 0f, 0f);
					newGameObject.transform.localRotation = Quaternion.Euler(new Vector3(60f, 0, 0f));
					newGameObject.transform.localScale = Vector3.one;
					SquirtParticles.Add(newGameObject.GetComponent<ParticleSystem>());
				}

				_fancyParticlesLoaded = true;
			}

			return true;
		}


		internal static bool Squirt(bool sound = true, bool softSE = false, TriggerType trigger = TriggerType.Manual, ChaControl female = null)
		{
			if (_squirtCooldown > 0 && trigger != TriggerType.Manual)
				return false;
			
			bool anyParticlePlayed = false;
			//Default values here are for when behavior is set to random, so we don't need to check for that  
			float min = DURATION_MIN;
			float max = DURATION_FULL;
			int streamCount = Random.Range(1, 4);
			bool isAroused = flags.gaugeFemale >= 70f;
			

			//int femaleIndex = (flags.mode == HFlag.EMode.houshi3P || flags.mode == HFlag.EMode.sonyu3P) ? flags.nowAnimationInfo.id % 2 : 0;
			foreach (ParticleSystem particle in SquirtParticles)
			{
				if (particle == null)
				{
					BetterSquirtPlugin.Logger.LogError("Null reference in SquirtParticleInfos list");
					continue;
				}
				if (female != null && !particle.transform.IsChildOf(female.transform))
					continue;

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
					max = !isAroused ? 2 : 2.7f;
					streamCount = 1;
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
							//UnityEngine.Random.Range() for int is ([inclusive], [exclusive])
							streamCount = flags.gaugeFemale < 70f ? Random.Range(1, 3) : Random.Range(2, 4);
							break;
					}
				}
				float duration = Random.Range(min, max);
				AnimationCurve emissionCurve = GenerateSquirtPattern(duration, out AnimationCurve speedCurve, out List<float> burstTimes);

				if (SquirtHD.Value && _fancyParticlesLoaded)
				{
					//Magic numbers for the minimum and maximum initial velocity are purely based on taste
					ApplyCurve(particle.gameObject, emissionCurve, speedCurve, 5.5f, 6.5f);

					foreach (GameObject gameObject in particle.gameObject.Children())
					{
						if (gameObject.name == "SubWaterStreamEffectCnstSpd5")
						{
							AnimationCurve subCurve = GenerateSquirtPattern(duration, out AnimationCurve subSpeed);
							ApplyCurve(gameObject, subCurve, subSpeed, 5.5f, 6.5f);
							gameObject.SetActive(streamCount > 1);
						}
						else if (gameObject.name == "SideWaterStreamEffectCnstSpd5")
						{
							ApplyCurve(gameObject, speedCurve, 4.5f, 5.5f);
							gameObject.SetActive(streamCount > 2);
						}
					}
				}

				particle.Simulate(0f);
				particle.Play();
				anyParticlePlayed = true;
				_squirtCooldown = Mathf.Max(_squirtCooldown, duration);		

				if (sound)
				{
					Utils.Sound.Setting setting = new Utils.Sound.Setting
					{
						type = Manager.Sound.Type.GameSE3D,
						assetBundleName = softSE ? @"sound/data/se/h/00/00_00.unity3d" : @"sound/data/se/h/12/12_00.unity3d",
						assetName = softSE ? "khse_10" : "hse_siofuki",
					};
					Transform referenceInfo = particle.transform.parent;

					if (softSE && SquirtHD.Value)
					{
						foreach (float time in burstTimes)
						{
							setting.delayTime = time;
							if (!PlaySetting(setting, referenceInfo))
								break;
						}
					}
					else
						PlaySetting(setting, referenceInfo);
				}
			}
			if (!anyParticlePlayed)
			{
				BetterSquirtPlugin.Logger.LogError("Null references in SquirtParticleInfos list. Could not initialize squirting");
				return false;
			}
			
			return true;
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
				if ((pattern.keys.First().time < 0.3f && pattern.keys.First().value > 0.7f) || timeElapsed == 0)
					burstTimes.Add(timeElapsed * DURATION_FULL);

				timeElapsed += pattern.keys.Last().time;
			}
			//Smoothly end the squirting AnimationCurve by bringing the emission down to 0 after a 0.5 second ramp down
			emissionCurve.AddKey(duration + (0.5f / DURATION_FULL), 0);
			initSpeed.AddKey(duration + (0.5f / DURATION_FULL), 0);

			return emissionCurve;
		}

		private static AnimationCurve GenerateSquirtPattern(float duration, out AnimationCurve initSpeed)
		{
			return GenerateSquirtPattern(duration, out initSpeed, out _);
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

		internal static bool CheckOrgasmSquirtCondition()
		{
			return GameAPI.InsideHScene && (SquirtBehavior.Value == SquirtMode.Always || (SquirtBehavior.Value == SquirtMode.Aroused && flags.gaugeFemale > 70f));
		}

		internal static bool CheckOrgasmSquirtCondition(GlobalMethod.ShuffleRand randSio)
		{
			return randSio == _randSio && CheckOrgasmSquirtCondition();
		}

		/// <summary>
		/// Accumulate the amount the user has dragged the vagina for and check if it has exceeded the threshold for the squirting procedure to proceed
		/// </summary>
		private static void OnDrag()
		{		
			Vector2 currentDrag = flags.xy[(int)HandCtrl.AibuColliderKind.kokan - 2];
			//truncate the x and y values to two decimal places to prevent superfluous values being added to _touchAmount caused by the imprecise nature of floats
			_touchAmount += ((int)(Mathf.Abs(currentDrag.y - _lastDragVector.y) * 100) + (int)(Mathf.Abs(currentDrag.x - _lastDragVector.x) * 100)) / 100f;
			_lastDragVector = currentDrag;

			if (_touchAmount > TOUCH_THRESHOLD)
			{
				_touchAmount = 0;
				if (Random.Range(0, 100) < TouchChance.Value && _squirtCooldown <= 0)
					Squirt(softSE: true, trigger: TriggerType.Touch);		
			}	
		}

		internal static void OnBoop(MonoBehaviour handCtrl, HandCtrl.AibuColliderKind touchArea)
		{
			if (touchArea == HandCtrl.AibuColliderKind.reac_bodydown && Random.Range(0, 100) < TouchChance.Value)
			{
				Squirt(softSE: true, trigger: TriggerType.Touch, female: Traverse.Create(handCtrl).Field("female").GetValue<ChaControl>());			
			}
		}


		internal static void OnCaressStart(MonoBehaviour handCtrl)
		{
			if (Random.Range(0, 100) < TouchChance.Value && 
				Traverse.Create(handCtrl).Field("selectKindTouch").GetValue<HandCtrl.AibuColliderKind>() == HandCtrl.AibuColliderKind.kokan)
			{
				Squirt(softSE: true, trigger: TriggerType.Touch, female: Traverse.Create(handCtrl).Field("female").GetValue<ChaControl>());		
			}					
		}

		//Only applicable when not in VR
		internal static void OnCaressClick(int _area)
		{
			//check if the vagina area was clicked and if the click happened in the current frame, to make sure this only runs once per click 
			if (_area == (int)HandCtrl.AibuColliderKind.kokan - 2 && Input.GetMouseButtonDown(0))
			{
				//run squirt every (number added below / TOUCH_THRESHOLD) clicks to prevent too much squirting caused by click spamming
				_touchAmount += 1.5f;

				if (_touchAmount > TOUCH_THRESHOLD)
				{	
					_touchAmount = 0;
					if (Random.Range(0, 100) < TouchChance.Value)
						Squirt(softSE: true, trigger: TriggerType.Touch);
				}
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
