// ============================================================
//  HealthDisplay.cs  —  血量显示插件（Unturned BepInEx 5）
//  作者：35117+Deepseek-v4-flash-0731
//  版本：v26.8.13.3
//
//  功能：
//   - 显示生物血量（数字 / 血条 / 两者），血条随距离缩放（有上下限）
//   - 显示位置：头顶 / 准星下方 / 屏幕角落
//   - 显示范围：所有 / 附近所有（可配距离）/ 视野内 / 准心附近 / 命中时 / 关闭
//   - 黑白名单过滤（生物选择器标签：僵尸 Z:类型名、动物 A:资产ID，支持 * 通配与玩家 P:SteamID）
//   - 隔墙检测：目标被遮挡时不显示血条（默认开启）
//   - 伤害数字：造成伤害时在目标上方飘出伤害数值
//   - 自己受到的伤害会以红色数字在屏幕中央提示
//
//  运行模式：
//   - 单机 / 主机（本机即服务器）：完整功能
//   - 客户端（连接他人服务器）：显示自己血量与受到的伤害
//   - 专用服务器：无 UI，不生效
// ============================================================
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using SDG.Unturned;
using UnityEngine;
using HarmonyLib;

namespace HealthDisplay
{
	[BepInPlugin("com.trae.healthdisplay", "血量显示 HealthDisplay", "26.8.13.3")]
	public class HealthDisplayPlugin : BaseUnityPlugin
	{
		// ==================== 配置分类 ====================
		private const string CatGeneral = "Unturned.Category:通用设置";
		private const string CatFilter = "Unturned.Category:名单过滤";
		private const string CatDisplay = "Unturned.Category:显示设置";
		private const string CatDamage = "Unturned.Category:伤害数字";

		// ==================== 配置项 ====================
		public static ConfigEntry<bool> cfgEnabled;
		public static ConfigEntry<string> cfgListMode;
		public static ConfigEntry<string> cfgWhiteList;
		public static ConfigEntry<string> cfgBlackList;
		public static ConfigEntry<bool> cfgShowZombies;
		public static ConfigEntry<bool> cfgShowAnimals;
		public static ConfigEntry<bool> cfgShowPlayers;
		public static ConfigEntry<string> cfgDisplayMode;
		public static ConfigEntry<bool> cfgShowPercentage;
		public static ConfigEntry<string> cfgDisplayPosition;
		public static ConfigEntry<string> cfgDisplayScope;
		public static ConfigEntry<float> cfgNearbyDistance;
		public static ConfigEntry<float> cfgCrosshairRadius;
		public static ConfigEntry<float> cfgMaxDistance;
		public static ConfigEntry<bool> cfgShowNames;
		public static ConfigEntry<int> cfgNameFontSize;
		public static ConfigEntry<float> cfgBarScaleMin;
		public static ConfigEntry<float> cfgBarScaleMax;
		public static ConfigEntry<bool> cfgOcclusionCheck;
		public static ConfigEntry<bool> cfgShowDamageNumbers;
		public static ConfigEntry<float> cfgDamageLifetime;
		public static ConfigEntry<bool> cfgShowIncomingDamage;

		// ==================== 运行时数据 ====================
		private class DamageNumber
		{
			public bool isWorld;          // true=世界坐标（实体头顶），false=屏幕坐标（自己受伤）
			public Vector3 worldPos;
			public Vector2 screenPos;
			public int value;
			public float startTime;
			public byte kind;             // 0=僵尸 1=动物 2=玩家 3=自己受伤
			public bool isCritical;       // 爆头
		}

		private static List<DamageNumber> damageNumbers = new List<DamageNumber>();
		private const int MAX_DAMAGE_NUMBERS = 60;

		// "命中时"显示范围：本机玩家命中过的目标
		private static HashSet<Zombie> hitZombies = new HashSet<Zombie>();
		private static HashSet<Animal> hitAnimals = new HashSet<Animal>();
		private static HashSet<Player> hitPlayers = new HashSet<Player>();

		// 名单（解析后的 key 列表）
		private static List<string> parsedWhite = new List<string>();
		private static List<string> parsedBlack = new List<string>();
		private static DateTime lastConfigWriteTime;
		private static float nextConfigCheck;

		// 样式缓存
		private static GUIStyle styleBarNumber;
		private static GUIStyle styleBarName;
		private static GUIStyle styleDamage;
		private static GUIStyle styleDamageCrit;
		private static GUIStyle styleHud;
		private static bool stylesCreated;

		// ==================== 生命周期 ====================
		private void Awake()
		{
			try
			{
				// ---- 绑定配置 ----
				cfgEnabled = Config.Bind("General", "Enabled", true, Category(CatGeneral, "插件总开关"));
				cfgListMode = Config.Bind("Filter", "ListMode", "Black",
					Category(CatFilter, "名单模式：Black=黑名单（名单中的不显示），White=白名单（只显示名单中的）",
						new AcceptableValueList<string>("Black", "White"), "Unturned.Cycle"));
				cfgWhiteList = Config.Bind("Filter", "WhiteList", "",
					Category(CatFilter, "白名单生物列表（生物选择器），僵尸 Z:类型名（NORMAL/MEGA 等），动物 A:资产ID，玩家 P:SteamID，支持 * 通配",
						null, "Unturned.CreatureList"));
				cfgBlackList = Config.Bind("Filter", "BlackList", "",
					Category(CatFilter, "黑名单生物列表（生物选择器），僵尸 Z:类型名（NORMAL/MEGA 等），动物 A:资产ID，玩家 P:SteamID，支持 * 通配",
						null, "Unturned.CreatureList"));
				cfgShowZombies = Config.Bind("Filter", "ShowZombies", true, Category(CatFilter, "是否显示僵尸血量"));
				cfgShowAnimals = Config.Bind("Filter", "ShowAnimals", true, Category(CatFilter, "是否显示动物血量"));
				cfgShowPlayers = Config.Bind("Filter", "ShowPlayers", false, Category(CatFilter, "是否显示玩家血量（仅服务器端生效）"));

				cfgDisplayMode = Config.Bind("Display", "DisplayMode", "Both",
					Category(CatDisplay, "显示模式：Both=血条+数字，Bar=仅血条，Number=仅数字",
						new AcceptableValueList<string>("Both", "Bar", "Number"), "Unturned.Cycle"));
				cfgShowPercentage = Config.Bind("Display", "ShowPercentage", false,
					Category(CatDisplay, "数字模式下额外显示百分比（如 75/100 (75%)）"));
				cfgDisplayPosition = Config.Bind("Display", "DisplayPosition", "Head",
					Category(CatDisplay, "显示位置：Head=生物头顶，Crosshair=准星下方（当前瞄准目标），Corner=屏幕左下角",
						new AcceptableValueList<string>("Head", "Crosshair", "Corner"), "Unturned.Cycle"));
				cfgDisplayScope = Config.Bind("Display", "DisplayScope", "All",
					Category(CatDisplay, "显示范围（哪些生物显示血量）：All=距离内所有，Nearby=附近所有（单独配置距离），View=视野内，Crosshair=准心附近（屏幕半径），Hit=命中过的目标，Off=关闭生物血量",
						new AcceptableValueList<string>("All", "Nearby", "View", "Crosshair", "Hit", "Off"), "Unturned.Cycle"));
				cfgNearbyDistance = Config.Bind("Display", "NearbyDistance", 10f,
					Category(CatDisplay, "Nearby 附近所有模式的显示距离（米）", new AcceptableValueRange<float>(1f, 200f)));
				cfgCrosshairRadius = Config.Bind("Display", "CrosshairRadius", 100f,
					Category(CatDisplay, "Crosshair 准心附近模式的屏幕半径（像素），生物投影到屏幕后距准心小于该值则显示", new AcceptableValueRange<float>(10f, 500f)));
				cfgMaxDistance = Config.Bind("Display", "MaxDistance", 30f,
					Category(CatDisplay, "最大显示距离（米），超过该距离不显示", new AcceptableValueRange<float>(5f, 500f)));
				cfgShowNames = Config.Bind("Display", "ShowNames", true,
					Category(CatDisplay, "在血条上方显示名称（僵尸类型 / 动物名 / 玩家名）"));
				cfgNameFontSize = Config.Bind("Display", "NameFontSize", 13,
					Category(CatDisplay, "名称字号（随距离缩放，实际显示=该值×缩放倍率）", new AcceptableValueRange<int>(8, 30)));
				cfgBarScaleMin = Config.Bind("Display", "BarScaleMin", 0.5f,
					Category(CatDisplay, "血条缩放下限（远处最小倍率；以 10 米为 1 倍）", new AcceptableValueRange<float>(0.2f, 1f)));
				cfgBarScaleMax = Config.Bind("Display", "BarScaleMax", 1.8f,
					Category(CatDisplay, "血条缩放上限（近处最大倍率，防止过大）", new AcceptableValueRange<float>(1f, 4f)));
				cfgOcclusionCheck = Config.Bind("Display", "OcclusionCheck", true,
					Category(CatDisplay, "不隔墙显示：目标被墙壁/物体遮挡时不显示血条"));

				cfgShowDamageNumbers = Config.Bind("Damage", "ShowDamageNumbers", true,
					Category(CatDamage, "造成伤害时在目标上方显示伤害数字（服务器端生效）"));
				cfgDamageLifetime = Config.Bind("Damage", "DamageLifetime", 1.2f,
					Category(CatDamage, "伤害数字持续时间（秒）", new AcceptableValueRange<float>(0.3f, 5f)));
				cfgShowIncomingDamage = Config.Bind("Damage", "ShowIncomingDamage", true,
					Category(CatDamage, "自己受到伤害时在屏幕中央显示红色伤害数字"));

				// ---- 订阅伤害事件（服务器端/单机触发）----
				DamageTool.damageZombieRequested += OnDamageZombieRequested;
				DamageTool.damageAnimalRequested += OnDamageAnimalRequested;
				DamageTool.damagePlayerRequested += OnDamagePlayerRequested;

				// ---- Harmony：客户端接收"自己受伤"消息 ----
				try
				{
					Harmony harmony = new Harmony("com.trae.healthdisplay");
					harmony.PatchAll(typeof(HealthDisplayPlugin).Assembly);
				}
				catch (Exception e)
				{
					Logger.LogError("[HealthDisplay] Harmony 初始化失败：" + e);
				}

				ParseLists();

				Logger.LogInfo("[HealthDisplay] 插件启动完成 v26.8.13.3");
			}
			catch (Exception e)
			{
				Logger.LogError("[HealthDisplay] 初始化异常：" + e);
			}
		}

		private void OnDestroy()
		{
			try
			{
				DamageTool.damageZombieRequested -= OnDamageZombieRequested;
				DamageTool.damageAnimalRequested -= OnDamageAnimalRequested;
				DamageTool.damagePlayerRequested -= OnDamagePlayerRequested;
			}
			catch (Exception e)
			{
				Logger.LogError("[HealthDisplay] 反注册异常：" + e);
			}
		}

		private void Update()
		{
			try
			{
				// 清理过期伤害数字
				float now = Time.realtimeSinceStartup;
				for (int i = damageNumbers.Count - 1; i >= 0; i--)
				{
					if (now - damageNumbers[i].startTime > cfgDamageLifetime.Value)
					{
						damageNumbers.RemoveAt(i);
					}
				}

				// 清理已死亡/失效的命中目标
				if (cfgDisplayScope != null && cfgDisplayScope.Value == "Hit")
				{
					hitZombies.RemoveWhere(delegate(Zombie z) { return z == null || z.isDead; });
					hitAnimals.RemoveWhere(delegate(Animal a) { return a == null || a.isDead; });
					hitPlayers.RemoveWhere(delegate(Player p) { return p == null || p.life == null || p.life.isDead; });
				}

				// 配置文件热重载（每 5 秒检查一次）
				if (Time.realtimeSinceStartup >= nextConfigCheck)
				{
					nextConfigCheck = Time.realtimeSinceStartup + 5f;
					if (System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath) != lastConfigWriteTime)
					{
						Config.Reload();
						lastConfigWriteTime = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
						ParseLists();
					}
				}
			}
			catch (Exception e)
			{
				Logger.LogError("[HealthDisplay] Update 异常：" + e);
			}
		}

		// ==================== 配置辅助 ====================
		private static ConfigDescription Category(string categoryTag, string description, params object[] extraTags)
		{
			object[] tags = new object[extraTags == null ? 1 : extraTags.Length + 1];
			tags[0] = categoryTag;
			if (extraTags != null)
			{
				for (int i = 0; i < extraTags.Length; i++) tags[i + 1] = extraTags[i];
			}
			return new ConfigDescription(description, null, tags);
		}

		private static ConfigDescription Category(string categoryTag, string description, AcceptableValueBase acceptableValues, params object[] extraTags)
		{
			object[] tags = new object[extraTags == null ? 1 : extraTags.Length + 1];
			tags[0] = categoryTag;
			if (extraTags != null)
			{
				for (int i = 0; i < extraTags.Length; i++) tags[i + 1] = extraTags[i];
			}
			return new ConfigDescription(description, acceptableValues, tags);
		}

		// ==================== 名单解析 ====================
		private static void ParseLists()
		{
			parsedWhite.Clear();
			parsedBlack.Clear();
			AddEntries(parsedWhite, cfgWhiteList.Value);
			AddEntries(parsedBlack, cfgBlackList.Value);
		}

		private static void AddEntries(List<string> target, string raw)
		{
			if (string.IsNullOrEmpty(raw)) return;
			string[] parts = raw.Split(new char[] { ',', ';', '\n', '\r', ' ', '，', '；' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < parts.Length; i++)
			{
				string entry = parts[i].Trim();
				if (entry.Length > 0 && !target.Contains(entry))
				{
					target.Add(entry);
				}
			}
		}

		/// <summary>
		/// 名单过滤：僵尸（speciality 枚举名 + 类型索引双 key 兼容）。
		/// </summary>
		private static bool IsZombieAllowed(Zombie z)
		{
			if (z == null) return false;
			bool isWhite = cfgListMode.Value == "White";
			if (isWhite && parsedWhite.Count == 0) return false;

			string specKey = "Z:" + z.speciality.ToString().ToUpper();
			string typeKey = "Z:" + z.type.ToString();

			bool inWhite = parsedWhite.Contains("Z:*") || parsedWhite.Contains(specKey) || parsedWhite.Contains(typeKey);
			bool inBlack = parsedBlack.Contains("Z:*") || parsedBlack.Contains(specKey) || parsedBlack.Contains(typeKey);

			return isWhite ? inWhite : !inBlack;
		}

		/// <summary>
		/// 名单过滤：动物（资产 ID）。
		/// </summary>
		private static bool IsAnimalAllowed(Animal a)
		{
			if (a == null || a.asset == null) return false;
			bool isWhite = cfgListMode.Value == "White";
			if (isWhite && parsedWhite.Count == 0) return false;

			string key = "A:" + a.asset.id.ToString();

			bool inWhite = parsedWhite.Contains("A:*") || parsedWhite.Contains(key);
			bool inBlack = parsedBlack.Contains("A:*") || parsedBlack.Contains(key);

			return isWhite ? inWhite : !inBlack;
		}

		/// <summary>
		/// 名单过滤：玩家（SteamID）。
		/// </summary>
		private static bool IsPlayerAllowed(string steamId)
		{
			bool isWhite = cfgListMode.Value == "White";
			if (isWhite && parsedWhite.Count == 0) return false;

			string key = "P:" + steamId;

			bool inWhite = parsedWhite.Contains("P:*") || parsedWhite.Contains(key);
			bool inBlack = parsedBlack.Contains("P:*") || parsedBlack.Contains(key);

			return isWhite ? inWhite : !inBlack;
		}

		/// <summary>
		/// 显示范围过滤：该实体是否显示血量。
		/// </summary>
		private static bool ShouldDisplayEntity(float sqrDistanceToCam, Vector3 worldPos, object entity)
		{
			if (cfgDisplayScope == null) return true;
			string scope = cfgDisplayScope.Value;
			if (scope == "Off") return false;

			float maxDist = cfgMaxDistance.Value;
			if (scope == "Nearby") maxDist = cfgNearbyDistance.Value;
			if (sqrDistanceToCam > maxDist * maxDist) return false;

			if (scope == "All" || scope == "Nearby") return true;

			Vector2 screenPos;
			if (!ProjectWorldToScreen(worldPos, out screenPos)) return false;

			if (scope == "View")
			{
				return screenPos.x >= -40f && screenPos.x <= Screen.width + 40f
					&& screenPos.y >= -40f && screenPos.y <= Screen.height + 40f;
			}

			if (scope == "Crosshair")
			{
				Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
				float radius = cfgCrosshairRadius.Value;
				return (screenPos - center).sqrMagnitude <= radius * radius;
			}

			if (scope == "Hit")
			{
				Zombie z = entity as Zombie;
				if (z != null) return hitZombies.Contains(z);
				Animal a = entity as Animal;
				if (a != null) return hitAnimals.Contains(a);
				Player p = entity as Player;
				if (p != null) return hitPlayers.Contains(p);
				return false;
			}

			return true;
		}

		/// <summary>
		/// 记录本机玩家命中过的目标（Hit 显示范围用）。
		/// </summary>
		private static void RecordHit(object entity)
		{
			Zombie z = entity as Zombie;
			if (z != null) { hitZombies.Add(z); return; }
			Animal a = entity as Animal;
			if (a != null) { hitAnimals.Add(a); return; }
			Player p = entity as Player;
			if (p != null) { hitPlayers.Add(p); }
		}

		// ==================== 伤害事件（服务器端/单机）====================
		private static bool IsFullMode()
		{
			return Provider.isServer && !Dedicator.IsDedicatedServer;
		}

		private void OnDamageZombieRequested(ref DamageZombieParameters parameters, ref bool shouldAllow)
		{
			try
			{
				if (!IsFullMode()) return;
				if (parameters.zombie == null || parameters.zombie.isDead) return;

				// 本机玩家命中的目标（Hit 显示范围）
				Player inst = parameters.instigator as Player;
				if (inst != null && inst == Player.LocalPlayer)
				{
					RecordHit(parameters.zombie);
				}

				if (!cfgShowDamageNumbers.Value) return;

				float times = parameters.times;
				if (parameters.applyGlobalArmorMultiplier)
				{
					if (parameters.limb == ELimb.SKULL) times *= Provider.modeConfigData.Zombies.Armor_Multiplier;
					else times *= Provider.modeConfigData.Zombies.NonHeadshot_Armor_Multiplier;
				}
				int dmg = Mathf.FloorToInt(parameters.damage * times);
				if (dmg <= 0) return;

				float headHeight = parameters.zombie.isMega ? 3.4f : (parameters.zombie.speciality == EZombieSpeciality.CRAWLER ? 1.2f : 2.4f);
				AddWorldDamage(parameters.zombie.transform.position + Vector3.up * headHeight, dmg, 0, parameters.limb == ELimb.SKULL);
			}
			catch (Exception e)
			{
				Logger.LogError("[HealthDisplay] OnDamageZombieRequested 异常：" + e);
			}
		}

		private void OnDamageAnimalRequested(ref DamageAnimalParameters parameters, ref bool shouldAllow)
		{
			try
			{
				if (!IsFullMode()) return;
				if (parameters.animal == null || parameters.animal.isDead) return;

				Player inst = parameters.instigator as Player;
				if (inst != null && inst == Player.LocalPlayer)
				{
					RecordHit(parameters.animal);
				}

				if (!cfgShowDamageNumbers.Value) return;

				float times = parameters.times;
				if (parameters.applyGlobalArmorMultiplier)
				{
					times *= Provider.modeConfigData.Animals.Armor_Multiplier;
				}
				int dmg = Mathf.FloorToInt(parameters.damage * times);
				if (dmg <= 0) return;

				AddWorldDamage(parameters.animal.transform.position + Vector3.up * 1.8f, dmg, 1, false);
			}
			catch (Exception e)
			{
				Logger.LogError("[HealthDisplay] OnDamageAnimalRequested 异常：" + e);
			}
		}

		private void OnDamagePlayerRequested(ref DamagePlayerParameters parameters, ref bool shouldAllow)
		{
			try
			{
				if (!IsFullMode()) return;
				if (parameters.player == null || parameters.player.life == null || parameters.player.life.isDead) return;

				// 本机玩家命中的目标（Hit 显示范围）：受伤者参数用 killer 判断攻击者
				Player local = Player.LocalPlayer;
				if (local != null && local.channel != null && parameters.killer == local.channel.owner.playerID.steamID)
				{
					RecordHit(parameters.player);
				}

				if (!cfgShowDamageNumbers.Value) return;

				float times = parameters.times;
				if (parameters.respectArmor)
				{
					times *= DamageTool.getPlayerArmor(parameters.limb, parameters.player);
				}
				if (parameters.applyGlobalArmorMultiplier)
				{
					times *= Provider.modeConfigData.Players.Armor_Multiplier;
				}
				int dmg = Mathf.FloorToInt(parameters.damage * times);
				if (dmg <= 0) return;

				AddWorldDamage(parameters.player.transform.position + Vector3.up * 2.3f, dmg, 2, parameters.limb == ELimb.SKULL);
			}
			catch (Exception e)
			{
				Logger.LogError("[HealthDisplay] OnDamagePlayerRequested 异常：" + e);
			}
		}

		// ==================== 伤害数字管理 ====================
		private static void AddWorldDamage(Vector3 worldPos, int value, byte kind, bool critical)
		{
			if (damageNumbers.Count >= MAX_DAMAGE_NUMBERS) damageNumbers.RemoveAt(0);
			DamageNumber dn = new DamageNumber();
			dn.isWorld = true;
			dn.worldPos = worldPos;
			dn.value = value;
			dn.startTime = Time.realtimeSinceStartup;
			dn.kind = kind;
			dn.isCritical = critical;
			damageNumbers.Add(dn);
		}

		public static void AddIncomingDamage(int value)
		{
			if (!cfgShowIncomingDamage.Value) return;
			if (damageNumbers.Count >= MAX_DAMAGE_NUMBERS) damageNumbers.RemoveAt(0);
			DamageNumber dn = new DamageNumber();
			dn.isWorld = false;
			dn.screenPos = new Vector2(Screen.width * 0.5f + UnityEngine.Random.Range(-30f, 30f), Screen.height * 0.5f + 60f);
			dn.value = value;
			dn.startTime = Time.realtimeSinceStartup;
			dn.kind = 3;
			dn.isCritical = false;
			damageNumbers.Add(dn);
		}

		// ==================== OnGUI 渲染 ====================
		private void OnGUI()
		{
			try
			{
				if (Dedicator.IsDedicatedServer) return;
				if (cfgEnabled == null || !cfgEnabled.Value) return;
				if (!Level.isLoaded) return;
				if (MainCamera.instance == null) return;

				EnsureStyles();

				// ---- 实体血量 ----
				if (IsFullMode())
				{
					string pos = cfgDisplayPosition.Value;
					if (pos == "Head")
					{
						RenderHeadHealth();
					}
					else if (pos == "Crosshair")
					{
						RenderCrosshairHealth();
					}
					else
					{
						RenderCornerHealth();
					}
				}
				else
				{
					// 客户端模式：降级为屏幕左下角显示自己血量
					RenderCornerHealth();
				}

				// ---- 伤害数字 ----
				if (cfgShowDamageNumbers.Value)
				{
					RenderDamageNumbers();
				}
			}
			catch (Exception e)
			{
				Logger.LogError("[HealthDisplay] OnGUI 异常：" + e);
			}
		}

		private static void EnsureStyles()
		{
			if (stylesCreated) return;
			stylesCreated = true;

			styleBarNumber = new GUIStyle(GUI.skin.label);
			styleBarNumber.fontSize = 11;
			styleBarNumber.alignment = TextAnchor.MiddleCenter;
			styleBarNumber.normal.textColor = Color.white;

			styleBarName = new GUIStyle(GUI.skin.label);
			styleBarName.fontSize = 10;
			styleBarName.alignment = TextAnchor.MiddleCenter;
			styleBarName.normal.textColor = new Color(1f, 1f, 1f, 0.85f);

			styleDamage = new GUIStyle(GUI.skin.label);
			styleDamage.fontSize = 16;
			styleDamage.fontStyle = FontStyle.Bold;
			styleDamage.alignment = TextAnchor.MiddleCenter;

			styleDamageCrit = new GUIStyle(GUI.skin.label);
			styleDamageCrit.fontSize = 22;
			styleDamageCrit.fontStyle = FontStyle.Bold;
			styleDamageCrit.alignment = TextAnchor.MiddleCenter;

			styleHud = new GUIStyle(GUI.skin.label);
			styleHud.fontSize = 14;
			styleHud.fontStyle = FontStyle.Bold;
			styleHud.alignment = TextAnchor.MiddleLeft;
		}

		// ---- 头顶模式 ----
		private static void RenderHeadHealth()
		{
			Camera cam = MainCamera.instance;
			Vector3 camPos = cam.transform.position;

			if (cfgShowZombies.Value)
			{
				List<Zombie> zombies = ZombieManager.AllZombies;
				for (int i = 0; i < zombies.Count; i++)
				{
					Zombie z = zombies[i];
					if (z == null || z.isDead) continue;
					if (!IsZombieAllowed(z)) continue;

					float headHeight = z.isMega ? 3.4f : (z.speciality == EZombieSpeciality.CRAWLER ? 1.2f : 2.4f);
					Vector3 headPos = z.transform.position + Vector3.up * headHeight;
					float sqrDist = (z.transform.position - camPos).sqrMagnitude;
					if (!ShouldDisplayEntity(sqrDist, headPos, z)) continue;

					Vector2 screenPos;
					if (!ProjectWorldToScreen(headPos, out screenPos)) continue;
					if (cfgOcclusionCheck.Value && IsOccluded(camPos, headPos, z.transform)) continue;

					float health = z.GetHealth();
					float maxHealth = z.GetMaxHealth();
					if (maxHealth <= 0f) continue;

					string label = cfgShowNames.Value ? GetZombieName(z) : "";
					DrawHealthBar(screenPos, health, maxHealth, label, new Color(0.9f, 0.75f, 0.15f), Mathf.Sqrt(sqrDist));
				}
			}

			if (cfgShowAnimals.Value)
			{
				List<Animal> animals = AnimalManager.animals;
				for (int i = 0; i < animals.Count; i++)
				{
					Animal a = animals[i];
					if (a == null || a.isDead || a.asset == null) continue;
					if (!IsAnimalAllowed(a)) continue;

					Vector3 headPos = a.transform.position + Vector3.up * 1.8f;
					float sqrDist = (a.transform.position - camPos).sqrMagnitude;
					if (!ShouldDisplayEntity(sqrDist, headPos, a)) continue;

					Vector2 screenPos;
					if (!ProjectWorldToScreen(headPos, out screenPos)) continue;
					if (cfgOcclusionCheck.Value && IsOccluded(camPos, headPos, a.transform)) continue;

					float health = a.GetHealth();
					float maxHealth = a.asset.health;
					if (maxHealth <= 0f) continue;

					string label = cfgShowNames.Value ? a.asset.FriendlyName : "";
					DrawHealthBar(screenPos, health, maxHealth, label, new Color(0.3f, 0.85f, 0.35f), Mathf.Sqrt(sqrDist));
				}
			}

			if (cfgShowPlayers.Value)
			{
				for (int i = 0; i < Provider.clients.Count; i++)
				{
					SteamPlayer sp = Provider.clients[i];
					Player p = sp != null ? sp.player : null;
					if (p == null || p.life == null || p.life.isDead) continue;

					string steamId = sp.playerID.steamID.ToString();
					if (!IsPlayerAllowed(steamId)) continue;

					Vector3 headPos = p.transform.position + Vector3.up * 2.3f;
					float sqrDist = (p.transform.position - camPos).sqrMagnitude;
					if (!ShouldDisplayEntity(sqrDist, headPos, p)) continue;

					Vector2 screenPos;
					if (!ProjectWorldToScreen(headPos, out screenPos)) continue;
					if (cfgOcclusionCheck.Value && IsOccluded(camPos, headPos, p.transform)) continue;

					float health = p.life.health;
					float maxHealth = (int)Provider.modeConfigData.Players.Health_Default;
					if (maxHealth <= 0f) maxHealth = 100f;

					string label = cfgShowNames.Value ? sp.playerID.playerName : "";
					DrawHealthBar(screenPos, health, maxHealth, label, new Color(0.85f, 0.3f, 0.3f), Mathf.Sqrt(sqrDist));
				}
			}
		}

		// ---- 准星模式 ----
		private static void RenderCrosshairHealth()
		{
			Camera cam = MainCamera.instance;
			Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

			Ray ray = cam.ScreenPointToRay(new Vector3(center.x, center.y, 0f));
			RaycastHit hit;
			if (!Physics.Raycast(ray, out hit, cfgMaxDistance.Value)) return;

			Zombie zombie = DamageTool.getZombie(hit.transform);
			Animal animal = DamageTool.getAnimal(hit.transform);
			Player player = DamageTool.getPlayer(hit.transform);

			float health;
			float maxHealth;
			string label = "";
			Color color;

			if (cfgShowZombies.Value && zombie != null)
			{
				if (!IsZombieAllowed(zombie)) return;
				float zHead = zombie.isMega ? 3.4f : (zombie.speciality == EZombieSpeciality.CRAWLER ? 1.2f : 2.4f);
				if (!ShouldDisplayEntity((zombie.transform.position - cam.transform.position).sqrMagnitude, zombie.transform.position + Vector3.up * zHead, zombie)) return;
				health = zombie.GetHealth();
				maxHealth = zombie.GetMaxHealth();
				if (maxHealth <= 0f) return;
				label = cfgShowNames.Value ? GetZombieName(zombie) : "";
				color = new Color(0.9f, 0.75f, 0.15f);
			}
			else if (cfgShowAnimals.Value && animal != null && animal.asset != null)
			{
				if (!IsAnimalAllowed(animal)) return;
				if (!ShouldDisplayEntity((animal.transform.position - cam.transform.position).sqrMagnitude, animal.transform.position + Vector3.up * 1.8f, animal)) return;
				health = animal.GetHealth();
				maxHealth = animal.asset.health;
				if (maxHealth <= 0f) return;
				label = cfgShowNames.Value ? animal.asset.FriendlyName : "";
				color = new Color(0.3f, 0.85f, 0.35f);
			}
			else if (cfgShowPlayers.Value && player != null)
			{
				string steamId = player.channel != null ? player.channel.owner.playerID.steamID.ToString() : "";
				if (!IsPlayerAllowed(steamId)) return;
				if (!ShouldDisplayEntity((player.transform.position - cam.transform.position).sqrMagnitude, player.transform.position + Vector3.up * 2.3f, player)) return;
				health = player.life.health;
				maxHealth = (int)Provider.modeConfigData.Players.Health_Default;
				if (maxHealth <= 0f) maxHealth = 100f;
				SteamPlayer sp = player.channel != null ? player.channel.owner : null;
				label = (cfgShowNames.Value && sp != null) ? sp.playerID.playerName : "";
				color = new Color(0.85f, 0.3f, 0.3f);
			}
			else
			{
				return;
			}

			Vector2 pos = new Vector2(center.x, center.y + 40f);
			DrawHealthBar(pos, health, maxHealth, label, color, hit.distance);
		}

		// ---- 角落模式（自己血量 HUD + 准星目标）----
		private static void RenderCornerHealth()
		{
			Player local = Player.LocalPlayer;
			if (local != null && local.life != null && !local.life.isDead)
			{
				float health = local.life.health;
				float maxHealth = (int)Provider.modeConfigData.Players.Health_Default;
				if (maxHealth <= 0f) maxHealth = 100f;

				Vector2 pos = new Vector2(20f, Screen.height - 70f);
				DrawHudHealth(pos, health, maxHealth);
			}

			// 完整模式：角落模式额外显示准星目标
			if (IsFullMode())
			{
				RenderCrosshairHealthAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f + 40f));
			}
		}

		private static void RenderCrosshairHealthAt(Vector2 drawPos)
		{
			Camera cam = MainCamera.instance;
			Ray ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
			RaycastHit hit;
			if (!Physics.Raycast(ray, out hit, cfgMaxDistance.Value)) return;

			Zombie zombie = DamageTool.getZombie(hit.transform);
			Animal animal = DamageTool.getAnimal(hit.transform);
			Player player = DamageTool.getPlayer(hit.transform);

			float health;
			float maxHealth;
			string label = "";
			Color color;

			if (cfgShowZombies.Value && zombie != null)
			{
				if (!IsZombieAllowed(zombie)) return;
				float zHead = zombie.isMega ? 3.4f : (zombie.speciality == EZombieSpeciality.CRAWLER ? 1.2f : 2.4f);
				if (!ShouldDisplayEntity((zombie.transform.position - cam.transform.position).sqrMagnitude, zombie.transform.position + Vector3.up * zHead, zombie)) return;
				health = zombie.GetHealth();
				maxHealth = zombie.GetMaxHealth();
				if (maxHealth <= 0f) return;
				label = cfgShowNames.Value ? GetZombieName(zombie) : "";
				color = new Color(0.9f, 0.75f, 0.15f);
			}
			else if (cfgShowAnimals.Value && animal != null && animal.asset != null)
			{
				if (!IsAnimalAllowed(animal)) return;
				if (!ShouldDisplayEntity((animal.transform.position - cam.transform.position).sqrMagnitude, animal.transform.position + Vector3.up * 1.8f, animal)) return;
				health = animal.GetHealth();
				maxHealth = animal.asset.health;
				if (maxHealth <= 0f) return;
				label = cfgShowNames.Value ? animal.asset.FriendlyName : "";
				color = new Color(0.3f, 0.85f, 0.35f);
			}
			else if (cfgShowPlayers.Value && player != null)
			{
				string steamId = player.channel != null ? player.channel.owner.playerID.steamID.ToString() : "";
				if (!IsPlayerAllowed(steamId)) return;
				if (!ShouldDisplayEntity((player.transform.position - cam.transform.position).sqrMagnitude, player.transform.position + Vector3.up * 2.3f, player)) return;
				health = player.life.health;
				maxHealth = (int)Provider.modeConfigData.Players.Health_Default;
				if (maxHealth <= 0f) maxHealth = 100f;
				SteamPlayer sp = player.channel != null ? player.channel.owner : null;
				label = (cfgShowNames.Value && sp != null) ? sp.playerID.playerName : "";
				color = new Color(0.85f, 0.3f, 0.3f);
			}
			else
			{
				return;
			}

			DrawHealthBar(drawPos, health, maxHealth, label, color, hit.distance);
		}

		// ---- 通用血条绘制（随距离缩放）----
		private static void DrawHealthBar(Vector2 pos, float health, float maxHealth, string label, Color color, float distance)
		{
			if (maxHealth <= 0f) return;
			float ratio = Mathf.Clamp01(health / maxHealth);
			float scale = GetBarScale(distance);

			string mode = cfgDisplayMode.Value;
			bool showBar = mode == "Both" || mode == "Bar";
			bool showNumber = mode == "Both" || mode == "Number";

			float barWidth = 64f * scale;
			float barHeight = 6f * scale;
			float totalHeight = showBar ? barHeight : 0f;
			if (showNumber) totalHeight += 14f * scale;
			if (label.Length > 0) totalHeight += 12f * scale;

			float y = pos.y - totalHeight;

			if (label.Length > 0)
			{
				styleBarName.fontSize = Mathf.Max(8, Mathf.RoundToInt(cfgNameFontSize.Value * scale));
				GUI.Label(new Rect(pos.x - 80f * scale, y, 160f * scale, 12f * scale), label, styleBarName);
				y += 12f * scale;
			}

			if (showBar)
			{
				// 背景
				Color old = GUI.color;
				GUI.color = new Color(0f, 0f, 0f, 0.55f);
				GUI.DrawTexture(new Rect(pos.x - barWidth * 0.5f, y, barWidth, barHeight), Texture2D.whiteTexture);
				// 血量条
				if (ratio > 0f)
				{
					GUI.color = color;
					GUI.DrawTexture(new Rect(pos.x - barWidth * 0.5f, y, barWidth * ratio, barHeight), Texture2D.whiteTexture);
				}
				GUI.color = old;
				y += barHeight;
			}

			if (showNumber)
			{
				string text;
				if (cfgShowPercentage.Value)
				{
					int percent = Mathf.FloorToInt(ratio * 100f);
					text = Mathf.CeilToInt(health).ToString() + "/" + Mathf.CeilToInt(maxHealth).ToString() + " (" + percent.ToString() + "%)";
				}
				else
				{
					text = Mathf.CeilToInt(health).ToString() + "/" + Mathf.CeilToInt(maxHealth).ToString();
				}
				styleBarNumber.fontSize = Mathf.Max(8, Mathf.RoundToInt(11f * scale));
				GUI.Label(new Rect(pos.x - 80f * scale, y, 160f * scale, 14f * scale), text, styleBarNumber);
			}
		}

		/// <summary>
		/// 血条缩放倍率：以 10 米为 1 倍，近大远小，限制在 [BarScaleMin, BarScaleMax]。
		/// </summary>
		private static float GetBarScale(float distance)
		{
			float scale = 10f / Mathf.Max(distance, 0.1f);
			return Mathf.Clamp(scale, cfgBarScaleMin.Value, cfgBarScaleMax.Value);
		}

		/// <summary>
		/// 隔墙检测：相机到目标头部之间被其他物体（非目标自身、非本地玩家）遮挡。
		/// </summary>
		private static bool IsOccluded(Vector3 from, Vector3 to, Transform ignore)
		{
			RaycastHit hit;
			if (!Physics.Linecast(from, to, out hit)) return false;
			if (hit.transform == null) return false;

			if (ignore != null && hit.transform.IsChildOf(ignore)) return false;

			Player local = Player.LocalPlayer;
			if (local != null && hit.transform.IsChildOf(local.transform)) return false;

			return true;
		}

		// ---- HUD 血条（角落）----
		private static void DrawHudHealth(Vector2 pos, float health, float maxHealth)
		{
			if (maxHealth <= 0f) return;
			float ratio = Mathf.Clamp01(health / maxHealth);

			string text = "生命 " + Mathf.CeilToInt(health).ToString() + "/" + Mathf.CeilToInt(maxHealth).ToString();
			GUI.Label(new Rect(pos.x, pos.y, 220f, 20f), text, styleHud);

			float barWidth = 200f;
			float barHeight = 8f;
			Color old = GUI.color;
			GUI.color = new Color(0f, 0f, 0f, 0.55f);
			GUI.DrawTexture(new Rect(pos.x, pos.y + 20f, barWidth, barHeight), Texture2D.whiteTexture);
			if (ratio > 0f)
			{
				GUI.color = Color.Lerp(new Color(0.85f, 0.2f, 0.2f), new Color(0.2f, 0.85f, 0.35f), ratio);
				GUI.DrawTexture(new Rect(pos.x, pos.y + 20f, barWidth * ratio, barHeight), Texture2D.whiteTexture);
			}
			GUI.color = old;
		}

		// ---- 伤害数字绘制 ----
		private static void RenderDamageNumbers()
		{
			Camera cam = MainCamera.instance;
			float now = Time.realtimeSinceStartup;

			for (int i = 0; i < damageNumbers.Count; i++)
			{
				DamageNumber dn = damageNumbers[i];
				float age = now - dn.startTime;
				float life = cfgDamageLifetime.Value;
				if (age < 0f || age > life) continue;

				Vector2 pos;
				if (dn.isWorld)
				{
					Vector3 p = dn.worldPos + Vector3.up * (age * 0.8f);
					if (!ProjectWorldToScreen(p, out pos)) continue;
				}
				else
				{
					pos = dn.screenPos + new Vector2(0f, -age * 40f);
				}

				float alpha = 1f - (age / life);
				alpha = Mathf.Clamp01(alpha * 2f);

				Color color;
				GUIStyle style;
				switch (dn.kind)
				{
					case 1: color = new Color(0.4f, 1f, 0.45f); break;
					case 2: color = new Color(1f, 0.35f, 0.35f); break;
					case 3: color = new Color(1f, 0.25f, 0.25f); break;
					default: color = new Color(1f, 0.85f, 0.2f); break;
				}

				if (dn.kind == 3)
				{
					style = styleDamage;
				}
				else if (dn.isCritical)
				{
					style = styleDamageCrit;
					color = new Color(1f, 0.55f, 0.1f);
				}
				else
				{
					style = styleDamage;
				}

				color.a = alpha;
				style.normal.textColor = color;

				string text = "-" + dn.value.ToString();
				GUI.Label(new Rect(pos.x - 60f, pos.y - 12f, 120f, 24f), text, style);
			}
		}

		// ==================== 工具 ====================
		private static bool ProjectWorldToScreen(Vector3 worldPos, out Vector2 screenPos)
		{
			screenPos = Vector2.zero;
			Camera cam = MainCamera.instance;
			if (cam == null) return false;

			Vector3 sp = cam.WorldToScreenPoint(worldPos);
			if (sp.z <= 0.05f) return false; // 在相机后方
			screenPos = new Vector2(sp.x, Screen.height - sp.y);
			return true;
		}

		private static string GetZombieName(Zombie z)
		{
			if (z == null) return "";
			switch (z.speciality)
			{
				case EZombieSpeciality.MEGA: return "巨型僵尸";
				case EZombieSpeciality.SPRINTER: return "疾跑者";
				case EZombieSpeciality.CRAWLER: return "爬行者";
				case EZombieSpeciality.ACID: return "酸性僵尸";
				case EZombieSpeciality.BURNER: return "燃烧者";
				case EZombieSpeciality.SPIRIT: return "幽灵";
				case EZombieSpeciality.FLANKER_FRIENDLY: return "潜行者";
				case EZombieSpeciality.FLANKER_STALK: return "潜行者";
				default:
					if (z.isBoss) return "Boss僵尸";
					return "僵尸";
			}
		}
	}

	// ==================== Harmony：自己受伤 ====================
	[HarmonyPatch(typeof(PlayerLife), "ReceiveDamagedEvent")]
	public static class ReceiveDamagedEventPatch
	{
		public static void Postfix(PlayerLife __instance, byte damageAmount)
		{
			try
			{
				if (HealthDisplayPlugin.cfgEnabled == null || !HealthDisplayPlugin.cfgEnabled.Value) return;
				if (damageAmount <= 0) return;
				if (Dedicator.IsDedicatedServer) return;

				// 只有本地玩家受伤才提示
				if (__instance == null || __instance.player == null) return;
				Player local = Player.LocalPlayer;
				if (local == null || __instance.player != local) return;

				HealthDisplayPlugin.AddIncomingDamage(damageAmount);
			}
			catch (Exception)
			{
				// 忽略 patch 内部异常，避免影响游戏
			}
		}
	}
}
