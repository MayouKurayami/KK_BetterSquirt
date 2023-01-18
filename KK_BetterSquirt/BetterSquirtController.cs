using HarmonyLib;
using KKAPI.MainGame;
using KKAPI.Utilities;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static UnityEngine.ParticleSystem;
using Illusion.Game;
using static HParticleCtrl;
using static KK_BetterSquirt.BetterSquirtPlugin;

namespace KK_BetterSquirt
{
	public class BetterSquirtController : GameCustomFunctionController
	{
		private static List<ParticleInfo> SquirtParticleInfos { get; set; }
		private static HFlag Hflag { get; set; }
		private static object _randSio;
		private static bool _fancyParticlesLoaded = false;

		//3 seconds minimum duration for squirting, just because
		private const float _duration = 3f;
		private const string ASSETBUNDLE = "addcustomeffect.unity3d";
		private const string ASSETNAME = "SprayRefractive";

		
		private void Update()
		{
			if (!GameAPI.InsideHScene)
				return;

			if (Input.GetKeyDown(SquirtKey.Value.MainKey) && SquirtKey.Value.Modifiers.All(x => Input.GetKey(x)))
				Squirt(softSE: true);
		}


		protected override void OnStartH(BaseLoader proc, HFlag hFlag, bool vr)
		{
			Hflag = hFlag;
			Traverse
				.Create(proc)
				.Field("lstProc")
				.GetValue<List<HActionBase>>()
				.OfType<HAibu>()
				.FirstOrDefault()
				.GetFieldValue("randSio", out _randSio);

			SquirtParticleInfos = GetSquirtParticleInfo(proc);

			if (SquirtHD.Value) 
				UpdateParticles(SquirtParticleInfos);
		}


		internal static List<ParticleInfo> GetSquirtParticleInfo(object proc)
		{
			if (!GameAPI.InsideHScene)
			{
				BetterSquirtPlugin.Logger.LogDebug("Not in H. No particles to access");
				return null;
			}		
			string[] fields = { "particle", "particle1" };

			var particleInfoList = fields
				.Select(field => Traverse.Create(proc).Field(field).GetValue<HParticleCtrl>())
				.Where(partCtrl => partCtrl != null)
				.Select(partCtrl => Traverse.Create(partCtrl).Field("dicParticle").GetValue<Dictionary<int, ParticleInfo>>())
				.Where(dic => dic != null && dic.TryGetValue(2, out ParticleInfo pInfo) && pInfo?.particle != null)
				.Select(dic => dic[2]);

			if (particleInfoList.Count() == 0)
			{
				BetterSquirtPlugin.Logger.LogDebug("Failed to access squirt ParticleInfo");
				return null;
			}

			return particleInfoList.ToList();			
		}


		internal static bool UpdateParticles(List<ParticleInfo> particleInfos)
		{
			if (!GameAPI.InsideHScene)
			{
				BetterSquirtPlugin.Logger.LogDebug("Not in H. No particles to update");
				return false;
			}

			if (SquirtHD.Value)
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
					if (main.duration < _duration)
						main.duration = _duration;
				}
				//fix obfuscation by pantyhose
				foreach (var renderer in asset.GetComponentsInChildren<ParticleSystemRenderer>())
					renderer.material.renderQueue = 3080;
			

				foreach (var particleInfo in particleInfos)
				{
					GameObject newGameObject = Instantiate(asset);
					newGameObject.name = asset.name;
					
					GameObject oldGameObject = particleInfo.particle.gameObject;
					newGameObject.transform.SetParent(particleInfo.particle.gameObject.transform.parent);

					//Adjust the position and rotation of the new Particle System to make sure the stream comes out of the manko at the right place and the right angle
					newGameObject.transform.localPosition = new Vector3(-0.01f, 0f, 0f);
					newGameObject.transform.localRotation = Quaternion.Euler(new Vector3(60f, 0, 0f));
					newGameObject.transform.localScale = Vector3.one;
					particleInfo.particle = newGameObject.GetComponent<ParticleSystem>();
					Destroy(oldGameObject);
				}

				_fancyParticlesLoaded = true;
				Destroy(asset);
			}
			else
			{
				foreach (var particleInfo in particleInfos)
				{
					GameObject vanillaAsset = CommonLib.LoadAsset<GameObject>(particleInfo.assetPath, particleInfo.file, clone: true, string.Empty);
					Hflag.hashAssetBundle.Add(particleInfo.assetPath);
					if (vanillaAsset != null)
					{
						GameObject oldGameObject = particleInfo.particle.gameObject;
						vanillaAsset.transform.parent = oldGameObject.transform.parent;
						vanillaAsset.transform.localPosition = particleInfo.pos;
						vanillaAsset.transform.localRotation = Quaternion.Euler(particleInfo.rot);
						vanillaAsset.transform.localScale = Vector3.one;
						particleInfo.particle = vanillaAsset.GetComponent<ParticleSystem>();
						Destroy(oldGameObject);
					}
					else
					{
						BetterSquirtPlugin.Logger.LogError("Failed to load vanilla particles from asset");
						return false;
					}
				}
				_fancyParticlesLoaded = false;
			}

			return true;
		}

		internal static bool Squirt(bool sound = true, bool softSE = false)
		{
			bool anyParticlePlayed = false;

			//int femaleIndex = (flags.mode == HFlag.EMode.houshi3P || flags.mode == HFlag.EMode.sonyu3P) ? flags.nowAnimationInfo.id % 2 : 0;
			foreach (ParticleInfo particleInfo in SquirtParticleInfos)
			{
				if (particleInfo == null || particleInfo.particle == null)
				{
					BetterSquirtPlugin.Logger.LogError("Null reference in SquirtParticleInfos list");
					continue;
				}
				bool secondBurst = Random.Range(0, 100f) < 70f;

				if (SquirtHD.Value && _fancyParticlesLoaded)
				{
					AnimationCurve curve = new AnimationCurve();
					curve.AddKey(0, 1);
					//Coin flip between one of two patterns
					if (Random.Range(0, 100f) < 50)
					{
						curve.AddKey(0.1f, 0);
						curve.AddKey(0.4f, Random.Range(0, 0.1f));
						curve.AddKey(1f, 0);
					}
					else
					{
						curve.AddKey(0.1f, Random.Range(0.5f, 0.8f));
						curve.AddKey(0.4f, Random.Range(0, 0.4f));
						curve.AddKey(0.65f, 0);
					}				
					if (secondBurst)
						curve.AddKey(0.5f, Random.Range(0.8f, 1.2f));

					foreach (var particle in particleInfo.particle.gameObject.GetComponentsInChildren<ParticleSystem>())
					{
						//Find the particle systems that last "_duration" seconds, as those are the ones we want to customize the animation curves of.
						if (particle.main.duration == _duration)
						{
							EmissionModule emission = particle.emission;
							float multiplier = emission.rateOverTimeMultiplier;
							emission.rateOverTime = new MinMaxCurve(multiplier, curve);
						}					 
					}
					ParticleSystem streamParticle = particleInfo.particle.transform.Find("WaterStreamEffectCnstSpd5").GetComponent<ParticleSystem>();
					if (streamParticle == null)
					{
						BetterSquirtPlugin.Logger.LogError("ParticleSystem WaterStreamEffectCnstSpd5 not found. Check unity3d file");
					}
					else 
					{
						AnimationCurve speedCurve = new AnimationCurve();
						speedCurve.AddKey(0, Random.Range(0.8f, 1f));
						speedCurve.AddKey(0.49f, 0.3f);
						speedCurve.AddKey(1f, 0);
						if (secondBurst)
							speedCurve.AddKey(Random.Range(0.5f, 0.6f), Random.Range(0.8f, 1f));

						MainModule main = streamParticle.main;
						//A curve multiplier of 7.2 here would give us roughly the same maximum multiplier as originally defined in the asset. Adjust to taste.
						main.startSpeed = new MinMaxCurve(7.2f, speedCurve);
					}				
				}				
				particleInfo.particle.Simulate(0f);
				particleInfo.particle.Play();
				anyParticlePlayed = true;

				if (sound)
				{
					Utils.Sound.Setting setting = new Utils.Sound.Setting
					{
						type = Manager.Sound.Type.GameSE3D,
						assetBundleName = softSE ? @"sound/data/se/h/00/00_00.unity3d" : @"sound/data/se/h/12/12_00.unity3d",
						assetName = softSE ? "khse_10" : "hse_siofuki"
					};

					Transform soundsource = Utils.Sound.Play(setting);
					Transform referenceInfo = particleInfo.particle.transform.parent;
					if (soundsource != null)
						soundsource.transform.SetParent(referenceInfo, false);
					else
						BetterSquirtPlugin.Logger.LogError("Failed to play squirting sound effect");

					if (secondBurst && softSE)
					{
						setting.delayTime = _duration * 0.5f;
						Transform soundsource2 = Utils.Sound.Play(setting);
						soundsource2.transform.SetParent(referenceInfo, false);
					}
				}
			}
			if (!anyParticlePlayed)
			{
				BetterSquirtPlugin.Logger.LogError("Null references in SquirtParticleInfos list. Could not initialize squirting");
				return false;
			}
			
			return true;
		}

		internal static bool CheckSquirtCondition()
		{
			return GameAPI.InsideHScene && (SquirtBehavior.Value == SquirtMode.Always || (SquirtBehavior.Value == SquirtMode.Aroused && Hflag.gaugeFemale > 70f));
		}

		internal static bool CheckRandSioReference(object reference)
		{
			return reference == _randSio;
		}
	}
}
