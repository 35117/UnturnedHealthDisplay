// ============================================================
//  HealthDisplay.cs  —  血量显示插件（Unturned BepInEx 5）
//  作者：35117+Deepseek-v4-flash-0731
//  版本：v26.8.13.1
//
//  功能：
//   - 显示生物血量（数字 / 血条 / 两者）
//   - 显示位置：头顶 / 准星下方 / 屏幕角落
//   - 黑白名单过滤（僵尸类型 Z:x、动物资产 A:x、玩家 P:SteamID，支持 * 通配）
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
	[BepInPlugin("com.trae.healthdisplay", "血量显示 HealthDisplay", "26.8.13.1")]
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
		public static ConfigEntry<float> cfgMaxDistance;
		public static ConfigEntry<bool> cfgShowNames;
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
					Category(CatFilter, "白名单列表，每行一个，支持 Z:僵尸类型ID / A:动物资产ID / P:玩家SteamID / 类型:* 通配"));
				cfgBlackList = Config.Bind("Filter", "BlackList", "",
					Category(CatFilter, "黑名单列表，每行一个，支持 Z:僵尸类型ID / A:动物资产ID / P:玩家SteamID / 类型:* 通配"));
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
				cfgMaxDistance = Config.Bind("Display", "MaxDistance", 30f,
					Category(CatDisplay, "最大显示距离（米），超过该距离不显示", new AcceptableValueRange<float>(5f, 500f)));
				cfgShowNames = Config.Bind("Display", "ShowNames", true,
					Category(CatDisplay, "在血条上方显示名称（僵尸类型 / 动物名 / 玩家名）"));

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

				Logger.LogInfo("[HealthDisplay] 插件启动完成 v26.8.13.1");
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
		/// 名单过滤：返回该 key 是否允许显示。
		/// </summary>
		private static bool IsAllowed(string key, string categoryWildcard)
		{
			bool isWhiteMode = cfgListMode.Value == "White";

			bool inWhite = parsedWhite.Contains(key) || parsedWhite.Contains(categoryWildcard);
			bool inBlack = parsedBlack.Contains(key) || parsedBlack.Contains(categoryWildcard);

			if (isWhiteMode)
			{
				// 白名单为空 → 什么都不显示
				if (parsedWhite.Count == 0) return false;
				return inWhite;
			}
			else
			{
				return !inBlack;
			}
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
				if (!cfgShowDamageNumbers.Value || !IsFullMode()) return;
				if (parameters.zombie == null || parameters.zombie.isDead) return;

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
				if (!cfgShowDamageNumbers.Value || !IsFullMode()) return;
				if (parameters.animal == null || parameters.animal.isDead) return;

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
				if (!cfgShowDamageNumbers.Value || !IsFullMode()) return;
				if (parameters.player == null || parameters.player.life == null || parameters.player.life.isDead) return;

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
			float maxDistSqr = cfgMaxDistance.Value * cfgMaxDistance.Value;

			if (cfgShowZombies.Value)
			{
				List<Zombie> zombies = ZombieManager.AllZombies;
				for (int i = 0; i < zombies.Count; i++)
				{
					Zombie z = zombies[i];
					if (z == null || z.isDead) continue;
					if ((z.transform.position - camPos).sqrMagnitude > maxDistSqr) continue;

					string key = "Z:" + z.type.ToString();
					if (!IsAllowed(key, "Z:*")) continue;

					float headHeight = z.isMega ? 3.4f : (z.speciality == EZombieSpeciality.CRAWLER ? 1.2f : 2.4f);
					Vector2 screenPos;
					if (!ProjectWorldToScreen(z.transform.position + Vector3.up * headHeight, out screenPos)) continue;

					float health = z.GetHealth();
					float maxHealth = z.GetMaxHealth();
					if (maxHealth <= 0f) continue;

					string label = cfgShowNames.Value ? GetZombieName(z) : "";
					DrawHealthBar(screenPos, health, maxHealth, label, new Color(0.9f, 0.75f, 0.15f));
				}
			}

			if (cfgShowAnimals.Value)
			{
				List<Animal> animals = AnimalManager.animals;
				for (int i = 0; i < animals.Count; i++)
				{
					Animal a = animals[i];
					if (a == null || a.isDead || a.asset == null) continue;
					if ((a.transform.position - camPos).sqrMagnitude > maxDistSqr) continue;

					string key = "A:" + a.asset.id.ToString();
					if (!IsAllowed(key, "A:*")) continue;

					Vector2 screenPos;
					if (!ProjectWorldToScreen(a.transform.position + Vector3.up * 1.8f, out screenPos)) continue;

					float health = a.GetHealth();
					float maxHealth = a.asset.health;
					if (maxHealth <= 0f) continue;

					string label = cfgShowNames.Value ? a.asset.FriendlyName : "";
					DrawHealthBar(screenPos, health, maxHealth, label, new Color(0.3f, 0.85f, 0.35f));
				}
			}

			if (cfgShowPlayers.Value)
			{
				for (int i = 0; i < Provider.clients.Count; i++)
				{
					SteamPlayer sp = Provider.clients[i];
					Player p = sp != null ? sp.player : null;
					if (p == null || p.life == null || p.life.isDead) continue;
					if ((p.transform.position - camPos).sqrMagnitude > maxDistSqr) continue;

					string steamId = sp.playerID.steamID.ToString();
					string key = "P:" + steamId;
					if (!IsAllowed(key, "P:*")) continue;

					Vector2 screenPos;
					if (!ProjectWorldToScreen(p.transform.position + Vector3.up * 2.3f, out screenPos)) continue;

					float health = p.life.health;
					float maxHealth = (int)Provider.modeConfigData.Players.Health_Default;
					if (maxHealth <= 0f) maxHealth = 100f;

					string label = cfgShowNames.Value ? sp.playerID.playerName : "";
					DrawHealthBar(screenPos, health, maxHealth, label, new Color(0.85f, 0.3f, 0.3f));
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
				health = zombie.GetHealth();
				maxHealth = zombie.GetMaxHealth();
				if (maxHealth <= 0f) return;
				label = cfgShowNames.Value ? GetZombieName(zombie) : "";
				color = new Color(0.9f, 0.75f, 0.15f);
			}
			else if (cfgShowAnimals.Value && animal != null && animal.asset != null)
			{
				health = animal.GetHealth();
				maxHealth = animal.asset.health;
				if (maxHealth <= 0f) return;
				label = cfgShowNames.Value ? animal.asset.FriendlyName : "";
				color = new Color(0.3f, 0.85f, 0.35f);
			}
			else if (cfgShowPlayers.Value && player != null)
			{
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
			DrawHealthBar(pos, health, maxHealth, label, color);
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
				health = zombie.GetHealth();
				maxHealth = zombie.GetMaxHealth();
				if (maxHealth <= 0f) return;
				label = cfgShowNames.Value ? GetZombieName(zombie) : "";
				color = new Color(0.9f, 0.75f, 0.15f);
			}
			else if (cfgShowAnimals.Value && animal != null && animal.asset != null)
			{
				health = animal.GetHealth();
				maxHealth = animal.asset.health;
				if (maxHealth <= 0f) return;
				label = cfgShowNames.Value ? animal.asset.FriendlyName : "";
				color = new Color(0.3f, 0.85f, 0.35f);
			}
			else if (cfgShowPlayers.Value && player != null)
			{
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

			DrawHealthBar(drawPos, health, maxHealth, label, color);
		}

		// ---- 通用血条绘制 ----
		private static void DrawHealthBar(Vector2 pos, float health, float maxHealth, string label, Color color)
		{
			if (maxHealth <= 0f) return;
			float ratio = Mathf.Clamp01(health / maxHealth);

			string mode = cfgDisplayMode.Value;
			bool showBar = mode == "Both" || mode == "Bar";
			bool showNumber = mode == "Both" || mode == "Number";

			float barWidth = 64f;
			float barHeight = 6f;
			float totalHeight = showBar ? barHeight : 0f;
			if (showNumber) totalHeight += 14f;
			if (label.Length > 0) totalHeight += 12f;

			float y = pos.y - totalHeight;

			if (label.Length > 0)
			{
				GUI.Label(new Rect(pos.x - 80f, y, 160f, 12f), label, styleBarName);
				y += 12f;
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
				GUI.Label(new Rect(pos.x - 80f, y, 160f, 14f), text, styleBarNumber);
			}
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
