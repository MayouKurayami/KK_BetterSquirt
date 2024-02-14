using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Random = UnityEngine.Random;
using static HandCtrl;
using static KK_BetterSquirt.BetterSquirt;
using static UnityEngine.ParticleSystem;
using Illusion.Game;
using Illusion.Extensions;

namespace KK_BetterSquirt
{
	public class CharaSquirt : MonoBehaviour
	{
		public ParticleSystem VanillaParticle { get; private set; }
		public ParticleSystem NewParticle { get; private set; }
		internal MonoBehaviour Hand { get; private set; }
		internal HVoiceCtrl.Voice Voice { get; private set; }

		private float _touchCoolDown;
		internal float TouchCooldown
		{
			get { return _touchCoolDown; }
			set
			{
				if (value < 0) throw new ArgumentOutOfRangeException();
				else _touchCoolDown = value;
			}
		}

		private static readonly MethodInfo hitReactionPlayInfo = AccessTools.Method(
			Type.GetType("VRHandCtrl, Assembly-CSharp") ?? typeof(HandCtrl), 
            nameof(HandCtrl.HitReactionPlay), 
#if KK
            new Type[] { typeof(int), typeof(bool) });
#else
            new Type[] { typeof(AibuColliderKind), typeof(bool) });
#endif


		internal void Init(ParticleSystem vanillaParticle, ParticleSystem newParticle, MonoBehaviour hand, HVoiceCtrl.Voice voice)
		{
			VanillaParticle = vanillaParticle;
			NewParticle = newParticle;
			Hand = hand;
			Voice = voice;
		}

		private void Update()
		{
			if (_touchCoolDown > 0)
				_touchCoolDown -= Time.deltaTime;
		}


		public bool Squirt(bool softSE, TriggerType trigger, bool sound = true)
		{
			ParticleSystem particle = Cfg_SquirtHD.Value ? NewParticle : VanillaParticle;
			if (particle == null)
			{
				BetterSquirt.Logger.LogError("Null ParticleSystem in charaSquirts list");
				return false;
			}

			//Default to full duration in case vanilla squirt is run
			float duration = DURATION_FULL;
			Transform soundReference = particle.transform.parent;
#if KK
			Utils.Sound.Setting setting = new Utils.Sound.Setting
			{
				type = Manager.Sound.Type.GameSE3D,
				assetBundleName = softSE ? @"sound/data/se/h/00/00_00.unity3d" : @"sound/data/se/h/12/12_00.unity3d",
				assetName = softSE ? "khse_10" : "hse_siofuki",
			};
#else
			Utils.Sound.Setting setting = new Utils.Sound.Setting(Manager.Sound.Type.GameSE3D)
			{
				bundle = @"sound/data/se/h/01.unity3d",
				asset = softSE ? "hse_ks_10" : "hse_ks_siofuki",
			};
#endif


			if (Cfg_SquirtHD.Value)
			{
				GenerateSquirtParameters(trigger, out duration, out int streamCount);
				GenerateSquirtPattern(duration, out AnimationCurve emissionCurve, out AnimationCurve speedCurve, out List<float> burstTimes);

				//Magic numbers for the minimum and maximum initial velocity are purely based on taste
				ApplyCurve(particle.gameObject, speedCurve, 5.5f, 6.5f, emissionCurve);
				foreach (GameObject gameObject in particle.gameObject.Children())
				{
					if (gameObject.name == "SubWaterStreamEffectCnstSpd5")
					{
						GenerateSquirtPattern(duration, out AnimationCurve subEmission, out AnimationCurve subSpeed, out _);
						ApplyCurve(gameObject, subSpeed, 5.5f, 6.5f, subEmission);
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
					burstActions += () =>
					hitReactionPlayInfo.Invoke(Hand, new object[] { (int)AibuColliderKind.reac_bodydown, CheckTwitchSECond() });

				if (burstActions != null)
					StartCoroutine(OnEachBurst(burstActions, burstTimes));
			}
			else
			//If improved squirt is not enabled, play the sound effect and twitch animation once since there are no bursts to synchronize to
			{
				if (sound)
					PlaySetting(setting, soundReference);

				//Do not play twitch animation during orgasm, since orgasm already has its own animation
				if (trigger != TriggerType.Orgasm)
					hitReactionPlayInfo.Invoke(Hand, new object[] { (int)AibuColliderKind.reac_bodydown, CheckTwitchSECond() });
			}

			particle.Simulate(0f);
			particle.Play();
			_touchCoolDown = duration;

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

		private bool CheckTwitchSECond()
		{
			if (Flags.nowAnimStateName == "SLoop" && Flags.speedCalc >= 0.5f)
				return false;
			else
				return Voice.state != HVoiceCtrl.VoiceKind.voice && Flags.nowAnimStateName != "OLoop";
		}

		private static bool PlaySetting(Utils.Sound.Setting setting, Transform referenceInfo)
		{
			Transform soundsource = Utils.Sound.Play(setting).transform;
			if (soundsource != null)
			{
				soundsource.transform.SetParent(referenceInfo, false);
				return true;
			}
			else
			{
				BetterSquirt.Logger.LogError("Failed to play sound effect");
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
			bool isAroused = Flags.gaugeFemale >= 70f;

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
				switch (Cfg_Duration.Value)
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

				switch (Cfg_Amount.Value)
				{
					case Behavior.Maximum:
						streamCount = 3;
						break;

					case Behavior.Minimum:
						streamCount = 1;
						break;

					case Behavior.Auto:
						streamCount = Flags.gaugeFemale < 70f ? Random.Range(1, 3) : Random.Range(2, 4);
						break;
				}
			}

			duration = Random.Range(min, max);
		}


		private static void GenerateSquirtPattern(float duration, out AnimationCurve emissionCurve, out AnimationCurve initSpeed, out List<float> burstTimes)
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
			emissionCurve = new AnimationCurve();
			initSpeed = new AnimationCurve();
			burstTimes = new List<float>();
			float timeElapsed = 0;

			//Pick patterns at random and add them to the output AnimationCurve until duration specified by the caller is met, or the maximum duration (normalized as 1) is met
			while (timeElapsed < Mathf.Min(duration, 1f))
			{
				//Get all the patterns whose length would fit within the remaining time, and pick one randomly
				List<AnimationCurve> patternRoster = patterns.Where(c => c.keys.Last().time <= (duration - timeElapsed)).ToList();
				if (patternRoster.Count() == 0)
					break;
				AnimationCurve pattern = patternRoster[Random.Range(0, patternRoster.Count())];

				foreach (Keyframe key in pattern.keys)
				{
					emissionCurve.AddKey(key.time + timeElapsed, key.value);
					//Clamp the value of the initial velocity within a subjectively reasonable range to lessen the fluctuation of the emission speed, which adds a bit more realism(TM)
					initSpeed.AddKey(key.time + timeElapsed, Mathf.Clamp(key.value, 0.5f, 1f));
				}
				//Use the first Keyframe of the pattern and some more magic numbers as thresholds to determine whether the pattern is "bursty" enough, then add the time of the Keyframe to the list of burst timestamps.
				//Or, unconditionally add the timestamp if timeElapsed is 0, since that means it's the beginning of squirt and we always want burst actions to run at that time
				if ((pattern.keys.First().time < 0.3f && pattern.keys.First().value > 0.9f) || timeElapsed == 0)
					burstTimes.Add(timeElapsed * DURATION_FULL);

				timeElapsed += pattern.keys.Last().time;
			}
			//Smoothly end the squirting AnimationCurve by bringing the emission down to 0 after a 0.5 second ramp down
			emissionCurve.AddKey(duration + (0.5f / DURATION_FULL), 0);
			initSpeed.AddKey(duration + (0.5f / DURATION_FULL), 0);
		}

		/// <summary>
		/// Apply independent AnimationCurves to the initial velocity and optionally emission of all ParticleSystems of a GameObject and its children, with the initial velocity's multipler picked randomly between a given range
		/// </summary>
		/// <param name="minSpeed">lower bound of the possible multiplier value of the initial velocity curve</param>
		/// <param name="maxSpeed">upper bound of the possible multiplier value of the initial velocity curve</param>
		private static void ApplyCurve(GameObject particleGameObject, AnimationCurve speedCurve, float minSpeed, float maxSpeed, AnimationCurve emissionCurve = null)
		{
			foreach (var particle in particleGameObject.GetComponentsInChildren<ParticleSystem>(true))
			{
				if (particle.main.duration == DURATION_FULL)
				{
					if (particle.main.startSpeed.mode != ParticleSystemCurveMode.Constant)
					{
						MainModule main = particle.main;
						main.startSpeed = new MinMaxCurve(Random.Range(minSpeed, maxSpeed), speedCurve);
					}

					if (emissionCurve != null)
					{
						EmissionModule emission = particle.emission;
						float multiplier = emission.rateOverTimeMultiplier;
						emission.rateOverTime = new MinMaxCurve(multiplier, emissionCurve);
					}
				}
			}
		}

	}
}
